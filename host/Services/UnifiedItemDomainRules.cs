using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static partial class UnifiedItemDomainRules
{
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
                    record.WeaponProfile)
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
            weaponProfile);
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
    EquipmentCombatProfileDefinition? WeaponProfile);
