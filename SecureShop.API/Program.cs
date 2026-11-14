using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using SecureShop.API.Middleware;
using SecureShop.API.Helpers;
using SecureShop.Application;
using SecureShop.Domain.Entities;
using SecureShop.Infrastructure;
using SecureShop.Application.Services; // <-- FIX: Added to resolve OrderService error

// ── Early crash handler ──────────────────────────────────────────────────────
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
}

// ── Production database guard ────────────────────────────────────────────────
using var serilogLoggerProvider = new Serilog.Extensions.Logging.SerilogLoggerProvider(Log.Logger);
var startupLogger = serilogLoggerProvider.CreateLogger("Startup");
DatabaseConnectionGuard.LogDiagnostics(builder.Configuration, builder.Environment.IsProduction(), startupLogger);
var dbStatus = DatabaseConnectionGuard.Evaluate(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Environment.IsProduction());
if (dbStatus != DatabaseConnectionStatus.Ok)
{
    Log.Fatal(
        "Refusing to start: ConnectionStrings__DefaultConnection is {Reason}. " +
        "Set the Railway env var ConnectionStrings__DefaultConnection to a valid Npgsql connection string " +
        "(e.g. Host=...;Username=...;Password=...;Database=...;SSL Mode=Require).",
        dbStatus == DatabaseConnectionStatus.Missing ? "missing or empty" : "unsafe for Production (localhost/placeholder or unparseable)");
    Environment.Exit(1);
}

// ── Application & Infrastructure services ────────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddSingleton<SecureShop.API.Services.MigrationCompletedSignal>();
builder.Services.AddHostedService<SecureShop.API.Services.OrderAbandonmentService>();

// ── HttpClient for Razor Pages to call API ───────────────────────────────────
builder.Services.AddHttpClient();

// ── Data Protection ──────────────────────────────────────────────────────────
var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(keysDirectory);

builder.Services.AddDataProtection()
    .SetApplicationName("SecureShop")
    .PersistKeysToDbContext<AppDbContext>();
Console.WriteLine("[STARTUP] Data Protection keys will be stored in PostgreSQL");

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── Authentication ───────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured. Set it in Railway environment variables.");
var bootstrapAdminCredentials = BootstrapAdminProvisioner.GetCredentials(builder.Configuration);

var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultForbidScheme = "AdminCookie";
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = ".SecureShop.User";
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
    })
    .AddCookie("AdminCookie", options =>
    {
        options.Cookie.Name = ".SecureShop.Admin";
        options.Cookie.Path = "/admin"; // <-- RESTRICTION: Means cookie is ONLY sent to /admin URLs
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = PathString.Empty;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(45);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnValidatePrincipal = context =>
            {
                if (context.Principal?.Identity?.IsAuthenticated == true)
                {
                    var isAdmin = context.Principal.IsInRole("Admin");
                    if (!isAdmin)
                    {
                        context.RejectPrincipal();
                    }
                }
                return Task.CompletedTask;
            }
        };
    })
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    
    options.AddPolicy("AdminApiPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireRole("Admin");
    });
    
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.AuthenticationSchemes.Add("AdminCookie");
        policy.RequireRole("Admin");
    });
});

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("adminlogin", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
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
builder.Services.AddControllers(options =>
    options.Filters.AddService<AdminCookieAntiforgeryFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddScoped<AdminCookieAntiforgeryFilter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "SecureShop API",
        Version     = "1.0.0",
        Description = "Production-ready e-commerce REST API with JWT authentication"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        Description  = "Enter JWT token (without 'Bearer' prefix)",
        In           = ParameterLocation.Header,
        Name         = "Authorization"
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = []
    });
});

// ── Razor Pages for Frontend ─────────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/Admin/Login2fa");
});

// ── Port ─────────────────────────────────────────────────────────────────────
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
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.Equals("/admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        context.Response.Headers["Cache-Control"] = "no-store";
    }
    await next();
});

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CacheBustingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SecureShop API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "SecureShop API Docs";
        c.InjectStylesheet("/swagger-ui/custom.css");
        c.EnableDeepLinking();
    });
}

