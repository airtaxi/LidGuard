using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<LidGuardNotificationsOptions>()
    .Bind(builder.Configuration.GetSection(LidGuardNotificationsOptions.SectionName))
    .Validate(options => options.TryValidate(out _), "LidGuard notification settings are invalid.")
    .ValidateOnStart();
builder.Services.PostConfigure<LidGuardNotificationsOptions>(options => options.Normalize());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.Name = "LidGuard.Notifications";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});

builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<NotificationDatabaseInitializer>();
builder.Services.AddSingleton<PushSubscriptionStore>();
builder.Services.AddSingleton<WebhookEventStore>();
builder.Services.AddSingleton<NotificationDeliveryStore>();
builder.Services.AddSingleton<WebhookEventProcessingSignal>();
builder.Services.AddSingleton<WebPushClient>();
builder.Services.AddSingleton<IWebPushNotificationSender, ClosureOpenSourceWebPushNotificationSender>();
builder.Services.AddHostedService<NotificationDispatchService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<NotificationDatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/login");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
LidGuardNotificationApiEndpoints.Map(app);

app.MapPost("/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

await app.RunAsync();
