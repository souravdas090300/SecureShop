using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace SecureShop.API.Helpers;

internal static class AuthTokenCookieHelper
{
    internal static CookieOptions Create(
        HttpContext context,
        IWebHostEnvironment environment,
        DateTimeOffset expires) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expires,
            Path = "/"
        };
}
