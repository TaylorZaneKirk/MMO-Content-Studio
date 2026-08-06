using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ActorAppearanceCatalogService
{
    private const int SupportedSchemaVersion = 1;
    private const string RigCatalogRelativePath = "actors/appearance/data/rigs/catalog_v1.json";
    private readonly AssetRootsOptions _options;

    public ActorAppearanceCatalogService(IOptions<AssetRootsOptions> options)
    {
        _options = options.Value;
    }

    public ActorRigCatalogDefinition LoadRigCatalog()
    {
        var rigCatalogPath = ResolveRigCatalogPath();
        if (rigCatalogPath is null)
        {
            return new ActorRigCatalogDefinition(
                false,
                null,
                "The configured game_client_assets root could not be resolved to the MMO Project client rig catalog.",
                []);
        }

        if (!File.Exists(rigCatalogPath))
        {
            return new ActorRigCatalogDefinition(
                false,
                rigCatalogPath,
                "The canonical MMO Project actor rig catalog is unavailable at the configured path.",
                []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(rigCatalogPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("schema_version", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || schemaVersion.GetInt32() != SupportedSchemaVersion)
            {
                return new ActorRigCatalogDefinition(
                    false,
                    rigCatalogPath,
                    $"Actor rig catalog schema_version must be {SupportedSchemaVersion}.",
                    []);
            }

            if (!root.TryGetProperty("rigs", out var rigsElement)
                || rigsElement.ValueKind != JsonValueKind.Array)
            {
                return new ActorRigCatalogDefinition(
                    false,
                    rigCatalogPath,
                    "Actor rig catalog must define a rigs array.",
                    []);
            }

            var rigs = new List<ActorRigDefinition>();
            foreach (var rigElement in rigsElement.EnumerateArray())
            {
                if (rigElement.ValueKind != JsonValueKind.Object)
                {
                    return new ActorRigCatalogDefinition(
                        false,
                        rigCatalogPath,
                        "Actor rig catalog contains a non-object rig entry.",
                        []);
                }

                var rig = ParseRig(rigElement);
                if (rig is null)
                {
                    return new ActorRigCatalogDefinition(
                        false,
                        rigCatalogPath,
                        "Actor rig catalog contains an invalid rig definition.",
                        []);
                }

                rigs.Add(rig);
            }

            return new ActorRigCatalogDefinition(true, rigCatalogPath, null, rigs);
        }
        catch (JsonException)
        {
            return new ActorRigCatalogDefinition(
                false,
                rigCatalogPath,
                "Actor rig catalog JSON could not be parsed.",
                []);
        }
        catch (IOException)
        {
            return new ActorRigCatalogDefinition(
                false,
                rigCatalogPath,
                "Actor rig catalog could not be read from disk.",
                []);
        }
    }

    public string? ResolveRigCatalogPath()
    {
        if (!_options.Roots.TryGetValue("game_client_assets", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var assetsRoot = Path.GetFullPath(configured);
        var clientRoot = Directory.GetParent(assetsRoot);
        if (clientRoot is null)
        {
            return null;
        }

        return Path.Combine(clientRoot.FullName, RigCatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static ActorRigDefinition? ParseRig(JsonElement rigElement)
    {
        if (!TryReadRequiredString(rigElement, "rig_id", out var rigId)
            || !TryReadRequiredInt(rigElement, "schema_version", out var schemaVersion)
            || schemaVersion != SupportedSchemaVersion
            || !rigElement.TryGetProperty("layers", out var layersElement)
            || layersElement.ValueKind != JsonValueKind.Object
            || !rigElement.TryGetProperty("sockets", out var socketsElement)
            || socketsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var layers = new List<ActorRigLayerDefinition>();
        foreach (var property in layersElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !TryReadRequiredString(property.Value, "binding_type", out var bindingType)
                || !TryReadRequiredString(property.Value, "default_render_plane", out var defaultRenderPlane)
                || !TryReadDirectionalZIndexes(property.Value, "z_index_by_direction", out var zIndexes))
            {
                return null;
            }

            layers.Add(new ActorRigLayerDefinition(
                property.Name,
                bindingType,
                defaultRenderPlane,
                zIndexes));
        }

        var sockets = new List<ActorRigSocketDefinition>();
        foreach (var property in socketsElement.EnumerateObject())
        {
            if (!TryReadDirectionalPoints(property.Value, out var points))
            {
                return null;
            }

            sockets.Add(new ActorRigSocketDefinition(property.Name, points));
        }

        return new ActorRigDefinition(rigId, schemaVersion, layers, sockets);
    }

    private static bool TryReadDirectionalZIndexes(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, int> zIndexes)
    {
        zIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!element.TryGetProperty(propertyName, out var zElement)
            || zElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var ordered = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var direction in DirectionOrder())
        {
            if (!zElement.TryGetProperty(direction, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var parsed))
            {
                return false;
            }

            ordered[direction] = parsed;
        }

        zIndexes = ordered;
        return true;
    }

    private static bool TryReadDirectionalPoints(
        JsonElement element,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> points)
    {
        points = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var ordered = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal);
        foreach (var direction in DirectionOrder())
        {
            if (!element.TryGetProperty(direction, out var framesElement)
                || framesElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var frames = new Dictionary<string, SourcePixelPointDefinition>(StringComparer.Ordinal);
            foreach (var frame in FrameOrder())
            {
                if (!framesElement.TryGetProperty(frame, out var pointElement)
                    || !TryReadPoint(pointElement, out var point))
                {
                    return false;
                }

                frames[frame] = point;
            }

            ordered[direction] = frames;
        }

        points = ordered;
        return true;
    }

    private static bool TryReadPoint(JsonElement element, out SourcePixelPointDefinition point)
    {
        point = new SourcePixelPointDefinition(0, 0);
        return TryReadRequiredInt(element, "x", out var x)
            && TryReadRequiredInt(element, "y", out var y)
            && (point = new SourcePixelPointDefinition(x, y)) is not null;
    }

    private static bool TryReadRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadRequiredInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out value))
        {
            return false;
        }

        return true;
    }

    private static string[] DirectionOrder() => ["N", "E", "S", "W"];

    private static string[] FrameOrder() => ["1", "2", "3", "4"];
}
