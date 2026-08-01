using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var listenUrl = builder.Configuration["AuthoringHost:ListenUrl"] ?? "http://127.0.0.1:5187";
if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var listenUri)
    || !string.Equals(listenUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
    || !listenUri.IsLoopback)
{
    throw new InvalidOperationException(
        "AuthoringHost:ListenUrl must be an absolute loopback HTTP URL.");
}

builder.WebHost.UseUrls(listenUri.ToString().TrimEnd('/'));

builder.Services.Configure<AuthoringHostOptions>(
    builder.Configuration.GetSection(AuthoringHostOptions.SectionName));
builder.Services.Configure<ConnectionProfilesOptions>(
    builder.Configuration.GetSection(ConnectionProfilesOptions.SectionName));
builder.Services.Configure<AssetRootsOptions>(
    builder.Configuration.GetSection(AssetRootsOptions.SectionName));

builder.Services.AddSingleton<AuthoringHealthService>();
builder.Services.AddSingleton<ContentCatalogService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Append("X-Content-Studio-Api-Version", AuthoringApi.CurrentVersion);
    await next();
});

app.MapGet($"{AuthoringApi.RoutePrefix}/system/handshake", (
    HttpContext context,
    IOptions<AuthoringHostOptions> options) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    var response = new HandshakeResponse(
        options.Value.ServiceName,
        version,
        AuthoringApi.CurrentVersion,
        AuthoringApi.SupportedVersions,
        DateTimeOffset.UtcNow);

    return Results.Ok(ApiEnvelope<HandshakeResponse>.Ok(requestId, response));
});

app.MapGet($"{AuthoringApi.RoutePrefix}/system/health", async (
    HttpContext context,
    AuthoringHealthService healthService,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    var response = await healthService.CheckAsync(cancellationToken);
    return Results.Ok(ApiEnvelope<AuthoringHealthResponse>.Ok(requestId, response));
});

app.MapGet($"{AuthoringApi.RoutePrefix}/catalog", (
    HttpContext context,
    ContentCatalogService catalogService) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return Results.Ok(ApiEnvelope<ContentCatalogResponse>.Ok(
        requestId,
        catalogService.LoadEmptyFoundationCatalog()));
});

app.MapFallback((HttpContext context) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return Results.NotFound(ApiEnvelope<object>.Failure(
        requestId,
        new ApiError(
            "route_not_found",
            "The requested Content Studio API route does not exist.",
            ValidationSeverity.Error)));
});

app.Run();
