using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record HandshakeResponse(
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("host_version")] string HostVersion,
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("supported_api_versions")] IReadOnlyList<string> SupportedApiVersions,
    [property: JsonPropertyName("server_time_utc")] DateTimeOffset ServerTimeUtc);

public sealed record AuthoringHealthResponse(
    [property: JsonPropertyName("overall_status")] HealthState OverallStatus,
    [property: JsonPropertyName("database")] DatabaseHealth Database,
    [property: JsonPropertyName("asset_roots")] IReadOnlyList<AssetRootHealth> AssetRoots);

public sealed record DatabaseHealth(
    [property: JsonPropertyName("status")] HealthState Status,
    [property: JsonPropertyName("profile")] string Profile,
    [property: JsonPropertyName("database_name")] string? DatabaseName,
    [property: JsonPropertyName("schema_contract")] string? SchemaContract,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("checks")] IReadOnlyList<HealthCheck> Checks);

public sealed record AssetRootHealth(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("status")] HealthState Status,
    [property: JsonPropertyName("message")] string Message);

public sealed record HealthCheck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] HealthState Status,
    [property: JsonPropertyName("message")] string Message);

[JsonConverter(typeof(JsonStringEnumConverter<HealthState>))]
public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unconfigured
}
