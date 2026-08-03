using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class MobAuthoringRegistry
{
    public const int CombatUnitMilliseconds = 600;
    public const int MinAttackSpeedUnits = 1;
    public const int MaxAttackSpeedUnits = 60;
    public const int MaxMobLevel = 1_000_000;
    public const int MaxDropOrder = 255;
    public const int MaxStackCount = 1_000_000;
    public const int MaxRangeTiles = 32;
    public const int MaxCombatBonusMagnitude = 1_000_000;
    public const double DefaultMovementSpeedTilesPerSecond = 1.25;
    public const double DefaultVisualRenderScale = 0.25;

    private static readonly AuthoringOption[] PublicationStates =
    [
        new("Draft", "Draft"),
        new("Published", "Published"),
        new("Disabled", "Disabled")
    ];

    private static readonly AuthoringOption[] AttackTypes =
    [
        new("melee", "Melee")
    ];

    private static readonly AuthoringOption[] AccuracyStyles =
    [
        new("thrust", "Thrust"),
        new("slash", "Slash"),
        new("crush", "Crush")
    ];

    private static readonly AuthoringOption[] FactionDispositions =
    [
        new("hostile", "Hostile"),
        new("neutral", "Neutral")
    ];

    private static readonly AuthoringOption[] CombatBonusFields =
    [
        new("attack_thrust", "Attack Thrust"),
        new("attack_slash", "Attack Slash"),
        new("attack_crush", "Attack Crush"),
        new("attack_ranged", "Attack Ranged"),
        new("attack_magic", "Attack Magic"),
        new("strength_melee", "Strength Melee"),
        new("strength_ranged", "Strength Ranged"),
        new("strength_magic", "Strength Magic"),
        new("defence_thrust", "Defence Thrust"),
        new("defence_slash", "Defence Slash"),
        new("defence_crush", "Defence Crush"),
        new("defence_ranged", "Defence Ranged"),
        new("defence_magic", "Defence Magic")
    ];

    public MobAuthoringDefaults Defaults { get; } = new(
        "melee",
        "crush",
        1,
        1,
        4,
        CombatUnitMilliseconds,
        DefaultMovementSpeedTilesPerSecond,
        1,
        1,
        DefaultVisualRenderScale,
        false,
        0,
        0,
        0);

    public IReadOnlyList<AuthoringOption> LoadPublicationStates() => PublicationStates;

    public IReadOnlyList<AuthoringOption> LoadAttackTypes() => AttackTypes;

    public IReadOnlyList<AuthoringOption> LoadAccuracyStyles() => AccuracyStyles;

    public IReadOnlyList<AuthoringOption> LoadFactionDispositions() => FactionDispositions;

    public IReadOnlyList<AuthoringOption> LoadCombatBonusFields() => CombatBonusFields;

    public MobSupportedLimits LoadSupportedLimits() => new(
        MaxMobLevel,
        MaxDropOrder,
        MaxStackCount,
        MaxRangeTiles,
        MaxCombatBonusMagnitude,
        MinAttackSpeedUnits,
        MaxAttackSpeedUnits);
}
