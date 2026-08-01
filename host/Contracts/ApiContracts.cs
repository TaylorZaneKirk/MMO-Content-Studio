using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public static class AuthoringApi
{
    public const string CurrentVersion = "1";
    public const string RoutePrefix = "/api/v1";
    public static readonly IReadOnlyList<string> SupportedVersions = [CurrentVersion];
}

public sealed record ApiEnvelope<T>(
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("errors")] IReadOnlyList<ApiError> Errors)
{
    public static ApiEnvelope<T> Ok(string requestId, T data) =>
        new(AuthoringApi.CurrentVersion, requestId, true, data, []);

    public static ApiEnvelope<T> Failure(string requestId, params ApiError[] errors) =>
        new(AuthoringApi.CurrentVersion, requestId, false, default, errors);
}

public sealed record ApiError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] ValidationSeverity Severity,
    [property: JsonPropertyName("field")] string? Field = null,
    [property: JsonPropertyName("remediation")] string? Remediation = null);

[JsonConverter(typeof(JsonStringEnumConverter<ValidationSeverity>))]
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}
