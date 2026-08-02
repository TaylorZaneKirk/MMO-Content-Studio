using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class HandEquipmentAuthoringRegistry
{
    public const int CombatUnitMilliseconds = 600;
    public const string ActiveWeaponSlotId = "right_hand";

    private static readonly AuthoringOption[] AttackFamilies =
    [
        new("melee", "Melee")
    ];

    private static readonly AuthoringOption[] MeleeAttackStyles =
    [
        new("thrust", "Thrust"),
        new("slash", "Slash"),
        new("crush", "Crush")
    ];

    private static readonly AuthoringOption[] WeaponAnimationRefs =
    [
        new("melee_default", "Melee Default")
    ];

    public IReadOnlyList<AuthoringOption> LoadAttackFamilies() => AttackFamilies;

    public IReadOnlyList<AuthoringOption> LoadAttackStyles() => MeleeAttackStyles;

    public IReadOnlyList<AuthoringOption> LoadWeaponAnimationRefs() => WeaponAnimationRefs;

    public IReadOnlySet<string> SupportedAttackFamilies { get; } =
        AttackFamilies.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> SupportedAttackStyles { get; } =
        MeleeAttackStyles.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> DefaultToolCapabilityIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "farming",
            "fishing",
            "mining",
            "woodcutting"
        };

    public bool IsKnownToolCapability(string capabilityId, IReadOnlySet<string> databaseCapabilities) =>
        databaseCapabilities.Contains(capabilityId) || DefaultToolCapabilityIds.Contains(capabilityId);
}

public static class HandEquipmentDomainRules
{
    public const int MaximumRequirements = 16;
    public const int MaximumModifiers = 16;
    public const int MaximumToolCapabilities = 16;
    public const int MaximumMagnitude = 1_000_000;
    public const int MaximumAttackSpeedUnits = 60;
    public const int MaximumRangeTiles = 32;
    public const int MaximumPowerTier = 1_000;

    public static IReadOnlyList<HandEquipmentToolCapabilityDraft> NormalizeToolCapabilities(
        IReadOnlyList<HandEquipmentToolCapabilityDraft>? capabilities) =>
        (capabilities ?? [])
            .Select(value => new HandEquipmentToolCapabilityDraft(
                NormalizeRequired(value.CapabilityId),
                value.PowerTier,
                NormalizeOptional(value.ActionAnimationId),
                NormalizeOptional(value.EffectResourceId)))
            .Where(value => value.CapabilityId.Length > 0
                || value.ActionAnimationId is not null
                || value.EffectResourceId is not null
                || value.PowerTier != 0)
            .OrderBy(value => value.CapabilityId, StringComparer.Ordinal)
            .ToArray();

    public static bool HasDuplicateToolCapabilities(
        IReadOnlyList<HandEquipmentToolCapabilityDraft> capabilities) =>
        capabilities
            .GroupBy(value => value.CapabilityId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    public static string Classify(
        bool hasConsumableProfile,
        string? equipmentSlotId,
        EquipmentCombatProfileDefinition? weaponProfile,
        IReadOnlyList<HandEquipmentToolCapabilityDefinition> toolCapabilities)
    {
        if (hasConsumableProfile)
        {
            return "Consumable";
        }

        var hasWeapon = weaponProfile is not null;
        var hasTools = toolCapabilities.Count > 0;
        return (hasWeapon, hasTools) switch
        {
            (true, true) => "Weapon + Tool",
            (true, false) => "Weapon",
            (false, true) => "Tool",
            _ => equipmentSlotId is not null ? "Equipment" : "Basic"
        };
    }

    public static bool IsActiveRuntimeWeapon(
        string? equipmentSlotId,
        EquipmentCombatProfileDefinition? weaponProfile) =>
        equipmentSlotId == HandEquipmentAuthoringRegistry.ActiveWeaponSlotId
        && weaponProfile is not null;

    public static string NormalizeRequired(string? value) =>
        (value ?? string.Empty).Trim();

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
