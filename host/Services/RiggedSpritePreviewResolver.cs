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
                    overlayRect,
                    overlayRect.X,
                    overlayRect.Y,
                    activeOverlay.ZIndexByDirection.GetValueOrDefault(direction) - baseZ));
            }
        }

        return new RiggedSpritePreviewDefinition(
            baseAssetFilePath,
            sourceWidth,
            sourceHeight,
            direction,
            frame.Value,
            cosmetics.OrderBy(cosmetic => cosmetic.ZIndex).ThenBy(cosmetic => cosmetic.ItemId, StringComparer.Ordinal).ToArray(),
            overlays.OrderBy(overlay => overlay.ZIndex).ToArray());
    }

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
