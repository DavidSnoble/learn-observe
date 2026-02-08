using LearnObserve;
using LearnObserve.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

DotEnv.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});

builder.Services.AddSingleton<ServiceStatusReader>();
builder.Services.AddSingleton<JournalReader>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = "Cookies";
    })
    .AddCookie("Cookies", options =>
    {
        options.Cookie.Name = "learn_observe_auth";
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    })
    .AddGoogle("Google", options =>
    {
        options.ClientId = builder.Configuration["GOOGLE_CLIENT_ID"] ?? "";
        options.ClientSecret = builder.Configuration["GOOGLE_CLIENT_SECRET"] ?? "";
        options.SaveTokens = false;
        options.Scope.Add("email");
        options.Scope.Add("profile");
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
        {
            var raw = builder.Configuration["ADMIN_EMAILS"] ?? "";
            var allowed = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowed.Count == 0) return false;

            var email = ctx.User.FindFirst("email")?.Value
                        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            return !string.IsNullOrWhiteSpace(email) && allowed.Contains(email);
        });
    });
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", ts = DateTimeOffset.UtcNow }));

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    if (ctx.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var email = ctx.User.FindFirst("email")?.Value
                ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    var name = ctx.User.FindFirst("name")?.Value
               ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
    return Results.Ok(new { email, name });
}).RequireAuthorization();

app.MapGet("/api/auth/login", (HttpContext ctx, string? returnUrl) =>
{
    var props = new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
    };
    return Results.Challenge(props, new[] { "Google" });
});

app.MapGet("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync("Cookies");
    return Results.Redirect("/");
});

app.MapGet("/api/status", async (HttpContext ctx, ServiceStatusReader reader, string unit) =>
{
    var status = await reader.GetStatusAsync(unit, ctx.RequestAborted);
    return status is null ? Results.NotFound(new { error = "unit not found" }) : Results.Ok(status);
}).RequireAuthorization("Admin");

app.MapGet("/api/logs", async (HttpContext ctx, JournalReader reader, string unit, string? priority, int? lines) =>
{
    var p = string.IsNullOrWhiteSpace(priority) ? "warning" : priority;
    var n = lines ?? 200;
    var data = await reader.TailAsync(unit, n, p, ctx.RequestAborted);
    return Results.Ok(new { lines = data });
}).RequireAuthorization("Admin");

app.MapFallbackToFile("index.html");

app.Run();