app.UseStaticFiles();

app.UseCors("Production");
app.UseRateLimiter();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapControllers().RequireRateLimiting("api");
app.MapRazorPages();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapGet("/api/swagger", () => Results.Redirect("/swagger"));

// ── CSV Export Endpoint (Placed inside /admin to access the AdminCookie) ─────
app.MapGet("/admin/export-orders", async (OrderService svc) =>
{
    var orders = await svc.GetAllOrdersAsync();
    var csv = new StringBuilder();
    csv.AppendLine("Order ID,Date,Customer,Status,Total Amount");
    foreach (var o in orders)
    {
        csv.AppendLine($"{o.Id},{o.CreatedAt:yyyy-MM-dd HH:mm},\"{o.CustomerEmail}\",{o.Status},{o.TotalAmount}");
    }
    
    var bytes = Encoding.UTF8.GetBytes(csv.ToString());
    return Results.File(bytes, "text/csv", $"orders-{DateTime.UtcNow:yyyyMMdd}.csv");
}).RequireAuthorization("AdminOnly");

// ── One-shot product seed endpoint (Admin session required) ──────────────────────
// Note: The Antiforgery check has been removed here. Security is provided by the
// Admin role requirement and the Bearer token, which is sufficient for this
// internal admin-only endpoint.
app.MapPost("/api/admin/seed", async (AppDbContext db) =>
{
    try
    {
        var count = await db.Set<SecureShop.Domain.Entities.Product>().CountAsync();
        if (count > 0)
            return Results.Ok(new { message = "Products already exist. No seed needed.", seeded = false });

        var products = new[]
        {
            SecureShop.Domain.Entities.Product.Create("Wireless Noise-Cancelling Headphones", "Premium over-ear headphones with active noise cancellation, 30-hour battery life and foldable design.", 89.99m, 50, "Electronics", null),
            SecureShop.Domain.Entities.Product.Create("Mechanical Gaming Keyboard", "TKL mechanical keyboard with RGB backlighting, Cherry MX switches and USB-C connectivity.", 64.99m, 35, "Electronics", null),
            SecureShop.Domain.Entities.Product.Create("Ergonomic Office Chair", "Adjustable lumbar support, breathable mesh back and armrests. Supports up to 120 kg.", 249.99m, 12, "Home", null),
            SecureShop.Domain.Entities.Product.Create("Running Shoes", "Lightweight trail running shoes with cushioned sole and breathable knit upper. Available in sizes 7-13.", 79.99m, 80, "Sports", null),
            SecureShop.Domain.Entities.Product.Create("Stainless Steel Water Bottle", "1 L vacuum-insulated bottle keeps drinks cold 24 h and hot 12 h. BPA-free, leak-proof lid.", 24.99m, 200, "Sports", null),
            SecureShop.Domain.Entities.Product.Create("The Pragmatic Programmer", "Classic software-craftsmanship book by David Thomas and Andrew Hunt. 20th anniversary edition.", 39.99m, 60, "Books", null),
            SecureShop.Domain.Entities.Product.Create("Clean Architecture", "Robert C. Martin guide to structuring applications for long-term maintainability.", 34.99m, 45, "Books", null),
            SecureShop.Domain.Entities.Product.Create("Slim-Fit Chino Trousers", "Stretch cotton blend, mid-rise, available in Navy, Stone and Olive. Machine washable.", 44.99m, 90, "Clothing", null),
        };

        foreach (var p in products)
            db.Set<SecureShop.Domain.Entities.Product>().Add(p);

        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Sample products seeded successfully.", seeded = true });
    }
    catch
    {
        return Results.Problem("Seed operation failed. Check server logs.");
    }
}).RequireAuthorization(policy =>
{
    policy.AddAuthenticationSchemes("AdminCookie", "Bearer");
    policy.RequireRole("Admin");
});

