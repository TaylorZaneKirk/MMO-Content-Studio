using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
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

builder.Services.AddSingleton<AuthoringDatabaseConnectionFactory>();
builder.Services.AddSingleton<AuthoringHealthService>();
builder.Services.AddSingleton<ItemAssetService>();
builder.Services.AddSingleton<ItemAssetAuthoringService>();
builder.Services.AddSingleton<BasicItemRepository>();
builder.Services.AddSingleton<BasicItemValidator>();
builder.Services.AddSingleton<BasicItemAuthoringService>();
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
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
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

app.MapGet($"{AuthoringApi.RoutePrefix}/catalog", async (
    HttpContext context,
    ContentCatalogService catalogService,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    var response = await catalogService.LoadAsync(cancellationToken);
    return Results.Ok(ApiEnvelope<ContentCatalogResponse>.Ok(requestId, response));
});

app.MapGet($"{AuthoringApi.RoutePrefix}/assets/items", (
    HttpContext context,
    ItemAssetService assetService) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return Results.Ok(ApiEnvelope<ItemAssetCatalogResponse>.Ok(
        requestId,
        assetService.LoadCatalog()));
});

app.MapPost($"{AuthoringApi.RoutePrefix}/assets/items/import", async (
    HttpContext context,
    ImportItemAssetRequest request,
    ItemAssetAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(requestId, await service.ImportAsync(request, cancellationToken));
});

app.MapGet($"{AuthoringApi.RoutePrefix}/items", async (
    HttpContext context,
    string? search,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(requestId, await service.ListAsync(search, cancellationToken));
});

app.MapGet($"{AuthoringApi.RoutePrefix}/items/{{itemId}}", async (
    HttpContext context,
    string itemId,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(requestId, await service.LoadAsync(itemId, cancellationToken));
});

app.MapPost($"{AuthoringApi.RoutePrefix}/items/{{itemId}}/preview", async (
    HttpContext context,
    string itemId,
    BasicItemPreviewRequest request,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(requestId, await service.PreviewAsync(itemId, request, cancellationToken));
});

app.MapPut($"{AuthoringApi.RoutePrefix}/items/{{itemId}}/draft", async (
    HttpContext context,
    string itemId,
    SaveBasicItemDraftRequest request,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(requestId, await service.SaveDraftAsync(itemId, request, cancellationToken));
});

app.MapPost($"{AuthoringApi.RoutePrefix}/items/{{itemId}}/publish", async (
    HttpContext context,
    string itemId,
    PublicationMutationRequest request,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(
        requestId,
        await service.PublishAsync(itemId, request.ExpectedUpdatedAtUtc, cancellationToken));
});

app.MapPost($"{AuthoringApi.RoutePrefix}/items/{{itemId}}/disable", async (
    HttpContext context,
    string itemId,
    PublicationMutationRequest request,
    BasicItemAuthoringService service,
    CancellationToken cancellationToken) =>
{
    var requestId = RequestIdProvider.Resolve(context);
    return ToHttpResult(
        requestId,
        await service.DisableAsync(itemId, request.ExpectedUpdatedAtUtc, cancellationToken));
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

static IResult ToHttpResult<T>(string requestId, AuthoringOperationResult<T> result)
{
    if (result.Succeeded && result.Value is not null)
    {
        return Results.Ok(ApiEnvelope<T>.Ok(requestId, result.Value));
    }

    var envelope = ApiEnvelope<T>.Failure(requestId, result.Errors.ToArray());
    var codes = result.Errors.Select(error => error.Code).ToHashSet(StringComparer.Ordinal);
    if (codes.Contains("item_not_found"))
    {
        return Results.NotFound(envelope);
    }

    if (codes.Contains("item_version_conflict")
        || codes.Contains("wrong_authoring_workspace")
        || codes.Contains("item_has_live_references"))
    {
        return Results.Conflict(envelope);
    }

    if (codes.Contains("database_unavailable"))
    {
        return Results.Json(envelope, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.BadRequest(envelope);
}

public sealed record PublicationMutationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);
