using System.Text.Json;
using System.Text.Json.Serialization;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class MobDomainRulesTests
{
    [Theory]
    [InlineData(" Green_Slime ", "green_slime", true)]
    [InlineData("green_slime_2", "green_slime_2", true)]
    [InlineData("green-slime", "green-slime", false)]
    [InlineData("green__slime", "green__slime", false)]
    [InlineData(" ", "", false)]
    public void NormalizeStableIdKeepsRuntimeSafeIdentifierShape(
        string value,
        string expected,
        bool supported)
    {
        Assert.Equal(expected, MobDomainRules.NormalizeStableId(value));
        Assert.Equal(supported, MobDomainRules.IsStableId(value));
    }

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData("Published", "Published")]
    [InlineData("disabled", "Disabled")]
    public void NormalizePublicationStatePreservesPersistedCasing(
        string value,
        string expected)
    {
        Assert.Equal(expected, MobDomainRules.NormalizePublicationState(value));
        Assert.True(MobDomainRules.IsSupportedPublicationState(value));
    }

    [Fact]
    public void RegistryExposesLockedT4AValues()
    {
        var registry = new MobAuthoringRegistry();

        Assert.Equal(600, MobAuthoringRegistry.CombatUnitMilliseconds);
        Assert.Equal(["Draft", "Published", "Disabled"], registry.LoadPublicationStates().Select(option => option.Id));
        Assert.Equal(["melee"], registry.LoadAttackTypes().Select(option => option.Id));
        Assert.Equal(["thrust", "slash", "crush"], registry.LoadAccuracyStyles().Select(option => option.Id));
        Assert.Equal(["hostile", "neutral"], registry.LoadFactionDispositions().Select(option => option.Id));
        Assert.Equal("melee", registry.Defaults.AttackType);
        Assert.Equal("crush", registry.Defaults.AccuracyStyle);
        Assert.Equal(4, registry.Defaults.AttackSpeedUnits);
        Assert.Equal(1.25, registry.Defaults.MovementSpeedTilesPerSecond);
        Assert.Equal(0.25, registry.Defaults.VisualRenderScale);
        Assert.False(registry.Defaults.CanProactivelyTargetHostileMobs);
    }

    [Theory]
    [InlineData(1, 1, 1, 5, 2)]
    [InlineData(3, 3, 2, 8, 4)]
    [InlineData(4, 3, 3, 10, 5)]
    [InlineData(99, 99, 99, 99, 113)]
    public void DerivedMobCombatLevelUsesApprovedIntegerFormula(
        int attack,
        int strength,
        int defence,
        int maxHealth,
        int expected)
    {
        Assert.Equal(expected, MobDomainRules.CalculateDerivedCombatLevel(
            attack,
            strength,
            defence,
            maxHealth));
    }

    [Fact]
    public void CombatLevelDiagnosticsUseApprovedInnateBonusLevelEquivalentFormula()
    {
        var diagnostics = MobDomainRules.CalculateCombatLevelDiagnostics(
            new MobCombatProfileDefinition("melee", "slash", 1, 1, 4, 10, 10, 10),
            new EquipmentCombatBonusDefinition(
                AttackThrust: 0,
                AttackSlash: 54,
                AttackCrush: 12,
                AttackRanged: 99,
                AttackMagic: 99,
                StrengthMelee: 64,
                StrengthRanged: 99,
                StrengthMagic: 99,
                DefenceThrust: 0,
                DefenceSlash: 64,
                DefenceCrush: 128,
                DefenceRanged: 99,
                DefenceMagic: 99));

        Assert.Equal("slash", diagnostics.SelectedAccuracyStyle);
        Assert.Equal(54, diagnostics.SelectedAttackBonus);
        Assert.Equal(26.03125d, diagnostics.EquivalentAttackLevel);
        Assert.Equal(29d, diagnostics.EquivalentStrengthLevel);
        Assert.Equal(10d, diagnostics.EquivalentDefenceThrustLevel);
        Assert.Equal(29d, diagnostics.EquivalentDefenceSlashLevel);
        Assert.Equal(48d, diagnostics.EquivalentDefenceCrushLevel);
    }

    [Fact]
    public void DerivedMobCombatLevelMatchesMirroredCrossRepositoryFixture()
    {
        var fixture = LoadCombatLevelFixture();

        Assert.Equal("combat_level_formula_v1", fixture.SchemaVersion);
        foreach (var example in fixture.MobExamples)
        {
            Assert.Equal(example.ExpectedCombatLevel, MobDomainRules.CalculateDerivedCombatLevel(
                example.AttackLevel,
                example.StrengthLevel,
                example.DefenceLevel,
                example.MaxHealth));
        }

        var diagnostic = Assert.Single(fixture.InnateBonusDiagnosticExamples);
        var actual = MobDomainRules.CalculateCombatLevelDiagnostics(
            new MobCombatProfileDefinition(
                "melee",
                diagnostic.AccuracyStyle,
                1,
                1,
                4,
                diagnostic.AttackLevel,
                diagnostic.StrengthLevel,
                diagnostic.DefenceLevel),
            new EquipmentCombatBonusDefinition(
                AttackThrust: 0,
                AttackSlash: diagnostic.AttackSlash,
                AttackCrush: 0,
                AttackRanged: 0,
                AttackMagic: 0,
                StrengthMelee: diagnostic.StrengthMelee,
                StrengthRanged: 0,
                StrengthMagic: 0,
                DefenceThrust: diagnostic.DefenceThrust,
                DefenceSlash: diagnostic.DefenceSlash,
                DefenceCrush: diagnostic.DefenceCrush,
                DefenceRanged: 0,
                DefenceMagic: 0));
        Assert.Equal(diagnostic.ExpectedSelectedAttackBonus, actual.SelectedAttackBonus);
        Assert.Equal(diagnostic.ExpectedEquivalentAttackLevel, actual.EquivalentAttackLevel);
        Assert.Equal(diagnostic.ExpectedEquivalentStrengthLevel, actual.EquivalentStrengthLevel);
        Assert.Equal(diagnostic.ExpectedEquivalentDefenceThrustLevel, actual.EquivalentDefenceThrustLevel);
        Assert.Equal(diagnostic.ExpectedEquivalentDefenceSlashLevel, actual.EquivalentDefenceSlashLevel);
        Assert.Equal(diagnostic.ExpectedEquivalentDefenceCrushLevel, actual.EquivalentDefenceCrushLevel);
    }

    [Theory]
    [InlineData("melee", true)]
    [InlineData("ranged", false)]
    public void AttackTypeRegistryIsNarrow(string attackType, bool supported)
    {
        Assert.Equal(supported, MobDomainRules.IsSupportedAttackType(attackType));
    }

    [Theory]
    [InlineData("thrust", true)]
    [InlineData("slash", true)]
    [InlineData("crush", true)]
    [InlineData(null, false)]
    [InlineData("magic", false)]
    public void AccuracyStyleRegistryIsNarrow(string? accuracyStyle, bool supported)
    {
        Assert.Equal(supported, MobDomainRules.IsSupportedAccuracyStyle(accuracyStyle));
    }

    [Theory]
    [InlineData(false, null, 0, 0, 0, true)]
    [InlineData(false, null, 4, 0, 0, true)]
    [InlineData(true, "mobs", 4, 600, 16, true)]
    [InlineData(true, null, 4, 600, 16, false)]
    [InlineData(true, "mobs", 0, 600, 16, false)]
    [InlineData(true, "mobs", 4, 0, 16, false)]
    [InlineData(true, "mobs", 4, 600, 0, false)]
    public void ProactiveTargetingRequiresFactionAndPositiveScanValues(
        bool proactive,
        string? factionId,
        int radius,
        int intervalMs,
        int candidateLimit,
        bool expected)
    {
        Assert.Equal(
            expected,
            MobDomainRules.IsProactiveTargetingConsistent(
                proactive,
                factionId,
                radius,
                intervalMs,
                candidateLimit));
    }

    [Fact]
    public void NormalizeGuaranteedDropsSortsAndCanonicalizesIds()
    {
        var drops = MobDomainRules.NormalizeGuaranteedDrops(
        [
            new(2, " Apple ", 1),
            new(1, "iron_ore", 4),
            new(1, "Coal", 2)
        ]);

        Assert.Equal([1, 1, 2], drops.Select(drop => drop.DropOrder));
        Assert.Equal(["coal", "iron_ore", "apple"], drops.Select(drop => drop.ItemId));
    }

    private static CombatLevelFixture LoadCombatLevelFixture()
    {
        var root = FindRepositoryRoot();
        var localPath = Path.Combine(root, "integrations", "mmo-project", "prototype", "shared", "combat", "combat_level_formula_v1.json");
        var canonicalPath = Path.Combine(root, "..", "..", "prototype", "shared", "combat", "combat_level_formula_v1.json");
        Assert.Equal(
            File.ReadAllText(canonicalPath),
            File.ReadAllText(localPath));
        return JsonSerializer.Deserialize<CombatLevelFixture>(File.ReadAllText(localPath)) ??
               throw new InvalidOperationException("Expected combat-level parity fixture.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MMO.ContentStudio.AuthoringHost.csproj")) ||
                Directory.Exists(Path.Combine(directory.FullName, "host")) &&
                Directory.Exists(Path.Combine(directory.FullName, "integrations")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Expected to find Content Studio repository root.");
    }

    private sealed record CombatLevelFixture(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("mob_examples")] IReadOnlyList<MobCombatLevelExample> MobExamples,
        [property: JsonPropertyName("innate_bonus_diagnostic_examples")] IReadOnlyList<InnateBonusDiagnosticExample> InnateBonusDiagnosticExamples);

    private sealed record MobCombatLevelExample(
        [property: JsonPropertyName("attack_level")] int AttackLevel,
        [property: JsonPropertyName("strength_level")] int StrengthLevel,
        [property: JsonPropertyName("defence_level")] int DefenceLevel,
        [property: JsonPropertyName("max_health")] int MaxHealth,
        [property: JsonPropertyName("expected_combat_level")] int ExpectedCombatLevel);

    private sealed record InnateBonusDiagnosticExample(
        [property: JsonPropertyName("attack_level")] int AttackLevel,
        [property: JsonPropertyName("strength_level")] int StrengthLevel,
        [property: JsonPropertyName("defence_level")] int DefenceLevel,
        [property: JsonPropertyName("accuracy_style")] string AccuracyStyle,
        [property: JsonPropertyName("attack_slash")] int AttackSlash,
        [property: JsonPropertyName("strength_melee")] int StrengthMelee,
        [property: JsonPropertyName("defence_thrust")] int DefenceThrust,
        [property: JsonPropertyName("defence_slash")] int DefenceSlash,
        [property: JsonPropertyName("defence_crush")] int DefenceCrush,
        [property: JsonPropertyName("expected_selected_attack_bonus")] int ExpectedSelectedAttackBonus,
        [property: JsonPropertyName("expected_equivalent_attack_level")] double ExpectedEquivalentAttackLevel,
        [property: JsonPropertyName("expected_equivalent_strength_level")] double ExpectedEquivalentStrengthLevel,
        [property: JsonPropertyName("expected_equivalent_defence_thrust_level")] double ExpectedEquivalentDefenceThrustLevel,
        [property: JsonPropertyName("expected_equivalent_defence_slash_level")] double ExpectedEquivalentDefenceSlashLevel,
        [property: JsonPropertyName("expected_equivalent_defence_crush_level")] double ExpectedEquivalentDefenceCrushLevel);

    [Fact]
    public void DuplicateDropChecksUsePersistedIdentity()
    {
        var drops = new[]
        {
            new MobDropDraft(0, "apple", 1),
            new MobDropDraft(0, "iron_ore", 1),
            new MobDropDraft(2, " Apple ", 1)
        };

        Assert.True(MobDomainRules.HasDuplicateDropOrders(drops));
        Assert.True(MobDomainRules.HasDuplicateDropItems(drops));
    }
}
