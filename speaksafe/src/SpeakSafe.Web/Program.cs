using System.Data.Common;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using SpeakSafe.Core;
using SpeakSafe.Web.Api;
using SpeakSafe.Web.Data;
using SpeakSafe.Web.Infra;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var services = builder.Services;
var appData = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

services.AddSingleton(TimeProvider.System);
services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// ---------------------------------------------------------------- encryption at rest
var keysPath = cfg["DataProtection:KeysPath"] ?? Path.Combine(appData, "keys");
var dp = services.AddDataProtection().SetApplicationName("SpeakSafe").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();
services.AddSingleton<ICipher, DataProtectionCipher>();

var evidencePath = cfg["Evidence:Path"];
if (string.IsNullOrWhiteSpace(evidencePath)) evidencePath = Path.Combine(appData, "evidence");
services.AddSingleton<IEvidenceFiles>(new FileEvidenceFiles(evidencePath));

// ---------------------------------------------------------------- store
var storeKind = cfg["Store"] ?? "SqlServer";
var samplePassword = cfg["Seed:SamplePassword"] ?? "demo1234";
if (storeKind.Equals("Memory", StringComparison.OrdinalIgnoreCase))
{
    services.AddSingleton<ISpeakStore>(sp => new MemoryStore(cfg.GetValue("Seed:Enabled", true)
        ? SeedData.Build(sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, samplePassword, sp.GetRequiredService<ICipher>())
        : null));
}
else
{
    // Registered by name so the code compiles against System.Data.Common only.
    DbProviderFactories.RegisterFactory("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
    var cs = cfg.GetConnectionString("SpeakSafe") ?? throw new InvalidOperationException("ConnectionStrings:SpeakSafe is missing.");
    services.AddSingleton(new Db(DbProviderFactories.GetFactory("Microsoft.Data.SqlClient"), cs));
    services.AddSingleton<SqlStore>();
    services.AddSingleton<ISpeakStore>(sp => sp.GetRequiredService<SqlStore>());
    services.AddSingleton<DbInitializer>();
}

services.AddSingleton<AccountService>();
services.AddSingleton<ReportService>();
services.AddSingleton<ChatService>();
services.AddSingleton<OrgService>();

// ---------------------------------------------------------------- auth
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "speaksafe.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(cfg.GetValue("Auth:SessionHours", 8));
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => Deny(ctx.Response, 401, "Sign in first.");
    o.Events.OnRedirectToAccessDenied = ctx => Deny(ctx.Response, 403, "You don't have access to this.");
});
services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Survivor, p => p.RequireRole(Actor.SurvivorRole))
    .AddPolicy(Policies.Reporter, p => p.RequireRole(Actor.SurvivorRole, Actor.TrackerRole))
    .AddPolicy(Policies.Participant, p => p.RequireRole(Actor.SurvivorRole, Actor.TrackerRole, Actor.OfficerRole))
    .AddPolicy(Policies.Officer, p => p.RequireRole(Actor.OfficerRole))
    .AddPolicy(Policies.Admin, p => p.RequireRole(Actor.AdminRole));

// Limits are kept in memory only; client addresses are never written to the database or logs.
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = (ctx, _) => new ValueTask(Deny(ctx.HttpContext.Response, 429, "Too many attempts. Wait a minute and try again."));
    o.AddPolicy("auth", ctx => Window(ctx, 10, TimeSpan.FromMinutes(1)));
    o.AddPolicy("report", ctx => Window(ctx, 12, TimeSpan.FromMinutes(10)));
    o.AddPolicy("chat", ctx => Window(ctx, 30, TimeSpan.FromMinutes(1)));
});

services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ReportService.MaxEvidenceBytes + 64 * 1024);

services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (app.Services.GetService<DbInitializer>() is { } init) await init.RunAsync();

app.UseForwardedHeaders();
if (cfg.GetValue("ForceHttps", false)) { app.UseHsts(); app.UseHttpsRedirection(); }

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
    await next();
});

// Consistent error envelope: { ok: false, error }.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (SpeakException ex) { await Deny(ctx.Response, ex.Status, ex.Message); }
    catch (BadHttpRequestException ex) { await Deny(ctx.Response, ex.StatusCode == 413 ? 413 : 400, ex.StatusCode == 413 ? "That file is too large." : ex.Message); }
    catch (System.Text.Json.JsonException) { await Deny(ctx.Response, 400, "The request body is not valid JSON for this endpoint."); }
    catch (InvalidDataException) { await Deny(ctx.Response, 400, "The upload could not be read. Try again."); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
        await Deny(ctx.Response, 500, "Something went wrong. It has been logged.");
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

Endpoints.Map(app);

app.Logger.LogInformation("SpeakSafe started · store {Store} · admin sign-in {Admin}", storeKind,
    string.IsNullOrEmpty(cfg["Admin:Pin"]) ? "off" : "on");
app.Run();

static RateLimitPartition<string> Window(HttpContext ctx, int permits, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 });

static Task Deny(HttpResponse res, int status, string error)
{
    if (res.HasStarted) return Task.CompletedTask;
    res.StatusCode = status;
    return res.WriteAsJsonAsync(new { ok = false, error });
}

public partial class Program;
