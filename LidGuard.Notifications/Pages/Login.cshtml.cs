using System.Security.Claims;
using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Localization;
using LidGuard.Notifications.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Pages;

internal sealed class LoginModel(IOptions<LidGuardNotificationsOptions> options) : PageModel
{
    [BindProperty]
    public string AccessToken { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(LocalRedirectPath.Normalize(returnUrl));

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            ErrorMessage = LidGuardNotificationText.AccessTokenRequired;
            return Page();
        }

        if (!SecretVerifier.EqualsConfiguredSecret(options.Value.AccessToken, AccessToken))
        {
            ErrorMessage = LidGuardNotificationText.InvalidAccessToken;
            return Page();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, LidGuardNotificationText.Brand)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(LocalRedirectPath.Normalize(returnUrl));
    }
}
