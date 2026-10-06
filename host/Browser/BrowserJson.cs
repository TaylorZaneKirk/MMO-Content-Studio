// Shared browser JSON boundary: bounded aggregates, exact Int64 transport and safe diagnostics.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MMO.ContentStudio.AuthoringHost.Contracts;
namespace MMO.ContentStudio.AuthoringHost.Browser;

internal static class BrowserJson
{
    internal static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { NumberHandling = JsonNumberHandling.AllowReadingFromString, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new LongStringConverter());
        return options;
    }

    internal static async Task<T?> Read<T>(HttpRequest request, CancellationToken cancellation)
    {
        // Whole aggregates are bounded independently of the larger PNG upload limit.
        if (request.ContentLength is > 1024 * 1024) throw new BadHttpRequestException("Content payload too large.", 413);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(bytes, cancellation)) > 0)
        {
            if (buffer.Length + count > 1024 * 1024) throw new BadHttpRequestException("Content payload too large.", 413);
            buffer.Write(bytes, 0, count);
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(), JsonOptions);
    }

    public static IResult Error(string code, string message, int status = 400) =>
        Json(new { success = false, errors = new[] { new ApiError(code, message, ValidationSeverity.Error) } }, status);

    public static IResult Json<T>(T value, int status = 200, string catalogName = "equipment visual")
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        RemoveHostPaths(node, catalogName);
        return Results.Json(node, JsonOptions, statusCode: status);
    }
    private static void RemoveHostPaths(JsonNode? node, string catalogName)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in new[] { "file_path", "asset_preview_file_path", "source_path" }) obj.Remove(name);
            if (obj["code"]?.GetValue<string>() is "map_catalog_publish_warning" or "map_catalog_publish_skipped")
            {
                obj["message"] = $"The database change completed, but the {catalogName} catalog was not refreshed.";
                obj["remediation"] = "Inspect host logs and run the existing export command on the host. Do not repeat the content mutation to retry an export.";
            }
            // Rig discovery diagnostics may include filesystem paths. The browser only needs availability.
            if (obj.ContainsKey("rigs") && obj.ContainsKey("available")) obj["message"] = obj["available"]?.GetValue<bool>() == true ? null : "Actor rig catalog is unavailable. Inspect host configuration.";
            foreach (var child in obj.ToArray()) RemoveHostPaths(child.Value, catalogName);
        }
        else if (node is JsonArray array) foreach (var child in array) RemoveHostPaths(child, catalogName);
    }
    private sealed class LongStringConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? long.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetInt64();
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
