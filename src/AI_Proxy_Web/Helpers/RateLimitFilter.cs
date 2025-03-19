using Microsoft.AspNetCore.Mvc.Filters;

namespace AI_Proxy_Web.Helpers;

public class RateLimitFilter : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rateLimiter = context.HttpContext.RequestServices.GetRequiredService<CustomRateLimiter>();
        var token = CurrentToken(context.HttpContext);
        token = string.IsNullOrEmpty(token) ? "anonymouse" : token;
        if (!await rateLimiter.IsRequestAllowedAsync(token))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        await base.OnActionExecutionAsync(context, next);
    }
    
    protected string CurrentToken(HttpContext httpContext)
    {
        var headersToken = httpContext.Request.Headers["x-access-token"].ToString();
        var cookiesToken = httpContext.Request.Cookies["admin-token"];
        var paramsToken = httpContext.Request.Query["token"].ToString();
        string token = !string.IsNullOrWhiteSpace(headersToken) ? headersToken : cookiesToken;
        token = string.IsNullOrWhiteSpace(token) ? paramsToken : token;
        return token;
    }
}