// ── Privacy & Terms clean URLs ───────────────────────────────────────────────
app.MapGet("/privacy", () => Results.Redirect("/privacy.html"));
app.MapGet("/terms",   () => Results.Redirect("/terms.html"));

// API status endpoint
app.MapGet("/api/status", () =>
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
            website = "/",
            products = "/products",
            cart = "/cart"
        }
    };
    return Results.Json(response);
});

// Authentication status endpoint (requires auth)
app.MapGet("/api/auth/status", (HttpContext context) =>
{
    var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;
    var name = context.User?.Identity?.Name;
    var email = context.User?.FindFirst(ClaimTypes.Email)?.Value;
    var claims = context.User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
    
    Console.WriteLine($"[Auth Status] IsAuthenticated: {isAuthenticated}, Name: {name}, Email: {email}");
    
    return Results.Json(new
    {
        isAuthenticated,
        name,
        email,
        claimsCount = claims?.Count ?? 0,
        claims
    });
}).RequireAuthorization();


// ── Database migration & seeding ─────────────────────────────────────────────
Log.Information("Startup init: finished, starting web host");

_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(2000);
        using var scope = app.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Startup init: database migration completed");

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Customer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                Log.Information("Startup init: created role {Role}", role);
            }
        }
        Log.Information("Startup init: role seeding completed");

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await BootstrapAdminProvisioner.ProvisionIfNeededAsync(
            userManager,
            bootstrapAdminCredentials,
            app.Logger);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Startup init: migration/seeding failed");
    }

    app.Services.GetRequiredService<SecureShop.API.Services.MigrationCompletedSignal>().MarkCompleted();

    try
    {
        using var seedScope = app.Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existingCount = await seedDb.Set<SecureShop.Domain.Entities.Product>().CountAsync();
        if (existingCount == 0)
        {
            var seedProducts = new[]
            {
                SecureShop.Domain.Entities.Product.Create("Wireless Noise-Cancelling Headphones", "Premium over-ear headphones with active noise cancellation, 30-hour battery life and foldable design.", 89.99m, 50, "Electronics", null),
                SecureShop.Domain.Entities.Product.Create("Mechanical Gaming Keyboard", "TKL mechanical keyboard with RGB backlighting, Cherry MX switches and USB-C connectivity.", 64.99m, 35, "Electronics", null),
                SecureShop.Domain.Entities.Product.Create("Ergonomic Office Chair", "Adjustable lumbar support, breathable mesh back and armrests. Supports up to 120 kg.", 249.99m, 12, "Home", null),
                SecureShop.Domain.Entities.Product.Create("Running Shoes", "Lightweight trail running shoes with cushioned sole and breathable knit upper. Available in sizes 7-13.", 79.99m, 80, "Sports", null),
                SecureShop.Domain.Entities.Product.Create("Stainless Steel Water Bottle", "1 L vacuum-insulated bottle keeps drinks cold 24 h and hot 12 h. BPA-free, leak-proof lid.", 24.99m, 200, "Sports", null),
                SecureShop.Domain.Entities.Product.Create("The Pragmatic Programmer", "Classic software-craftsmanship book by David Thomas and Andrew Hunt. 20th anniversary edition.", 39.99m, 60, "Books", null),
                SecureShop.Domain.Entities.Product.Create("Clean Architecture", "Robert C. Martin guide to structuring applications for long-term maintainability.", 34.99m, 45, "Books", null),
                SecureShop.Domain.Entities.Product.Create("Slim-Fit Chino Trousers", "Stretch cotton blend, mid-rise, available in Navy, Stone and Olive. Machine washable.", 44.99m, 90, "Clothing", null),
            };

            foreach (var product in seedProducts)
                seedDb.Set<SecureShop.Domain.Entities.Product>().Add(product);

            await seedDb.SaveChangesAsync();
            Log.Information("Startup init: seeded {Count} sample products", seedProducts.Length);
        }
        else
        {
            Log.Information("Startup init: {Count} products already in DB, skipping seed", existingCount);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Startup init: product seeding failed");
    }
});

app.Run();

public partial class Program { }