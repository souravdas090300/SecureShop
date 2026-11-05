using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SecureShop.API.Middleware;

public sealed class AdminCookieAntiforgeryFilter(
    IAntiforgery antiforgery,
    ILogger<AdminCookieAntiforgeryFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method)
            || !context.HttpContext.User.Identities.Any(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == "AdminCookie"))
        {
            await next();
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("Rejected an admin API mutation with an invalid antiforgery token");
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid antiforgery token",
                Detail = "The request could not be validated."
            });
            return;
        }

        await next();
    }
}
