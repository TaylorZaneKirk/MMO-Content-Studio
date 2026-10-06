// Owns the browser access boundary. Legacy desktop APIs remain loopback-only.
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

namespace MMO.ContentStudio.AuthoringHost.Browser;

public sealed class BrowserAccess
{
    public string? LanUrl { get; init; }
    public string? AllowedSubnet { get; init; }
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
    public string? PasswordHash { get; init; }
    public bool ReadOnly { get; init; }
    public bool TrustedHomeLanWithoutPassword { get; init; }
    public bool Configured => !string.IsNullOrWhiteSpace(PasswordHash);
    private Uri? _lan;
    private System.Net.IPNetwork? _subnet;

    public static BrowserAccess Configure(WebApplicationBuilder builder, Uri loopback)
    {
        var access = builder.Configuration.GetSection("Browser").Get<BrowserAccess>() ?? new();
        if (access.TrustedHomeLanWithoutPassword && (string.IsNullOrWhiteSpace(access.LanUrl) || access.Configured))
            throw new InvalidOperationException("TrustedHomeLanWithoutPassword requires an explicit LAN URL and no password hash. Remove this opt-in before configuring password access.");
        if (!string.IsNullOrWhiteSpace(access.LanUrl))
        {
            if (!Uri.TryCreate(access.LanUrl, UriKind.Absolute, out var lan)
                || lan.Scheme != "https" || lan.AbsolutePath != "/" || lan.Query != "" || lan.UserInfo != ""
                || !IPAddress.TryParse(lan.Host, out var address) || !IsPrivateV4(address)
                || lan.Port == loopback.Port || (!access.Configured && !access.TrustedHomeLanWithoutPassword) || string.IsNullOrWhiteSpace(access.CertificatePath)
                || !System.Net.IPNetwork.TryParse(access.AllowedSubnet, out var subnet)
                || !IsPrivateV4(subnet.BaseAddress) || subnet.PrefixLength < 16 || !subnet.Contains(address))
                throw new InvalidOperationException("Browser LAN access requires an explicit private IPv4 HTTPS URL on a separate port, a private subnet (/16 or narrower), certificate, and either a password hash or explicit TrustedHomeLanWithoutPassword opt-in.");
            access._lan = lan;
            access._subnet = subnet;
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.Listen(IPAddress.Parse(loopback.Host == "localhost" ? "127.0.0.1" : loopback.Host), loopback.Port);
                server.Listen(address, lan.Port, endpoint => endpoint.UseHttps(access.CertificatePath, access.CertificatePassword));
            });
        }
        // Sessions intentionally expire across host restarts; do not create a key store or credentials.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider()
            .AddKeyManagementOptions(options => options.XmlRepository = new MemoryKeys());
        builder.Services.AddSingleton(access);
        builder.Services.AddAuthentication("Studio").AddCookie("Studio", options =>
        {
            options.Cookie.Name = "Studio.Session";
            options.Cookie.Path = "/studio";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(4);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-Studio-CSRF";
            options.Cookie.Name = "Studio.CSRF";
            options.Cookie.Path = "/studio";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("studio-login", _ => RateLimitPartition.GetFixedWindowLimiter("login", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        return access;
    }

    public void Use(WebApplication app, Uri loopback)
    {
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var local = context.Connection.LocalIpAddress;
            var desktopListener = local is not null && IPAddress.IsLoopback(local) && context.Connection.LocalPort == loopback.Port;
            var allowedHost = desktopListener
                ? request.Host.Port == loopback.Port && (request.Host.Host == "localhost" || IPAddress.TryParse(request.Host.Host, out var hostIp) && IPAddress.IsLoopback(hostIp))
                : IsAllowedLanRequest(context);
            if (!allowedHost) { context.Response.StatusCode = 403; return; }
            var browser = request.Path.StartsWithSegments("/studio");
            if (!browser)
            {
                // No legacy routes through the LAN listener, or through browser-origin requests.
                if (!desktopListener || request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Sec-Fetch-Site"))
                { context.Response.StatusCode = 403; return; }
                await next(); return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            // Network authorization applies only to this HTTPS listener, never to loopback or legacy routes.
            var trustedHomeLan = TrustedHomeLanWithoutPassword && IsAllowedLanRequest(context);
            var api = request.Path.StartsWithSegments("/studio/api");
            var session = request.Path == "/studio/api/session" || request.Path == "/studio/api/login";
            if (api && !session && !trustedHomeLan && Configured && context.User.Identity?.IsAuthenticated != true)
            { context.Response.StatusCode = 401; return; }
            if (api && !HttpMethods.IsGet(request.Method))
            {
                if ((!Configured && !trustedHomeLan) || (ReadOnly && request.Path != "/studio/api/login" && request.Path != "/studio/api/logout"))
                { await BrowserJson.Error("browser_read_only", "Browser writes are not enabled on this host.", 403).ExecuteAsync(context); return; }
                if (request.Headers.Origin != $"{request.Scheme}://{request.Host}")
                { context.Response.StatusCode = 403; return; }
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { context.Response.StatusCode = 403; return; }
            }
            try { await next(); }
            catch (Exception exception) when (api && !context.Response.HasStarted)
            {
                app.Logger.LogWarning(exception, "Browser request failed for {Path}", request.Path);
                context.Response.Clear();
                context.Response.Headers.CacheControl = "no-store";
                if (exception is System.Text.Json.JsonException or FormatException or OverflowException or BadHttpRequestException)
                { await BrowserJson.Error("invalid_request", "The request contains invalid or unsupported fields, or exceeds its size limit.", exception is BadHttpRequestException bad ? bad.StatusCode : 400).ExecuteAsync(context); return; }
                await BrowserJson.Error("request_failed", "The request did not complete. For a write, reload and compare before trying again.", 500).ExecuteAsync(context);
            }
        });
    }

    public void Map(WebApplication app)
    {
        app.MapGet("/studio/api/session", (HttpContext context, IAntiforgery csrf) =>
        {
            var authenticated = context.User.Identity?.IsAuthenticated == true;
            var trustedHomeLan = TrustedHomeLanWithoutPassword && IsAllowedLanRequest(context);
            return BrowserJson.Json(new
            {
                authenticated, configured = Configured, trusted_home_lan = trustedHomeLan,
                read_only = ReadOnly || (!Configured && !trustedHomeLan),
                can_edit = !ReadOnly && (authenticated || trustedHomeLan),
                csrf_token = csrf.GetAndStoreTokens(context).RequestToken
            });
        });
        app.MapPost("/studio/api/login", async (HttpContext context) =>
        {
            var login = await context.Request.ReadFromJsonAsync<Login>();
            if (login is null || login.Password.Length > 1024 || !Configured
                || new PasswordHasher<string>().VerifyHashedPassword("studio", PasswordHash!, login.Password) == PasswordVerificationResult.Failed)
                return BrowserJson.Error("login_failed", "The password was not accepted.", 401);
            await context.SignInAsync("Studio", new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Studio owner")], "Studio")));
            return Results.NoContent();
        }).RequireRateLimiting("studio-login");
        app.MapPost("/studio/api/logout", async (HttpContext context) =>
        { await context.SignOutAsync("Studio"); return Results.NoContent(); });
        // Serve a fixed set of bundled files, never a configurable filesystem root.
        foreach (var name in new[] { "index.html", "shops.html", "loot-tables.html", "items.js", "shops.js", "loot-tables.js", "world-objects.html", "world-objects.js", "blacksmithing.html", "blacksmithing.js", "quests.html", "quests.js", "dialogue.html", "dialogue.js", "npcs.html", "npcs.js", "actor-appearance.js", "studio-common.js", "studio.css" })
        {
            var file = name;
            app.MapGet($"/studio/{file}", () => Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "studio", file),
                file.EndsWith(".js") ? "text/javascript" : file.EndsWith(".css") ? "text/css" : "text/html"));
        }
        // Routing also matches a trailing slash; a second mapping would be ambiguous.
        app.MapGet("/studio", () => Results.Redirect("/studio/index.html"));
    }

    private bool IsAllowedLanRequest(HttpContext context)
    {
        var local = context.Connection.LocalIpAddress;
        var remote = context.Connection.RemoteIpAddress;
        return _lan is not null && _subnet is not null && context.Request.IsHttps
            && local is not null && local.Equals(IPAddress.Parse(_lan.Host))
            && context.Connection.LocalPort == _lan.Port
            && remote is not null && _subnet.Value.Contains(remote)
            && string.Equals(context.Request.Host.Value, _lan.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateV4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168);
    }
    // ASP.NET also warms its key manager at startup. Keep that storage in memory too.
    private sealed class MemoryKeys : Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository
    {
        private readonly List<System.Xml.Linq.XElement> _elements = [];
        public IReadOnlyCollection<System.Xml.Linq.XElement> GetAllElements()
        { lock (_elements) return _elements.Select(element => new System.Xml.Linq.XElement(element)).ToArray(); }
        public void StoreElement(System.Xml.Linq.XElement element, string friendlyName)
        { lock (_elements) _elements.Add(new System.Xml.Linq.XElement(element)); }
    }
    private sealed record Login(string Password);
}
