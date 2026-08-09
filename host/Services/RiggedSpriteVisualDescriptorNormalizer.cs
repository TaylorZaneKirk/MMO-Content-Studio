using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static class RiggedSpriteVisualDescriptorNormalizer
{
    public static bool Equivalent(
        RiggedSpriteVisualDescriptor? left,
        RiggedSpriteVisualDescriptor? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SchemaVersion == right.SchemaVersion
            && string.Equals(left.RigId, right.RigId, StringComparison.Ordinal)
            && string.Equals(left.CalibrationId, right.CalibrationId, StringComparison.Ordinal)
            && string.Equals(left.PosePolicy, right.PosePolicy, StringComparison.Ordinal)
            && string.Equals(left.FixedDirection, right.FixedDirection, StringComparison.Ordinal)
            && left.FixedFrame == right.FixedFrame
            && left.ContainsLegacyBaseLayers == right.ContainsLegacyBaseLayers
            && left.CosmeticItemIds.Count == right.CosmeticItemIds.Count
            && left.CosmeticItemIds.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(right.CosmeticItemIds.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    public static (string VisualMode, RiggedSpriteVisualDescriptor? CompositeVisual) Normalize(
        string? visualMode,
        RiggedSpriteVisualDescriptor? compositeVisual)
    {
        var normalizedVisualMode = (visualMode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedVisualMode == ActorVisualModes.FlatSprite)
        {
            return (ActorVisualModes.FlatSprite, null);
        }

        if (normalizedVisualMode != ActorVisualModes.CompositeRig || compositeVisual is null)
        {
            return (normalizedVisualMode, compositeVisual);
        }

        if (compositeVisual.ContainsLegacyBaseLayers)
        {
            return (ActorVisualModes.CompositeRig, compositeVisual);
        }

        var posePolicy = compositeVisual.PosePolicy?.Trim().ToLowerInvariant() ?? string.Empty;
        var cosmeticItemIds = compositeVisual.CosmeticItemIds
            .OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.Ordinal);
        var calibrationId = string.IsNullOrWhiteSpace(compositeVisual.CalibrationId)
            ? null
            : compositeVisual.CalibrationId.Trim();

        return (ActorVisualModes.CompositeRig, new RiggedSpriteVisualDescriptor(
            1,
            compositeVisual.RigId?.Trim() ?? string.Empty,
            calibrationId,
            posePolicy,
            posePolicy == "fixed" ? compositeVisual.FixedDirection?.Trim().ToUpperInvariant() : null,
            posePolicy == "fixed" ? compositeVisual.FixedFrame : null,
            cosmeticItemIds));
    }
}
