using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SecureShop.API.Middleware;
using SecureShop.Application;
using SecureShop.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// ── Application & Infrastructure services ────────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "PLACEHOLDER_SET_JWT_SECRET_IN_RAILWAY_ENV";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer   = true, ValidIssuer   = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true, ClockSkew     = TimeSpan.Zero
        };
    });

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit            = 100;
        opt.Window                 = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder   = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit             = 5;
    });
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window      = TimeSpan.FromMinutes(1);
        opt.QueueLimit  = 0;
    });
    options.RejectionStatusCode = 429;
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        var origins = builder.Configuration["AllowedOrigins"]?.Split(',')
                      ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

// ── MVC + Swagger ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SecureShop API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your JWT token"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── Port (Railway injects PORT env var) ─────────────────────────────────────
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ═════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ═════════════════════════════════════════════════════════════════════════════

// ── 1. Global exception handler — ALWAYS first ────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

// ── 2. Swagger — Development only ────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options => { options.SerializeAsV2 = false; });

    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (path == "/swagger" || path == "/swagger/" || path == "/swagger/index.html")
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";

            await context.Response.WriteAsync("""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>SecureShop API Docs</title>
    <link rel="stylesheet" href="./swagger-ui.css?v=20260407" />
    <link rel="stylesheet" href="/swagger-ui/custom.css" />
</head>
<body>
    <div id="swagger-ui"></div>
    <script src="./swagger-ui-bundle.js?v=20260407"></script>
    <script src="./swagger-ui-standalone-preset.js?v=20260407"></script>
    <script>
        window.onload = function () {
            window.ui = SwaggerUIBundle({
                url: '/swagger/v1/swagger.json?v=20260407',
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
                layout: 'StandaloneLayout'
            });
        };
    </script>
</body>
</html>
""");
            return;
        }

        await next();
    });

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SecureShop API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "SecureShop API Docs";
        c.InjectStylesheet("/swagger-ui/custom.css");
        c.ConfigObject.DeepLinking = true;
        c.EnableDeepLinking();
    });
}

// ── 3. Static files (serves /swagger-ui/custom.css etc.) ─────────────────────
app.UseStaticFiles();

// ── 4. Custom middleware — AFTER Swagger ──────────────────────────────────────
//      FIX: was before UseSwagger; now safe because CacheBustingMiddleware
//      skips /swagger and /docs paths internally
app.UseMiddleware<CacheBustingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// ── 5. HTTPS / HSTS ──────────────────────────────────────────────────────────
// Railway terminates TLS at the load balancer; the container only sees plain
// HTTP.  Enabling UseHttpsRedirection here would 301-redirect Railway's health
// checker and every real request, breaking everything.  HSTS is also handled
// externally on Railway, so both are intentionally omitted.

// ── 6. Framework middleware ───────────────────────────────────────────────────
app.UseCors("Production");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── 7. Endpoints ──────────────────────────────────────────────────────────────
app.MapControllers();

// Health check — available in all environments for Railway
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Docs endpoints (development only)
if (app.Environment.IsDevelopment())
{
    app.MapGet("/swagger.json", () => Results.Redirect("/swagger/v1/swagger.json"));
    app.MapGet("/", () => Results.Redirect("/docs-new"));
    app.MapGet("/docs", () => Results.Redirect("/swagger/"));
    app.MapGet("/index.html", () => Results.Redirect("/swagger/"));
    app.MapGet("/docs-new", (HttpRequest req) =>
    {
        var origin = $"{req.Scheme}://{req.Host}";
        var specUrl = $"{origin}/swagger/v1/swagger.json?v=20260408";
        var html = $$"""
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>SecureShop API Docs (Isolated)</title>
    <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
</head>
<body>
    <div id="swagger-ui"></div>
    <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
    <script>
        window.ui = SwaggerUIBundle({
            url: "{{specUrl}}",
            dom_id: '#swagger-ui',
            deepLinking: true,
            presets: [SwaggerUIBundle.presets.apis],
            layout: 'BaseLayout'
        });
    </script>
</body>
</html>
""";

        return Results.Content(html, "text/html");
    });
}
else
{
    app.MapGet("/", () => Results.NotFound());
}

// ── Database migration & seeding ─────────────────────────────────────────────
Log.Information("Startup init: database migration starting");
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    Log.Information("Startup init: database migration completed");

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "User" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    Log.Information("Startup init: role seeding completed");
}
catch (Exception ex)
{
    Log.Error(ex, "Startup init: database migration failed — ensure ConnectionStrings__DefaultConnection is set in Railway env vars");
}

Log.Information("Startup init: finished, starting web host");
app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    if (addresses is not null)
    {
        foreach (var address in addresses)
        {
            Log.Information("Listening on {Address}", address);
        }
    }
});
app.Run();