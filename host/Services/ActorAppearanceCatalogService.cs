using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ActorAppearanceCatalogService
{
    private const int SupportedSchemaVersion = 1;
    private const string RigCatalogRelativePath = "actors/appearance/data/rigs/catalog_v1.json";
    private const string RigCalibrationCatalogRelativePath = "actors/appearance/data/rig_calibrations/catalog_v1.json";
    private const string EquippedVisualCatalogRelativePath = "actors/appearance/data/equipped_visuals/published_catalog_v1.json";
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

        var configuredRoot = Path.GetFullPath(configured);
        foreach (var candidate in RigCatalogCandidates(configuredRoot))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return RigCatalogCandidates(configuredRoot).FirstOrDefault();
    }

    public string? ResolveRigCalibrationCatalogPath() =>
        ResolveCatalogPath(RigCalibrationCatalogRelativePath);

    public ActorRiggedSpriteCatalogDefinition LoadRiggedSpriteCatalog()
    {
        var rigCatalog = LoadRigCatalog();
        if (!rigCatalog.Available)
        {
            return new ActorRiggedSpriteCatalogDefinition(
                false,
                rigCatalog.Message,
                [],
                [],
                [],
                false,
                rigCatalog.Message,
                rigCatalog.SourcePath);
        }

        var calibrationPath = ResolveCatalogPath(RigCalibrationCatalogRelativePath);
        var equippedVisualPath = ResolveCatalogPath(EquippedVisualCatalogRelativePath);
        var calibrations = LoadCatalog<ActorRigCalibrationDefinition>(
            calibrationPath,
            "rig calibration",
            TryReadCalibrations);
        var equippedVisuals = LoadCatalog<PublishedEquippedVisualDefinition>(
            equippedVisualPath,
            "published equipped-visual",
            TryReadEquippedVisuals);

        return new ActorRiggedSpriteCatalogDefinition(
            true,
            null,
            rigCatalog.Rigs,
            calibrations.Entries,
            equippedVisuals.Entries,
            true,
            null,
            rigCatalog.SourcePath,
            calibrations.Available,
            calibrations.Message,
            calibrationPath,
            equippedVisuals.Available,
            equippedVisuals.Message,
            equippedVisualPath);
    }

    public ActorAppearanceOptionsDefinition LoadOptions()
    {
        var catalog = LoadRiggedSpriteCatalog();
        return new ActorAppearanceOptionsDefinition(
            catalog.Available,
            catalog.Message,
            [
                new AuthoringOption(ActorVisualModes.FlatSprite, "Flat Sprite"),
                new AuthoringOption(ActorVisualModes.CompositeRig, "Rigged Sprite")
            ],
            catalog.Rigs,
            catalog.Calibrations,
            catalog.EquippedVisuals.Where(visual => visual.BindingType == "socket").ToArray(),
            catalog.RigsAvailable,
            catalog.RigMessage,
            catalog.RigCatalogPath,
            catalog.CalibrationsAvailable,
            catalog.CalibrationMessage,
            catalog.CalibrationCatalogPath,
            catalog.EquippedVisualsAvailable,
            catalog.EquippedVisualMessage,
            catalog.EquippedVisualCatalogPath);
    }

    private static CatalogLoadResult<T> LoadCatalog<T>(
        string? path,
        string catalogName,
        TryReadCatalog<T> reader)
    {
        if (path is null || !File.Exists(path))
        {
            return new CatalogLoadResult<T>(
                false,
                $"The canonical MMO Project {catalogName} catalog is unavailable.",
                []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return reader(document.RootElement, out var entries)
                ? new CatalogLoadResult<T>(true, null, entries)
                : new CatalogLoadResult<T>(false, $"The canonical MMO Project {catalogName} catalog is invalid.", []);
        }
        catch (JsonException)
        {
            return new CatalogLoadResult<T>(false, $"The canonical MMO Project {catalogName} catalog JSON could not be parsed.", []);
        }
        catch (IOException)
        {
            return new CatalogLoadResult<T>(false, $"The canonical MMO Project {catalogName} catalog could not be read from disk.", []);
        }
    }

    private delegate bool TryReadCatalog<T>(JsonElement root, out IReadOnlyList<T> entries);

    private sealed record CatalogLoadResult<T>(bool Available, string? Message, IReadOnlyList<T> Entries);

    private string? ResolveCatalogPath(string relativePath)
    {
        if (!_options.Roots.TryGetValue("game_client_assets", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var configuredRoot = Path.GetFullPath(configured);
        return CatalogCandidates(configuredRoot, relativePath).FirstOrDefault(File.Exists)
            ?? CatalogCandidates(configuredRoot, relativePath).FirstOrDefault();
    }

    private static IReadOnlyList<string> RigCatalogCandidates(string configuredRoot)
        => CatalogCandidates(configuredRoot, RigCatalogRelativePath);

    private static IReadOnlyList<string> CatalogCandidates(string configuredRoot, string relativePath)
    {
        var candidates = new List<string>();
        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        void AddCandidate(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                return;
            }

            var candidate = Path.GetFullPath(Path.Combine(basePath, normalizedRelativePath));
            if (!candidates.Contains(candidate, StringComparer.Ordinal))
            {
                candidates.Add(candidate);
            }
        }

        AddCandidate(configuredRoot);

        if (string.Equals(Path.GetFileName(configuredRoot), "assets", StringComparison.OrdinalIgnoreCase))
        {
            var clientRoot = Directory.GetParent(configuredRoot);
            if (clientRoot is not null)
            {
                AddCandidate(clientRoot.FullName);
            }
        }

        return candidates;
    }

    private static bool TryReadCalibrations(JsonElement root, out IReadOnlyList<ActorRigCalibrationDefinition> calibrations)
    {
        calibrations = [];
        if (!HasSupportedSchemaVersion(root) || !root.TryGetProperty("calibrations", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<ActorRigCalibrationDefinition>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryReadRequiredString(entry, "calibration_id", out var calibrationId) ||
                !TryReadRequiredString(entry, "rig_id", out var rigId))
            {
                return false;
            }

            if (!TryReadSparseSocketOverrides(entry, out var socketOverrides) ||
                !TryReadSparseDirectionalRectangles(entry, "foreground_overlays", out var overlayOverrides))
            {
                return false;
            }

            parsed.Add(new ActorRigCalibrationDefinition(calibrationId, rigId, socketOverrides, overlayOverrides));
        }

        calibrations = parsed;
        return true;
    }

    private static bool TryReadEquippedVisuals(JsonElement root, out IReadOnlyList<PublishedEquippedVisualDefinition> equippedVisuals)
    {
        equippedVisuals = [];
        if (!HasSupportedSchemaVersion(root) || !root.TryGetProperty("equipped_visuals", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<PublishedEquippedVisualDefinition>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryReadRequiredString(entry, "item_id", out var itemId) ||
                !TryReadRequiredString(entry, "rig_id", out var rigId) ||
                !TryReadRequiredString(entry, "binding_type", out var bindingType) ||
                !TryReadRequiredString(entry, "render_layer_id", out var renderLayerId) ||
                !TryReadOptionalString(entry, "asset_key", out var assetKey) ||
                !TryReadOptionalString(entry, "socket_id", out var socketId) ||
                !TryReadOptionalPoint(entry, "nudge", out var nudge) ||
                !TryReadSparseDirectionalPoints(entry, "grip_anchors", out var gripAnchors) ||
                !TryReadSparseDirectionalBooleans(entry, "flip_poses", out var flipPoses) ||
                !TryReadSparseDirectionalBooleans(entry, "hidden_poses", out var hiddenPoses) ||
                !TryReadSparseDirectionalBooleans(entry, "item_over_grip_poses", out var itemOverGripPoses))
            {
                return false;
            }

            parsed.Add(new PublishedEquippedVisualDefinition(
                itemId, rigId, bindingType, renderLayerId, assetKey, socketId, nudge,
                gripAnchors, flipPoses, hiddenPoses, itemOverGripPoses));
        }

        equippedVisuals = parsed;
        return true;
    }

    private static bool HasSupportedSchemaVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("schema_version", out var schemaVersion) &&
        schemaVersion.ValueKind == JsonValueKind.Number &&
        schemaVersion.TryGetInt32(out var value) &&
        value == SupportedSchemaVersion;

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

        var foregroundOverlays = new List<ActorRigForegroundOverlayDefinition>();
        if (rigElement.TryGetProperty("foreground_overlays", out var foregroundOverlaysElement)
            && foregroundOverlaysElement.ValueKind != JsonValueKind.Null
            && !TryReadForegroundOverlays(
                foregroundOverlaysElement,
                layers.Select(layer => layer.LayerId),
                sockets.Select(socket => socket.SocketId),
                out foregroundOverlays))
        {
            return null;
        }

        TryReadOptionalString(rigElement, "solid_sprite_base_layer_id", out var solidSpriteBaseLayerId);
        return new ActorRigDefinition(rigId, schemaVersion, layers, sockets, foregroundOverlays, solidSpriteBaseLayerId);
    }

    private static bool TryReadForegroundOverlays(
        JsonElement element,
        IEnumerable<string> layerIds,
        IEnumerable<string> socketIds,
        out List<ActorRigForegroundOverlayDefinition> overlays)
    {
        overlays = [];
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var knownLayerIds = layerIds.ToHashSet(StringComparer.Ordinal);
        var knownSocketIds = socketIds.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !TryReadRequiredString(property.Value, "socket_id", out var socketId)
                || !TryReadRequiredString(property.Value, "source_layer_id", out var sourceLayerId)
                || !knownSocketIds.Contains(socketId)
                || !knownLayerIds.Contains(sourceLayerId)
                || !TryReadDirectionalZIndexes(property.Value, "z_index_by_direction", out var zIndexes)
                || !TryReadOptionalDirectionalRectangles(property.Value, "source_rect_by_direction", out var sourceRects))
            {
                return false;
            }

            overlays.Add(new ActorRigForegroundOverlayDefinition(
                property.Name,
                socketId,
                sourceLayerId,
                zIndexes,
                sourceRects));
        }

        return true;
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

    private static bool TryReadOptionalDirectionalRectangles(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition?>> rectangles)
    {
        var ordered = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition?>>(StringComparer.Ordinal);
        foreach (var direction in DirectionOrder())
        {
            var frames = new Dictionary<string, SourcePixelRectangleDefinition?>(StringComparer.Ordinal);
            foreach (var frame in FrameOrder())
            {
                frames[frame] = null;
            }

            ordered[direction] = frames;
        }

        if (!element.TryGetProperty(propertyName, out var rectanglesElement)
            || rectanglesElement.ValueKind == JsonValueKind.Null)
        {
            rectangles = ordered;
            return true;
        }

        if (rectanglesElement.ValueKind != JsonValueKind.Object)
        {
            rectangles = ordered;
            return false;
        }

        foreach (var direction in DirectionOrder())
        {
            if (!rectanglesElement.TryGetProperty(direction, out var framesElement)
                || framesElement.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (framesElement.ValueKind != JsonValueKind.Object)
            {
                rectangles = ordered;
                return false;
            }

            var frames = new Dictionary<string, SourcePixelRectangleDefinition?>(StringComparer.Ordinal);
            foreach (var frame in FrameOrder())
            {
                if (!framesElement.TryGetProperty(frame, out var rectangleElement)
                    || rectangleElement.ValueKind == JsonValueKind.Null)
                {
                    frames[frame] = null;
                    continue;
                }

                if (!TryReadRectangle(rectangleElement, out var rectangle))
                {
                    rectangles = ordered;
                    return false;
                }

                frames[frame] = rectangle;
            }

            ordered[direction] = frames;
        }

        rectangles = ordered;
        return true;
    }

    private static bool TryReadPoint(JsonElement element, out SourcePixelPointDefinition point)
    {
        point = new SourcePixelPointDefinition(0, 0);
        return TryReadRequiredInt(element, "x", out var x)
            && TryReadRequiredInt(element, "y", out var y)
            && (point = new SourcePixelPointDefinition(x, y)) is not null;
    }

    private static bool TryReadOptionalPoint(JsonElement element, string propertyName, out SourcePixelPointDefinition? point)
    {
        point = null;
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryReadPoint(value, out var parsed))
        {
            return false;
        }

        point = parsed;
        return true;
    }

    private static bool TryReadOptionalString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadSparseDirectionalPoints(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> values)
    {
        var parsed = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal);
		values = parsed;
        var root = element;
        if (!string.IsNullOrEmpty(propertyName) && !element.TryGetProperty(propertyName, out root))
        {
            return true;
        }
        if (root.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var direction in root.EnumerateObject())
        {
            if (direction.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var frames = new Dictionary<string, SourcePixelPointDefinition>(StringComparer.Ordinal);
            foreach (var frame in direction.Value.EnumerateObject())
            {
                if (!TryReadPoint(frame.Value, out var point))
                {
                    return false;
                }
                frames[frame.Name] = point;
            }
            parsed[direction.Name] = frames;
        }
        values = parsed;
        return true;
    }

    private static bool TryReadSparseSocketOverrides(
        JsonElement element,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>> values)
    {
        var parsed = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>>(StringComparer.Ordinal);
		values = parsed;
        if (!element.TryGetProperty("sockets", out var root) || root.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var socket in root.EnumerateObject())
        {
            if (!TryReadSparseDirectionalPoints(socket.Value, string.Empty, out var directions))
            {
                return false;
            }
            parsed[socket.Name] = directions;
        }
        values = parsed;
        return true;
    }

    private static bool TryReadSparseDirectionalRectangles(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>> values)
    {
        var parsed = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>>(StringComparer.Ordinal);
		values = parsed;
        if (!element.TryGetProperty(propertyName, out var root) || root.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var overlay in root.EnumerateObject())
        {
            if (!overlay.Value.TryGetProperty("source_rect_by_direction", out var directions) || directions.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var parsedDirections = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>(StringComparer.Ordinal);
            foreach (var direction in directions.EnumerateObject())
            {
                if (direction.Value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }
                var frames = new Dictionary<string, SourcePixelRectangleDefinition>(StringComparer.Ordinal);
                foreach (var frame in direction.Value.EnumerateObject())
                {
                    if (!TryReadRectangle(frame.Value, out var rect))
                    {
                        return false;
                    }
                    frames[frame.Name] = rect;
                }
                parsedDirections[direction.Name] = frames;
            }
            parsed[overlay.Name] = parsedDirections;
        }
        values = parsed;
        return true;
    }

    private static bool TryReadSparseDirectionalBooleans(
        JsonElement element,
        string propertyName,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> values)
    {
        var parsed = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal);
		values = parsed;
        if (!element.TryGetProperty(propertyName, out var root) || root.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var direction in root.EnumerateObject())
        {
            if (direction.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var frames = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var frame in direction.Value.EnumerateObject())
            {
                if (frame.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    return false;
                }
                frames[frame.Name] = frame.Value.GetBoolean();
            }
            parsed[direction.Name] = frames;
        }
        values = parsed;
        return true;
    }

    private static bool TryReadRectangle(JsonElement element, out SourcePixelRectangleDefinition rectangle)
    {
        rectangle = new SourcePixelRectangleDefinition(0, 0, 0, 0);
        if (!TryReadRequiredInt(element, "x", out var x)
            || !TryReadRequiredInt(element, "y", out var y)
            || !TryReadRequiredInt(element, "width", out var width)
            || !TryReadRequiredInt(element, "height", out var height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        rectangle = new SourcePixelRectangleDefinition(x, y, width, height);
        return true;
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
