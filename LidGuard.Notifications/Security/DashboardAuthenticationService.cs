using System.Security.Claims;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LidGuard.Notifications.Security;

internal sealed class DashboardAuthenticationService(AuthenticationRefreshTokenStore refreshTokenStore)
{
    public async Task SignInAsync(HttpContext httpContext, bool rememberLogin, CancellationToken cancellationToken)
    {
        var refreshTokenIssue = rememberLogin ? await refreshTokenStore.CreateAsync(DashboardAuthenticationConstants.RefreshTokenLifetime, cancellationToken) : null;

        await SignInWithIssueAsync(httpContext, rememberLogin, refreshTokenIssue);
    }

    public async Task<bool> TryRefreshAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[DashboardAuthenticationConstants.RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            if (httpContext.User.Identity?.IsAuthenticated != true) return false;

            await SignInWithIssueAsync(httpContext, rememberLogin: false, refreshTokenIssue: null);
            return true;
        }

        var refreshTokenIssue = await refreshTokenStore.RotateAsync(refreshToken, DashboardAuthenticationConstants.RefreshTokenLifetime, cancellationToken);
        if (refreshTokenIssue is null)
        {
            DeleteRefreshCookie(httpContext);
            if (httpContext.User.Identity?.IsAuthenticated == true) return true;

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return false;
        }

        await SignInWithIssueAsync(httpContext, rememberLogin: true, refreshTokenIssue);
        return true;
    }

    public async Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[DashboardAuthenticationConstants.RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken)) await refreshTokenStore.RevokeAsync(refreshToken, cancellationToken);

        DeleteRefreshCookie(httpContext);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static async Task SignInWithIssueAsync(HttpContext httpContext, bool rememberLogin, AuthenticationRefreshTokenIssue? refreshTokenIssue)
    {
        var now = DateTimeOffset.UtcNow;
        var principal = CreatePrincipal();
        var authenticationProperties = new AuthenticationProperties
        {
            AllowRefresh = false,
            ExpiresUtc = now.Add(DashboardAuthenticationConstants.AccessTokenLifetime),
            IsPersistent = rememberLogin,
            IssuedUtc = now
        };

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authenticationProperties);
        httpContext.User = principal;

        if (rememberLogin && refreshTokenIssue is not null)
        {
            httpContext.Response.Cookies.Append(DashboardAuthenticationConstants.RefreshCookieName, refreshTokenIssue.Token, CreateRefreshCookieOptions(httpContext, refreshTokenIssue.ExpiresAtUtc));
        }
        else
        {
            DeleteRefreshCookie(httpContext);
        }
    }

    private static ClaimsPrincipal CreatePrincipal()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, LidGuardNotificationText.Brand)], CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static CookieOptions CreateRefreshCookieOptions(HttpContext httpContext, DateTimeOffset expiresAtUtc) => new()
    {
        Expires = expiresAtUtc,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = DashboardAuthenticationConstants.RefreshTokenLifetime,
        Path = "/",
        SameSite = SameSiteMode.Strict,
        Secure = httpContext.Request.IsHttps
    };

    private static void DeleteRefreshCookie(HttpContext httpContext)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
            Secure = httpContext.Request.IsHttps
        };
        httpContext.Response.Cookies.Delete(DashboardAuthenticationConstants.RefreshCookieName, cookieOptions);
    }
}
