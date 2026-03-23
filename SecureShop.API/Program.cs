using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SecureShop.API.Middleware;
using SecureShop.Application;
using SecureShop.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100; opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; opt.QueueLimit = 5;
    });
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10; opt.Window = TimeSpan.FromMinutes(1); opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        var origins = builder.Configuration["AllowedOrigins"]?.Split(',') ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SecureShop API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer",
        BearerFormat = "JWT", Description = "Enter your JWT token"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "SecureShop API v1"); c.RoutePrefix = string.Empty; });
app.UseHttpsRedirection();
app.UseHsts();
app.UseCors("Production");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SecureShop.Infrastructure.AppDbContext>();
    db.Database.Migrate();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
    foreach (var role in new[] { "Admin", "Customer" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole(role));
}

app.Run();