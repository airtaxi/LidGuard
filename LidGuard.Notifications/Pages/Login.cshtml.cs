using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Localization;
using LidGuard.Notifications.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Pages;

internal sealed class LoginModel(IOptions<LidGuardNotificationsOptions> options, DashboardAuthenticationService authenticationService) : PageModel
{
    [BindProperty]
    public string AccessToken { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberLogin { get; set; } = true;

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(LocalRedirectPath.Normalize(returnUrl));

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(LocalRedirectPath.Normalize(returnUrl));

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

        await authenticationService.SignInAsync(HttpContext, RememberLogin, HttpContext.RequestAborted);
        return LocalRedirect(LocalRedirectPath.Normalize(returnUrl));
    }
}
