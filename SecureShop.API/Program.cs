using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SecureShop.API.Middleware;
using SecureShop.Application;
using SecureShop.Infrastructure;

// ── Early crash handler — visible in Railway deploy logs before Serilog starts ──
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Console.Error.WriteLine($"[FATAL] UnhandledException: {e.ExceptionObject}");
Console.WriteLine("[STARTUP] Process started");

var builder = WebApplication.CreateBuilder(args);
Console.WriteLine($"[STARTUP] Environment: {builder.Environment.EnvironmentName}");

// ── Logging ──────────────────────────────────────────────────────────────────
try
{
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();
    builder.Host.UseSerilog();
    Console.WriteLine("[STARTUP] Serilog configured");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] Serilog configuration failed: {ex}");
    // Fall back to default logging so the app can still start
}

// ── Application & Infrastructure services ────────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured. Set it in Railway environment variables.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
// README: 100 req/min API, 10 req/min auth
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit          = 100;
        opt.Window               = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit           = 5;
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

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "SecureShop API",
        Version     = "v1",
        Description = "Production-ready e-commerce REST API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        Description  = "Enter your JWT token"
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

// ── Port (Railway injects PORT env var) ──────────────────────────────────────
// Use TryParse — int.Parse throws FormatException if PORT has whitespace or
// unexpected content, which crashes the process before binding any port.
var portRaw = Environment.GetEnvironmentVariable("PORT")?.Trim();
var port = int.TryParse(portRaw, out var p) && p > 0 ? p : 8080;
Console.WriteLine($"[STARTUP] Binding to port {port} (PORT env={portRaw ?? "(not set)"})");
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(port);
});

// ═════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ═════════════════════════════════════════════════════════════════════════════

// ── Middleware pipeline (ORDER MATTERS) ───────────────────────────────────────

// 1. Security headers — outermost so every response gets them
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Global exception handler
app.UseMiddleware<GlobalExceptionMiddleware>();

// 3. Swagger — Enable in all environments for Railway
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SecureShop API v1");
    c.RoutePrefix  = "swagger";
    c.DocumentTitle = "SecureShop API Docs";
    if (app.Environment.IsDevelopment())
    {
        c.InjectStylesheet("/swagger-ui/custom.css");
    }
    c.EnableDeepLinking();
});

// 4. Static files (serves /swagger-ui/custom.css etc.)
app.UseStaticFiles();

// 5. Railway terminates TLS at the load balancer; the container only sees plain
//    HTTP. UseHttpsRedirection and UseHsts must NOT run inside the container —
//    they cause redirect loops behind a TLS-terminating proxy.

// 6. Framework middleware
app.UseCors("Production");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapControllers();

// Health check — available in all environments for Railway
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Root endpoint - Redirect to Swagger documentation
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/docs", () => Results.Redirect("/swagger"));

// API status endpoint (for monitoring/health checks)
app.MapGet("/api", () =>
{
    var response = new
    {
        service   = "SecureShop API",
        version   = "1.0.0",
        status    = "running",
        timestamp = DateTime.UtcNow,
        endpoints = new 
        { 
            swagger = "/swagger",
            health = "/health",
            docs = "/docs"
        }
    };
    return Results.Json(response);
});

// ── Database migration & seeding (background — runs AFTER Kestrel starts) ────
// Running MigrateAsync before app.Run() blocks the port from opening, causing
// Railway health checks to fail while EF Core retries the connection.
Log.Information("Startup init: finished, starting web host");

_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(2000); // give Kestrel a moment to bind
        using var scope = app.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Startup init: database migration completed");

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in new[] { "Admin", "Customer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                Log.Information("Startup init: created role {Role}", role);
            }
        }

        Log.Information("Startup init: role seeding completed");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Startup init: migration/seeding failed — ensure ConnectionStrings__DefaultConnection is set in Railway env vars");
    }
});

app.Run();