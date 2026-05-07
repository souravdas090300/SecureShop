using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
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
using SecureShop.Domain.Entities;
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

// ── HttpClient for Razor Pages to call API ───────────────────────────────────
builder.Services.AddHttpClient();

// ── Data Protection with persistent keys ──────────────────────────────────────
// CRITICAL: Without persistent keys, cookie encryption keys change on every restart
// causing all existing cookies to become unreadable
var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(keysDirectory);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("SecureShop");

Console.WriteLine($"[STARTUP] Data Protection keys directory: {keysDirectory}");

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── Authentication: Cookies for Razor Pages, JWT for API ─────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured. Set it in Railway environment variables.");

builder.Services.AddAuthentication(options =>
    {
        // CRITICAL: All three defaults must be set for cookie authentication to work
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
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
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnSigningIn = context =>
            {
                Console.WriteLine($"[UserAuth] OnSigningIn - Principal: {context.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            },
            OnSignedIn = context =>
            {
                Console.WriteLine($"[UserAuth] OnSignedIn - User signed in successfully");
                return Task.CompletedTask;
            },
            OnValidatePrincipal = context =>
            {
                Console.WriteLine($"[UserAuth] OnValidatePrincipal - IsAuthenticated: {context.Principal?.Identity?.IsAuthenticated}, Name: {context.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie("AdminCookie", options =>
    {
        options.Cookie.Name = ".SecureShop.Admin";
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnSigningIn = context =>
            {
                Console.WriteLine($"[AdminAuth] OnSigningIn - Principal: {context.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            },
            OnSignedIn = context =>
            {
                Console.WriteLine($"[AdminAuth] OnSignedIn - Admin signed in successfully");
                return Task.CompletedTask;
            },
            OnValidatePrincipal = context =>
            {
                Console.WriteLine($"[AdminAuth] OnValidatePrincipal - IsAuthenticated: {context.Principal?.Identity?.IsAuthenticated}, Name: {context.Principal?.Identity?.Name}");
                
                // Validate that the user still has Admin role
                if (context.Principal?.Identity?.IsAuthenticated == true)
                {
                    var isAdmin = context.Principal.IsInRole("Admin");
                    if (!isAdmin)
                    {
                        Console.WriteLine($"[AdminAuth] User no longer has Admin role - rejecting principal");
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
    // API endpoints use JWT Bearer authentication
    options.AddPolicy("ApiPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    
    // Admin API endpoints
    options.AddPolicy("AdminApiPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireRole("Admin");
    });
    
    // Admin Razor Pages
    options.AddPolicy("AdminPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add("AdminCookie");
        policy.RequireRole("Admin");
    });
});

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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
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

// ── Razor Pages for Frontend ─────────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    // Admin pages require AdminCookie authentication scheme
    options.Conventions.AuthorizeFolder("/Admin", "AdminPolicy");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
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

// 3. Cache-busting headers for dynamic pages
app.UseMiddleware<CacheBustingMiddleware>();

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

// 4.5. Clear old corrupted cookies (from before Data Protection keys were persisted)
app.Use(async (context, next) =>
{
    // Delete ALL old cookies from previous failed authentication attempts
    var cookiesToDelete = new[] { ".SecureShop.Auth", ".SecureShop.Auth.v2", ".SecureShop.Auth.v3", "UserEmail", "UserName" };
    foreach (var cookieName in cookiesToDelete)
    {
        if (context.Request.Cookies.ContainsKey(cookieName))
        {
            context.Response.Cookies.Delete(cookieName, new CookieOptions { Path = "/" });
            Console.WriteLine($"[Middleware] Deleted old cookie: {cookieName}");
        }
    }
    await next();
});

// 5. Railway terminates TLS at the load balancer; the container only sees plain
//    HTTP. UseHttpsRedirection and UseHsts must NOT run inside the container —
//    they cause redirect loops behind a TLS-terminating proxy.

// 6. Framework middleware
app.UseCors("Production");
app.UseRateLimiter();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Add middleware to debug authentication on every request
app.Use(async (context, next) =>
{
    Console.WriteLine($"[Middleware] Request: {context.Request.Method} {context.Request.Path}");
    Console.WriteLine($"[Middleware] Cookies count: {context.Request.Cookies.Count}");
    foreach (var cookie in context.Request.Cookies)
    {
        Console.WriteLine($"[Middleware] Cookie: {cookie.Key} = {cookie.Value.Substring(0, Math.Min(20, cookie.Value.Length))}...");
    }
    Console.WriteLine($"[Middleware] BEFORE next() - IsAuthenticated: {context.User?.Identity?.IsAuthenticated}");
    
    await next();
    
    Console.WriteLine($"[Middleware] AFTER next() - IsAuthenticated: {context.User?.Identity?.IsAuthenticated}");
});

// ── Endpoints ─────────────────────────────────────────────────────────────────
// Map API controllers under /api prefix
app.MapControllers();

// Map Razor Pages for frontend website
app.MapRazorPages();

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

// Convenience redirect: /api/swagger → /swagger
app.MapGet("/api/swagger", () => Results.Redirect("/swagger"));

// ── One-shot product seed endpoint (Admin JWT required) ──────────────────────
// Call GET /api/admin/seed with a valid Admin Bearer token to populate samples.
// Only inserts when the Products table is empty; safe to call multiple times.
app.MapGet("/api/admin/seed", async (AppDbContext db) =>
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
        // Exception details are intentionally not returned to the caller.
        return Results.Problem("Seed operation failed. Check server logs.");
    }
}).RequireAuthorization("AdminApiPolicy");

// ── Privacy & Terms clean URLs (required for Google OAuth branding) ───────────
// Actual HTML files live in wwwroot/privacy.html and wwwroot/terms.html.
app.MapGet("/privacy", () => Results.Redirect("/privacy.html"));
app.MapGet("/terms",   () => Results.Redirect("/terms.html"));

// API status endpoint (for monitoring/health checks)
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

// Debug endpoint to check authentication status
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

        // Seed default admin user if none exists
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        
        var adminEmail = "admin@secureshop.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };
            
            var result = await userManager.CreateAsync(adminUser, "Admin123!@#");
            
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                Log.Information("Startup init: created default admin user (admin@secureshop.com / Admin123!@#)");
            }
            else
            {
                Log.Warning("Startup init: failed to create admin user: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

    }
    catch (Exception ex)
    {
        Log.Error(ex, "Startup init: migration/seeding failed — ensure ConnectionStrings__DefaultConnection is set in Railway env vars");
    }

    // Product seeding runs in its own isolated try/catch so migration or role
    // failures above cannot prevent the catalogue from being populated.
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