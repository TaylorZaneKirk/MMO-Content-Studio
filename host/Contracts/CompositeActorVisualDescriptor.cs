using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

// JSONB remains the persistence representation, while validation and export use
// this stable domain shape rather than ad-hoc JsonElement traversal.
public sealed record CompositeActorVisualDescriptor(
    string RigId,
    IReadOnlyDictionary<string, string> BaseLayers,
    IReadOnlyDictionary<string, string> CosmeticItemIds)
{
    public static bool TryParse(JsonElement? value, out CompositeActorVisualDescriptor? descriptor)
    {
        descriptor = null;
        if (value is not { ValueKind: JsonValueKind.Object } element
            || !TryReadString(element, "rig_id", out var rigId)
            || !TryReadOptionalStringMap(element, "base_layers", out var baseLayers)
            || !TryReadOptionalStringMap(element, "cosmetic_item_ids", out var cosmetics))
        {
            return false;
        }

        descriptor = new CompositeActorVisualDescriptor(rigId, baseLayers, cosmetics);
        return true;
    }

    public static JsonElement? Normalize(JsonElement? value)
    {
        if (!TryParse(value, out var descriptor))
        {
            return value?.Clone();
        }

        return JsonSerializer.SerializeToElement(new CanonicalCompositeActorVisual(
            descriptor!.RigId,
            descriptor.CosmeticItemIds));
    }

    private sealed record CanonicalCompositeActorVisual(
        [property: JsonPropertyName("rig_id")] string RigId,
        [property: JsonPropertyName("cosmetic_item_ids")] IReadOnlyDictionary<string, string> CosmeticItemIds);

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryReadOptionalStringMap(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.Object && TryReadMap(property, out values);
    }

    private static bool TryReadMap(JsonElement element, out IReadOnlyDictionary<string, string> values)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(value))
            {
                values = map;
                return false;
            }

            map[property.Name] = value;
        }

        values = map;
        return true;
    }
}
