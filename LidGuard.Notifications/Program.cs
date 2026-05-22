using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;
using LidGuard.Notifications.Security;
using LidGuard.Notifications.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<LidGuardNotificationsOptions>()
    .Bind(builder.Configuration.GetSection(LidGuardNotificationsOptions.SectionName))
    .Validate(options => options.TryValidate(out _), "LidGuard notification settings are invalid.")
    .ValidateOnStart();
builder.Services.PostConfigure<LidGuardNotificationsOptions>(options => options.Normalize());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(ConfigureAuthenticationCookie);
builder.Services.AddAuthorization();
builder.Services.AddLocalization();
builder.Services.AddRazorPages(ConfigureRazorPages);

builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<NotificationDatabaseInitializer>();
builder.Services.AddSingleton<AuthenticationRefreshTokenStore>();
builder.Services.AddSingleton<PushSubscriptionStore>();
builder.Services.AddSingleton<WebhookEventStore>();
builder.Services.AddSingleton<NotificationDeliveryStore>();
builder.Services.AddSingleton<WebhookEventProcessingSignal>();
builder.Services.AddSingleton<DashboardAuthenticationService>();
builder.Services.AddSingleton<WebPushClient>();
builder.Services.AddSingleton<IWebPushNotificationSender, ClosureOpenSourceWebPushNotificationSender>();
builder.Services.AddHostedService<NotificationDispatchService>();

var app = builder.Build();
var notificationOptions = app.Services.GetRequiredService<IOptions<LidGuardNotificationsOptions>>().Value;
LidGuardNotificationCulture.ApplyDefaultCultureFromEnvironmentOrOptions(notificationOptions);

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<NotificationDatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/login");

app.UseRequestLocalization(LidGuardNotificationCulture.CreateRequestLocalizationOptions(notificationOptions));
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<DashboardAuthenticationRefreshMiddleware>();
app.UseAuthorization();

app.MapRazorPages();
LidGuardNotificationApiEndpoints.Map(app);

app.MapPost("/logout", (Delegate)SignOutAsync).RequireAuthorization();

app.MapPost("/language", (Delegate)SetLanguageAsync);

await app.RunAsync();

static void ConfigureAuthenticationCookie(CookieAuthenticationOptions options)
{
    options.Cookie.HttpOnly = true;
    options.Cookie.Name = DashboardAuthenticationConstants.AccessCookieName;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = DashboardAuthenticationConstants.AccessTokenLifetime;
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.SlidingExpiration = false;
}

static void ConfigureRazorPages(RazorPagesOptions options)
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
}

static async Task<IResult> SignOutAsync(HttpContext httpContext, DashboardAuthenticationService authenticationService)
{
    await authenticationService.SignOutAsync(httpContext, httpContext.RequestAborted);
    return Results.Redirect("/login");
}

static async Task<IResult> SetLanguageAsync(HttpContext httpContext)
{
    var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
    var culture = form["culture"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    if (LidGuardNotificationCulture.TryCreateSelectableCultureInfo(culture, out var cultureInfo))
    {
        var requestCulture = new RequestCulture(cultureInfo);
        var cookieOptions = new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps
        };
        httpContext.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(requestCulture), cookieOptions);
    }

    return Results.Redirect(LocalRedirectPath.Normalize(returnUrl));
}
