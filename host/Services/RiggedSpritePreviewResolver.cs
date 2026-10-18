using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class RiggedSpritePreviewResolver
{
    private const string AppearanceAssetPrefix = "res://assets/actors/player/";
    private readonly ActorAppearanceCatalogService _catalogService;
    private readonly ItemAssetService _assetService;

    public RiggedSpritePreviewResolver(
        ActorAppearanceCatalogService catalogService,
        ItemAssetService assetService)
    {
        _catalogService = catalogService;
        _assetService = assetService;
    }

    public RiggedSpritePreviewDefinition? Resolve(
        string baseAssetFilePath,
        int sourceWidth,
        int sourceHeight,
        RiggedSpriteVisualDescriptor? descriptor,
        string? requestedDirection,
        int? requestedFrame)
    {
        if (descriptor is null || string.IsNullOrWhiteSpace(baseAssetFilePath))
        {
            return null;
        }

        var catalog = _catalogService.LoadRiggedSpriteCatalog();
        var rig = catalog.Rigs.SingleOrDefault(candidate => candidate.RigId == descriptor.RigId);
        if (!catalog.Available || rig is null)
        {
            return null;
        }

        var direction = descriptor.PosePolicy == "fixed" ? descriptor.FixedDirection : NormalizeDirection(requestedDirection);
        var frame = descriptor.PosePolicy == "fixed" ? descriptor.FixedFrame : NormalizeFrame(requestedFrame);
        if (direction is null || frame is null)
        {
            return null;
        }

        var frameKey = frame.Value.ToString();
        var calibration = descriptor.CalibrationId is null
            ? null
            : catalog.Calibrations.SingleOrDefault(candidate => candidate.CalibrationId == descriptor.CalibrationId);
        var baseLayer = rig.Layers.SingleOrDefault(layer => layer.LayerId == rig.SolidSpriteBaseLayerId)
            ?? rig.Layers.SingleOrDefault(layer => layer.LayerId == "body");
        var baseZ = baseLayer?.ZIndexByDirection.GetValueOrDefault(direction) ?? 0;
        var cosmetics = new List<RiggedSpritePreviewCosmeticDefinition>();
        var overlays = new List<RiggedSpritePreviewOverlayDefinition>();

        foreach (var selection in descriptor.CosmeticItemIds.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var visual = catalog.EquippedVisuals.SingleOrDefault(candidate =>
                candidate.ItemId == selection.Value &&
                candidate.RigId == rig.RigId &&
                candidate.RenderLayerId == selection.Key &&
                candidate.BindingType == "socket");
            if (visual is null || string.IsNullOrWhiteSpace(visual.SocketId) || string.IsNullOrWhiteSpace(visual.AssetKey)
                || IsPoseEnabled(visual.HiddenPoses, direction, frameKey))
            {
                continue;
            }

            var socket = rig.Sockets.SingleOrDefault(candidate => candidate.SocketId == visual.SocketId);
            var socketPosition = FindPoint(calibration?.SocketOverrides, visual.SocketId, direction, frameKey)
                ?? FindPoint(socket?.Positions, direction, frameKey);
            var gripAnchor = FindPoint(visual.GripAnchors, direction, frameKey);
            if (socketPosition is null || gripAnchor is null)
            {
                continue;
            }

            var itemPath = ResolveAppearanceFrame(visual.RenderLayerId, visual.AssetKey, direction, frame.Value);
            if (itemPath is null || !TryReadPngDimensions(itemPath, out var itemWidth, out _))
            {
                continue;
            }

            var flip = IsPoseEnabled(visual.FlipPoses, direction, frameKey);
            var anchorX = flip ? itemWidth - 1 - gripAnchor.X : gripAnchor.X;
            var nudge = visual.Nudge ?? new SourcePixelPointDefinition(0, 0);
            var itemLayer = rig.Layers.SingleOrDefault(layer => layer.LayerId == visual.RenderLayerId);
            var itemZ = (itemLayer?.ZIndexByDirection.GetValueOrDefault(direction) ?? baseZ) - baseZ;
            var activeOverlay = rig.ForegroundOverlays.FirstOrDefault(overlay => overlay.SocketId == visual.SocketId
                && overlay.SourceLayerId == rig.SolidSpriteBaseLayerId);
            var overlayRect = activeOverlay is null ? null
                : FindRectangle(calibration?.ForegroundOverlayOverrides, activeOverlay.OverlayId, direction, frameKey)
                    ?? FindRectangle(activeOverlay.SourceRectByDirection, direction, frameKey);
            var itemOverGrip = IsPoseEnabled(visual.ItemOverGripPoses, direction, frameKey);
            if (itemOverGrip && activeOverlay is not null)
            {
                itemZ = activeOverlay.ZIndexByDirection.GetValueOrDefault(direction) - baseZ + 1;
            }
            else if (direction == "N")
            {
                // Solid actor previews use the rear-facing held-item convention.
                itemZ = Math.Min(itemZ, -1);
            }

            cosmetics.Add(new RiggedSpritePreviewCosmeticDefinition(
                visual.ItemId,
                itemPath,
                socketPosition.X - anchorX + nudge.X,
                socketPosition.Y - gripAnchor.Y + nudge.Y,
                itemZ,
                flip));

            if (overlayRect is not null && activeOverlay is not null)
            {
                overlays.Add(new RiggedSpritePreviewOverlayDefinition(
                    activeOverlay.OverlayId,
                    overlayRect,
                    overlayRect.X,
                    overlayRect.Y,
                    activeOverlay.ZIndexByDirection.GetValueOrDefault(direction) - baseZ));
            }
        }

        var baseFilePath = ResolveBaseFrame(
            baseAssetFilePath,
            direction,
            frame.Value,
            descriptor.PosePolicy == "actor_pose");
        var previewSourceWidth = sourceWidth;
        var previewSourceHeight = sourceHeight;
        if (TryReadPngDimensions(baseFilePath, out var resolvedSourceWidth, out var resolvedSourceHeight))
        {
            previewSourceWidth = resolvedSourceWidth;
            previewSourceHeight = resolvedSourceHeight;
        }

        return new RiggedSpritePreviewDefinition(
            baseFilePath,
            previewSourceWidth,
            previewSourceHeight,
            direction,
            frame.Value,
            cosmetics.OrderBy(cosmetic => cosmetic.ZIndex).ThenBy(cosmetic => cosmetic.ItemId, StringComparer.Ordinal).ToArray(),
            overlays.OrderBy(overlay => overlay.ZIndex).ToArray());
    }

    // PlayerComposite selects legacy metadata by asset key, not published item ID.
    // Keep its layer order and attachment math here; the existing native/browser
    // preview consumers draw the resolved images without owning pose decisions.
    public RiggedSpritePreviewDefinition? ResolveHumanLayers(
        IReadOnlyDictionary<string,string>? layers, string? direction, int? frame)
    {
        if (layers is null) return null;
        direction = direction?.ToUpperInvariant() ?? "S";
        if (direction is not ("N" or "E" or "S" or "W")) return null;
        var pose = Math.Clamp(frame ?? 2, 1, 4);
        var frameKey = pose.ToString();
        var rig = _catalogService.LoadRigCatalog().Rigs.FirstOrDefault(r => r.RigId == "humanoid_v1");
        var assetsRoot = _assetService.GetGameAssetsRoot();
        if (rig is null || assetsRoot is null) return null;
        var legacyPath = Path.Combine(assetsRoot, "..", "actors", "appearance", "data", "equipped_visuals", "catalog_v1.json");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<HumanVisualCatalog>(File.ReadAllText(legacyPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
        var resolved = new List<RiggedSpritePreviewCosmeticDefinition>();
        var resolvedFrames = new Dictionary<string,int>();
        var activeSockets = new HashSet<string>();
        var slots = new[] { "cape", "legs", "boots", "body", "left_hand", "right_hand", "gloves", "head" };
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (!layers.TryGetValue(slot, out var key) || string.IsNullOrEmpty(key)) continue;
            if (!Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$")) return null;
            var path = ResolveAppearanceFrame(slot, key, direction, pose);
            if (path is null || !TryReadPngDimensions(path, out var width, out _)) return null;
            var resolvedFrame = int.Parse(Regex.Match(Path.GetFileName(path), "-F([1-4])-").Groups[1].Value);
            resolvedFrames[slot] = resolvedFrame;
            var visual = legacy?.EquippedVisuals.LastOrDefault(v => v.AssetKey == key && v.RigId == rig.RigId);
            var depth = rig.Layers.First(l => l.LayerId == slot).ZIndexByDirection.GetValueOrDefault(direction);
            if (visual?.ZIndexOverrideByDirection?.TryGetValue(direction, out var overridden) == true) depth = overridden;
            var flip = false; var x = 0; var y = 0;
            if (visual?.RenderLayerId == slot)
            {
                if (IsPoseEnabled(visual.HiddenPoses, direction, frameKey)) continue;
                flip = IsPoseEnabled(visual.FlipX, direction, frameKey);
                if (IsPoseEnabled(visual.ItemOverGrip, direction, frameKey))
                {
                    var depths = rig.ForegroundOverlays.Where(o => o.SocketId == visual.SocketId &&
                        FindRectangle(o.SourceRectByDirection, direction, frameKey) is not null)
                        .Select(o => o.ZIndexByDirection.GetValueOrDefault(direction)).ToArray();
                    depth = (depths.Length > 0 ? depths.Max() : Math.Max(depth, rig.Layers.Max(l => l.ZIndexByDirection.GetValueOrDefault(direction)))) + 1;
                }
                if (visual.BindingType == "socket")
                {
                    var socket = rig.Sockets.FirstOrDefault(p => p.SocketId == visual.SocketId);
                    var point = FindPoint(socket?.Positions, direction, frameKey);
                    var grip = FindPoint(visual.GripAnchors, direction, frameKey);
                    if (point is not null && grip is not null)
                    {
                        x = point.X - (flip ? width - 1 - grip.X : grip.X) + (visual.Nudge?.X ?? 0);
                        y = point.Y - grip.Y + (visual.Nudge?.Y ?? 0);
                        activeSockets.Add(visual.SocketId!);
                    }
                }
            }
            // Encode stable scene sibling order for equal-depth body/legs/head.
            resolved.Add(new(slot, path, x, y, depth * 10 + index, flip));
        }
        var body = resolved.FirstOrDefault(p => p.ItemId == "body");
        if (body is null || !TryReadPngDimensions(body.FilePath, out var sourceWidth, out var sourceHeight)) return null;
        var overlays = new List<RiggedSpritePreviewOverlayDefinition>();
        foreach (var overlay in rig.ForegroundOverlays)
        {
            if (!activeSockets.Contains(overlay.SocketId)) continue;
            // The humanoid rig's grip crops come from its body layer.
            if (overlay.SourceLayerId != "body") return null;
            var rect = FindRectangle(overlay.SourceRectByDirection, direction, resolvedFrames["body"].ToString());
            if (rect is null || rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > sourceWidth || rect.Y + rect.Height > sourceHeight) continue;
            overlays.Add(new(overlay.OverlayId, rect, rect.X, rect.Y,
                overlay.ZIndexByDirection.GetValueOrDefault(direction) * 10 - body.ZIndex));
        }
        return new(body.FilePath, sourceWidth, sourceHeight, direction, pose,
            resolved.Where(p => p.ItemId != "body").Select(p => p with { X = p.X - body.X, Y = p.Y - body.Y, ZIndex = p.ZIndex - body.ZIndex }).ToArray(), overlays);
    }

    private sealed record HumanVisualCatalog(IReadOnlyList<HumanVisual> EquippedVisuals);
    private sealed record HumanVisual(string AssetKey, string RigId, string BindingType, string RenderLayerId,
        string? SocketId, SourcePixelPointDefinition? Nudge,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,SourcePixelPointDefinition>>? GripAnchors,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,bool>>? FlipX,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,bool>>? HiddenPoses,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,bool>>? ItemOverGrip,
        IReadOnlyDictionary<string,int>? ZIndexOverrideByDirection);

    private string? ResolveAppearanceFrame(string renderLayerId, string assetKey, string direction, int frame)
    {
        foreach (var candidate in FrameCandidates(direction, frame))
        {
            var resolution = _assetService.ResolveGameAssetPng(
                $"{AppearanceAssetPrefix}{renderLayerId}/{assetKey}-F{candidate}-{direction}.png",
                "equipped visual texture");
            if (resolution.Exists)
            {
                return resolution.FilePath;
            }
        }
        return null;
    }

    public static string ResolveBaseFrame(
        string baseAssetFilePath,
        string direction,
        int frame,
        bool resolveNormalizedMobFrame = false)
    {
        var fileName = Path.GetFileName(baseAssetFilePath);
        var charsMatch = Regex.Match(
            fileName,
            "^Chars_\\d+_([^-]+)-F[1-4]-[NESW]\\.png$",
            RegexOptions.CultureInvariant);
        if (charsMatch.Success)
        {
            var spriteGroup = charsMatch.Groups[1].Value;
            var charsCandidate = ResolveExactCharsFrame(baseAssetFilePath, direction, frame);
            if (charsCandidate is not null)
            {
                return charsCandidate;
            }

            var fallbackCandidate = Path.Combine(
                Path.GetDirectoryName(baseAssetFilePath) ?? string.Empty,
                $"Chars_{CharsFramePrefix(direction, 1)}_{spriteGroup}-F1-{direction}.png");
            if (File.Exists(fallbackCandidate))
            {
                return fallbackCandidate;
            }

            return baseAssetFilePath;
        }

        var match = Regex.Match(baseAssetFilePath, "-F[1-4]-[NESW]\\.png$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            var normalizedMobFrame = resolveNormalizedMobFrame
                ? ResolveNormalizedMobFrame(baseAssetFilePath, direction, frame)
                : null;
            if (normalizedMobFrame is not null)
            {
                return normalizedMobFrame;
            }

            return baseAssetFilePath;
        }

        var candidate = baseAssetFilePath[..match.Index] + $"-F{frame}-{direction}.png";
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var genericFallbackCandidate = baseAssetFilePath[..match.Index] + $"-F1-{direction}.png";
        return File.Exists(genericFallbackCandidate) ? genericFallbackCandidate : baseAssetFilePath;
    }

    private static string? ResolveNormalizedMobFrame(string baseAssetFilePath, string direction, int frame)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(baseAssetFilePath) ?? string.Empty);
        if (!string.Equals(directory.Name, "mobs", StringComparison.Ordinal)
            || !string.Equals(directory.Parent?.Name, "objects", StringComparison.Ordinal)
            || !string.Equals(directory.Parent?.Parent?.Name, "maps", StringComparison.Ordinal)
            || !string.Equals(directory.Parent?.Parent?.Parent?.Name, "assets", StringComparison.Ordinal))
        {
            return null;
        }

        var assetKey = Path.GetFileNameWithoutExtension(baseAssetFilePath);
        if (!Regex.IsMatch(assetKey, "^[a-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        var candidate = Path.Combine(
            directory.Parent!.Parent!.Parent!.FullName,
            "actors",
            "mobs",
            $"{assetKey}-F{frame}-{direction}.png");
        return File.Exists(candidate) ? candidate : null;
    }

    public static string? ResolveExactCharsFrame(string baseAssetFilePath, string direction, int frame)
    {
        var fileName = Path.GetFileName(baseAssetFilePath);
        var match = Regex.Match(
            fileName,
            "^Chars_\\d+_([^-]+)-F[1-4]-[NESW]\\.png$",
            RegexOptions.CultureInvariant);
        if (!match.Success || direction is not ("N" or "E" or "S" or "W") || frame is < 1 or > 4)
        {
            return null;
        }

        var candidate = Path.Combine(
            Path.GetDirectoryName(baseAssetFilePath) ?? string.Empty,
            $"Chars_{CharsFramePrefix(direction, frame)}_{match.Groups[1].Value}-F{frame}-{direction}.png");
        return File.Exists(candidate) ? candidate : null;
    }

    private static int CharsFramePrefix(string direction, int frame) => (direction, frame) switch
    {
        ("E", 4) => 143,
        ("W", 4) => 144,
        ("N", 4) => 142,
        ("S", 4) => 141,
        ("E", _) => 128 + frame,
        ("W", _) => 131 + frame,
        ("N", _) => 134 + frame,
        _ => 137 + frame
    };

    private static IEnumerable<int> FrameCandidates(string direction, int frame)
    {
        yield return frame;
        if (direction == "N" && frame != 4)
        {
            yield return 4;
        }
        if (frame != 3)
        {
            yield return 3;
        }
    }

    private static string NormalizeDirection(string? direction) => direction is "N" or "E" or "S" or "W" ? direction : "S";
    private static int NormalizeFrame(int? frame) => frame is >= 1 and <= 4 ? frame.Value : 1;

    private static bool IsPoseEnabled(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? poses, string direction, string frame) =>
        poses is not null && poses.TryGetValue(direction, out var frames) && frames.TryGetValue(frame, out var enabled) && enabled;

    private static SourcePixelPointDefinition? FindPoint(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>? values,
        string direction,
        string frame) => values is not null && values.TryGetValue(direction, out var frames) && frames.TryGetValue(frame, out var value) ? value : null;

    private static SourcePixelPointDefinition? FindPoint(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>>? values,
        string key,
        string direction,
        string frame) => values is not null && values.TryGetValue(key, out var directions) ? FindPoint(directions, direction, frame) : null;

    private static SourcePixelRectangleDefinition? FindRectangle(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition?>> values,
        string direction,
        string frame) => values.TryGetValue(direction, out var frames) && frames.TryGetValue(frame, out var value) ? value : null;

    private static SourcePixelRectangleDefinition? FindRectangle(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>>? values,
        string key,
        string direction,
        string frame) => values is not null && values.TryGetValue(key, out var directions)
            && directions.TryGetValue(direction, out var frames) && frames.TryGetValue(frame, out var value) ? value : null;

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        if (stream.Read(header) != header.Length || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4e || header[3] != 0x47)
        {
            return false;
        }
        width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return width > 0 && height > 0;
    }
}
