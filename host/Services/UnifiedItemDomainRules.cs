using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static partial class UnifiedItemDomainRules
{
    private static readonly string[] DirectionOrder = ["N", "E", "S", "W"];
    private static readonly string[] FrameOrder = ["1", "2", "3", "4"];

    public const int MaximumConsumableRequirements = 16;
    public const int MaximumConsumableEffects = 16;
    public const int MaximumEquipmentRequirements = 16;
    public const int MaximumEquipmentModifiers = 16;
    public const int MaximumMagnitude = 1_000_000;
    public const int MaximumAttackSpeedUnits = 60;
    public const int MaximumRangeTiles = 32;
    public const int MaximumToolCapabilities = 16;
    public const int MaximumPowerTier = 1_000;

    public static NormalizedItemDraft Normalize(
        string displayName,
        string iconTexturePath,
        ItemConsumableBehaviorDraft? consumableBehavior,
        ItemEquipmentMetadataDraft? equipment,
        IReadOnlyList<ItemToolCapabilityDraft>? toolCapabilities)
    {
        var normalizedEquipment = NormalizeEquipment(equipment);
        return new NormalizedItemDraft(
            NormalizeRequired(displayName),
            NormalizeRequired(iconTexturePath),
            NormalizeConsumable(consumableBehavior),
            normalizedEquipment,
            NormalizeToolCapabilities(toolCapabilities));
    }

    public static NormalizedItemDraft FromRecord(UnifiedItemRecord record) =>
        Normalize(
            record.DisplayName,
            record.IconTexturePath,
            record.ConsumableBehavior is null
                ? null
                : new ItemConsumableBehaviorDraft(
                    record.ConsumableBehavior.UseAction,
                    record.ConsumableBehavior.ConsumeQuantity,
                    record.ConsumableBehavior.ResultItemId,
                    record.ConsumableBehavior.SuccessMessage,
                    record.ConsumableBehavior.UsableInCombat,
                    record.ConsumableBehavior.CooldownMs,
                    record.ConsumableBehavior.UseAnimationId,
                    record.ConsumableBehavior.UseSoundResourcePath,
                    record.ConsumableRequirements,
                    record.ConsumableEffects),
            HasEquipmentMetadata(record)
                ? new ItemEquipmentMetadataDraft(
                    record.EquipmentSlotId,
                    record.RequiredStrength,
                    record.Requirements.Select(value => new EquipmentSkillRequirementDraft(value.SkillId, value.RequiredValue)).ToArray(),
                    record.SkillModifiers.Select(value => new EquipmentSkillModifierDraft(value.SkillId, value.ModifierValue)).ToArray(),
                    record.CombatBonuses,
                    record.WeaponProfile,
                    record.EquippedVisual is null
                        ? null
                        : new ItemEquippedVisualDraft(
                            record.EquippedVisual.AssetKey,
                            record.EquippedVisual.RigId,
                            record.EquippedVisual.BindingType,
                            record.EquippedVisual.RenderLayerId,
                            record.EquippedVisual.SocketId,
                            record.EquippedVisual.SecondarySocketId,
                            record.EquippedVisual.Nudge,
                            record.EquippedVisual.GripAnchors,
                            record.EquippedVisual.FlipXByPose,
                            record.EquippedVisual.HiddenPoses))
                : null,
            record.ToolCapabilities.Select(value => new ItemToolCapabilityDraft(
                value.CapabilityId,
                value.PowerTier,
                value.ActionAnimationId,
                value.EffectResourceId)).ToArray());

    public static bool HasEquipmentMetadata(UnifiedItemRecord record) =>
        record.EquipmentSlotId is not null
        || record.RequiredStrength != 1
        || record.Requirements.Count > 0
        || record.SkillModifiers.Count > 0
        || record.WeaponProfile is not null
        || (record.CombatBonuses is not null && !record.CombatBonuses.IsZero)
        || record.HasSkillRequirements
        || record.HasSkillModifiers
        || record.HasCombatProfile
        || record.HasCombatBonuses;

    public static string Classify(
        bool hasConsumable,
        NormalizedItemEquipmentMetadata? equipment,
        IReadOnlyList<ItemToolCapabilityDraft> toolCapabilities)
    {
        var labels = new List<string>();
        if (hasConsumable)
        {
            labels.Add("Consumable");
        }
        if (equipment?.WeaponProfile is not null)
        {
            labels.Add("Weapon");
        }
        else if (equipment is not null)
        {
            labels.Add("Equipment");
        }
        if (toolCapabilities.Count > 0)
        {
            labels.Add("Tool");
        }

        return labels.Count == 0 ? "Basic" : string.Join(" + ", labels);
    }

    public static string NormalizeRequired(string? value) =>
        (value ?? string.Empty).Trim();

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool HasDuplicateToolCapabilities(IReadOnlyList<ItemToolCapabilityDraft> capabilities) =>
        capabilities
            .GroupBy(value => value.CapabilityId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    public static bool IsHandSlot(string? slotId) =>
        slotId is "right_hand" or "left_hand";

    private static NormalizedItemConsumableBehavior? NormalizeConsumable(
        ItemConsumableBehaviorDraft? consumable)
    {
        if (consumable is null)
        {
            return null;
        }

        return new NormalizedItemConsumableBehavior(
            NormalizeRequired(consumable.UseAction),
            consumable.ConsumeQuantity,
            NormalizeOptional(consumable.ResultItemId),
            NormalizeOptional(consumable.SuccessMessage),
            consumable.UsableInCombat,
            consumable.CooldownMs,
            NormalizeOptional(consumable.UseAnimationId),
            NormalizeOptional(consumable.UseSoundResourcePath),
            (consumable.Requirements ?? [])
                .Select(value => new ConsumableRequirementDefinition(
                    value.RequirementIndex,
                    NormalizeRequired(value.RequirementType),
                    NormalizeRequired(value.TargetId),
                    value.MinimumValue))
                .OrderBy(value => value.RequirementIndex)
                .ThenBy(value => value.RequirementType, StringComparer.Ordinal)
                .ThenBy(value => value.TargetId, StringComparer.Ordinal)
                .ToArray(),
            (consumable.Effects ?? [])
                .Select(value => new ConsumableEffectDefinition(
                    value.EffectIndex,
                    NormalizeRequired(value.EffectType),
                    NormalizeRequired(value.TargetId),
                    value.MinimumAmount,
                    value.MaximumAmount))
                .OrderBy(value => value.EffectIndex)
                .ThenBy(value => value.EffectType, StringComparer.Ordinal)
                .ThenBy(value => value.TargetId, StringComparer.Ordinal)
                .ToArray());
    }

    private static NormalizedItemEquipmentMetadata? NormalizeEquipment(
        ItemEquipmentMetadataDraft? equipment)
    {
        if (equipment is null)
        {
            return null;
        }

        var slotId = NormalizeOptional(equipment.EquipmentSlotId);
        if (slotId is null)
        {
            return null;
        }

        var weaponProfile = IsHandSlot(slotId)
            ? NormalizeWeaponProfile(equipment.WeaponProfile)
            : null;
        return new NormalizedItemEquipmentMetadata(
            slotId,
            equipment.RequiredStrength,
            (equipment.Requirements ?? [])
                .Select(value => new EquipmentSkillRequirementDraft(
                    NormalizeRequired(value.SkillId),
                    value.RequiredValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            (equipment.SkillModifiers ?? [])
                .Select(value => new EquipmentSkillModifierDraft(
                    NormalizeRequired(value.SkillId),
                    value.ModifierValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            equipment.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero,
            weaponProfile,
            NormalizeEquippedVisual(equipment.EquippedVisual));
    }

    private static EquipmentCombatProfileDefinition? NormalizeWeaponProfile(
        EquipmentCombatProfileDefinition? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new EquipmentCombatProfileDefinition(
            NormalizeRequired(profile.ProfileId),
            NormalizeRequired(profile.AttackType),
            NormalizeOptional(profile.AccuracyStyle),
            profile.MinimumRangeTiles,
            profile.MaximumRangeTiles,
            profile.AttackSpeedUnits);
    }

    private static IReadOnlyList<ItemToolCapabilityDraft> NormalizeToolCapabilities(
        IReadOnlyList<ItemToolCapabilityDraft>? capabilities) =>
        (capabilities ?? [])
            .Select(value => new ItemToolCapabilityDraft(
                NormalizeRequired(value.CapabilityId),
                value.PowerTier,
                NormalizeOptional(value.ActionAnimationId),
                NormalizeOptional(value.EffectResourceId)))
            .Where(value => value.CapabilityId.Length > 0
                || value.PowerTier != 0
                || value.ActionAnimationId is not null
                || value.EffectResourceId is not null)
            .OrderBy(value => value.CapabilityId, StringComparer.Ordinal)
            .ToArray();

    private static NormalizedItemEquippedVisual? NormalizeEquippedVisual(
        ItemEquippedVisualDraft? equippedVisual)
    {
        if (equippedVisual is null)
        {
            return null;
        }

        var assetKey = NormalizeOptional(equippedVisual.AssetKey);
        var rigId = NormalizeOptional(equippedVisual.RigId);
        var bindingType = NormalizeOptional(equippedVisual.BindingType);
        var renderLayerId = NormalizeOptional(equippedVisual.RenderLayerId);

        if (assetKey is null
            && rigId is null
            && bindingType is null
            && renderLayerId is null
            && NormalizeOptional(equippedVisual.SocketId) is null
            && NormalizeOptional(equippedVisual.SecondarySocketId) is null
            && equippedVisual.Nudge is null
            && (equippedVisual.GripAnchors is null || equippedVisual.GripAnchors.Count == 0)
            && (equippedVisual.FlipXByPose is null || equippedVisual.FlipXByPose.Count == 0)
            && (equippedVisual.HiddenPoses is null || equippedVisual.HiddenPoses.Count == 0))
        {
            return null;
        }

        return new NormalizedItemEquippedVisual(
            assetKey,
            rigId,
            bindingType,
            renderLayerId,
            NormalizeOptional(equippedVisual.SocketId),
            NormalizeOptional(equippedVisual.SecondarySocketId),
            equippedVisual.Nudge ?? new SourcePixelPointDefinition(0, 0),
            NormalizeGripAnchors(equippedVisual.GripAnchors),
            NormalizeFlipXByPose(equippedVisual.FlipXByPose),
            NormalizeHiddenPoses(equippedVisual.HiddenPoses));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> NormalizeGripAnchors(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>? gripAnchors)
    {
        var normalized = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal);
        if (gripAnchors is null)
        {
            return normalized;
        }

        foreach (var direction in DirectionOrder)
        {
            if (!gripAnchors.TryGetValue(direction, out var frames) || frames is null)
            {
                continue;
            }

            var normalizedFrames = new Dictionary<string, SourcePixelPointDefinition>(StringComparer.Ordinal);
            foreach (var frame in FrameOrder)
            {
                if (!frames.TryGetValue(frame, out var point))
                {
                    continue;
                }

                normalizedFrames[frame] = new SourcePixelPointDefinition(point.X, point.Y);
            }

            if (normalizedFrames.Count > 0)
            {
                normalized[direction] = normalizedFrames;
            }
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> NormalizeFlipXByPose(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? flipXByPose)
    {
        var normalized = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal);
        if (flipXByPose is null)
        {
            return normalized;
        }

        foreach (var direction in DirectionOrder)
        {
            if (!flipXByPose.TryGetValue(direction, out var frames) || frames is null)
            {
                continue;
            }

            var normalizedFrames = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var frame in FrameOrder)
            {
                if (frames.TryGetValue(frame, out var flipX) && flipX)
                {
                    normalizedFrames[frame] = true;
                }
            }

            if (normalizedFrames.Count > 0)
            {
                normalized[direction] = normalizedFrames;
            }
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> NormalizeHiddenPoses(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? hiddenPoses) =>
        NormalizeFlipXByPose(hiddenPoses);

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex StableIdRegex();
}

public sealed record NormalizedItemDraft(
    string DisplayName,
    string IconTexturePath,
    NormalizedItemConsumableBehavior? ConsumableBehavior,
    NormalizedItemEquipmentMetadata? Equipment,
    IReadOnlyList<ItemToolCapabilityDraft> ToolCapabilities);

public sealed record NormalizedItemConsumableBehavior(
    string UseAction,
    int ConsumeQuantity,
    string? ResultItemId,
    string? SuccessMessage,
    bool UsableInCombat,
    int CooldownMs,
    string? UseAnimationId,
    string? UseSoundResourcePath,
    IReadOnlyList<ConsumableRequirementDefinition> Requirements,
    IReadOnlyList<ConsumableEffectDefinition> Effects);

public sealed record NormalizedItemEquipmentMetadata(
    string EquipmentSlotId,
    int RequiredStrength,
    IReadOnlyList<EquipmentSkillRequirementDraft> Requirements,
    IReadOnlyList<EquipmentSkillModifierDraft> SkillModifiers,
    EquipmentCombatBonusDefinition CombatBonuses,
    EquipmentCombatProfileDefinition? WeaponProfile,
    NormalizedItemEquippedVisual? EquippedVisual);

public sealed record NormalizedItemEquippedVisual(
    string? AssetKey,
    string? RigId,
    string? BindingType,
    string? RenderLayerId,
    string? SocketId,
    string? SecondarySocketId,
    SourcePixelPointDefinition Nudge,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> GripAnchors,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? FlipXByPose = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? HiddenPoses = null);
