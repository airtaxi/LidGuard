using Microsoft.AspNetCore.Authorization;

namespace LidGuard.Notifications.Security;

internal sealed class DashboardAuthenticationRefreshMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, DashboardAuthenticationService authenticationService)
    {
        if (ShouldRefresh(httpContext) && (httpContext.User.Identity?.IsAuthenticated == true || httpContext.Request.Cookies.ContainsKey(DashboardAuthenticationConstants.RefreshCookieName))) await authenticationService.TryRefreshAsync(httpContext, httpContext.RequestAborted);

        await next(httpContext);
    }

    private static bool ShouldRefresh(HttpContext httpContext)
    {
        var isPublicPath = httpContext.Request.Path.StartsWithSegments("/api/webhooks") || httpContext.Request.Path.StartsWithSegments("/api/push/public-key") || httpContext.Request.Path.StartsWithSegments("/healthz");
        if (isPublicPath) return false;

        if (httpContext.Request.Path.StartsWithSegments("/login")) return true;

        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;

        return true;
    }
}
