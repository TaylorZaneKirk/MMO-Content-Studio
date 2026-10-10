using System.Text.Json.Serialization;
namespace MMO.Project.Lore;

// The authored study document. Creature families use combat milestones; Flowers uses five harvest milestones. Specimens carry
// independent eligibility, XP and point weights. Focus rules share this document.
public sealed record LoreSettings
{
    [JsonPropertyName("study_duration_ms")] public int StudyDurationMs { get; init; } = 1800;
    [JsonPropertyName("families")] public LoreFamily[] Families { get; init; } = [];
    [JsonPropertyName("specimens")] public LoreSpecimen[] Specimens { get; init; } = [];
    [JsonPropertyName("lectern_definition_id")] public string LecternDefinitionId { get; init; } = "study_lectern";
    [JsonPropertyName("station_xp_percent")] public int StationXpPercent { get; init; } = 125;
    [JsonPropertyName("station_auto_ms")] public int StationAutoMs { get; init; } = 1800;
    [JsonPropertyName("station_manual_ms")] public int StationManualMs { get; init; } = 600;
    [JsonPropertyName("focus_drain_ms")] public int FocusDrainMs { get; init; } = 12000;
    [JsonPropertyName("focus_reduction_percent")] public int FocusReductionPercent { get; init; } = 5;
    [JsonPropertyName("fishing_focus_level")] public int FishingFocusLevel { get; init; } = 5;
    [JsonPropertyName("cooking_focus_level")] public int CookingFocusLevel { get; init; } = 8;
    [JsonPropertyName("mining_focus_level")] public int MiningFocusLevel { get; init; } = 11;
    [JsonPropertyName("blacksmithing_focus_level")] public int BlacksmithingFocusLevel { get; init; } = 14;
    [JsonPropertyName("woodcutting_focus_level")] public int WoodcuttingFocusLevel { get; init; } = 17;
    [JsonPropertyName("crafting_focus_level")] public int CraftingFocusLevel { get; init; } = 20;
    [JsonPropertyName("farming_focus_level")] public int FarmingFocusLevel { get; init; } = 23;
    [JsonPropertyName("alchemy_focus_level")] public int AlchemyFocusLevel { get; init; } = 26;
    [JsonPropertyName("guarded_mind_level")] public int GuardedMindLevel { get; init; } = 1;
    [JsonPropertyName("guarded_mind_defence_percent")] public int GuardedMindDefencePercent { get; init; } = 5;
    [JsonPropertyName("guarded_mind_minimum_defence")] public int GuardedMindMinimumDefence { get; init; } = 1;
    [JsonPropertyName("guarded_mind_drain_ms")] public int GuardedMindDrainMs { get; init; } = 36000;
    [JsonPropertyName("guarded_mind_ready_ms")] public int GuardedMindReadyMs { get; init; } = 600;
    [JsonPropertyName("measured_force_level")] public int MeasuredForceLevel { get; init; } = 4;
    [JsonPropertyName("measured_force_strength_percent")] public int MeasuredForceStrengthPercent { get; init; } = 5;
    [JsonPropertyName("measured_force_minimum_strength")] public int MeasuredForceMinimumStrength { get; init; } = 1;
    [JsonPropertyName("measured_force_drain_ms")] public int MeasuredForceDrainMs { get; init; } = 36000;
    [JsonPropertyName("measured_force_ready_ms")] public int MeasuredForceReadyMs { get; init; } = 600;
    [JsonPropertyName("steady_hand_level")] public int SteadyHandLevel { get; init; } = 7;
    [JsonPropertyName("steady_hand_attack_percent")] public int SteadyHandAttackPercent { get; init; } = 5;
    [JsonPropertyName("steady_hand_minimum_attack")] public int SteadyHandMinimumAttack { get; init; } = 1;
    [JsonPropertyName("steady_hand_drain_ms")] public int SteadyHandDrainMs { get; init; } = 36000;
    [JsonPropertyName("steady_hand_ready_ms")] public int SteadyHandReadyMs { get; init; } = 600;
    [JsonPropertyName("keen_aim_level")] public int KeenAimLevel { get; init; } = 8;
    [JsonPropertyName("keen_aim_ranged_percent")] public int KeenAimRangedPercent { get; init; } = 5;
    [JsonPropertyName("keen_aim_minimum_ranged")] public int KeenAimMinimumRanged { get; init; } = 1;
    [JsonPropertyName("keen_aim_drain_ms")] public int KeenAimDrainMs { get; init; } = 36000;
    [JsonPropertyName("keen_aim_ready_ms")] public int KeenAimReadyMs { get; init; } = 600;
    [JsonPropertyName("arcane_clarity_level")] public int ArcaneClarityLevel { get; init; } = 9;
    [JsonPropertyName("arcane_clarity_magic_accuracy_percent")] public int ArcaneClarityMagicAccuracyPercent { get; init; } = 5;
    [JsonPropertyName("arcane_clarity_minimum_magic_accuracy")] public int ArcaneClarityMinimumMagicAccuracy { get; init; } = 1;
    [JsonPropertyName("arcane_clarity_drain_ms")] public int ArcaneClarityDrainMs { get; init; } = 36000;
    [JsonPropertyName("arcane_clarity_ready_ms")] public int ArcaneClarityReadyMs { get; init; } = 600;
    // Both rates spend exact integer units per 100 ns gameplay tick. Publication
    // cannot change the denominator while any saved partial point remains.
    [JsonIgnore] public long ConcentrationUnitsPerPoint => checked((long)FocusDrainMs * GuardedMindDrainMs * TimeSpan.TicksPerMillisecond);
    [JsonIgnore] public IReadOnlyList<InsightFocus> Focuses => [
        new("guarded_mind", GuardedMindLevel), new("measured_force", MeasuredForceLevel),
        new("steady_hand", SteadyHandLevel), new("keen_aim", KeenAimLevel), new("arcane_clarity", ArcaneClarityLevel),
        new("fishing", FishingFocusLevel), new("cooking", CookingFocusLevel),
        new("mining", MiningFocusLevel), new("blacksmithing", BlacksmithingFocusLevel),
        new("woodcutting", WoodcuttingFocusLevel), new("crafting", CraftingFocusLevel),
        new("farming", FarmingFocusLevel), new("alchemy", AlchemyFocusLevel)];

    public LoreSpecimen? FindSpecimen(string itemId) => Specimens?.FirstOrDefault(s => s is not null && s.ItemId == itemId);
    public LoreFamily? FindFamily(string familyId) => Families.FirstOrDefault(f => f.FamilyId == familyId);
    public LoreFamily? FamilyForMob(string definitionId) => Families.FirstOrDefault(f => f.MobDefinitionIds.Contains(definitionId, StringComparer.Ordinal));

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        if (Families is null || Families.Length == 0 || Families.Any(f => f is null))
            return ["Explicit subject families are required."];
        if (Families.Select(f => f.FamilyId).Distinct(StringComparer.Ordinal).Count() != Families.Length)
            errors.Add("Each family ID may appear once.");
        foreach (var family in Families)
        {
            // Flower tuning is explicit and bounded; it has no creature members
            // or combat rewards. Do not infer future plant families from item names.
            if (family.IsFlowers)
            {
                if (family.DisplayName != "Flowers" || family.RequiredLevel is < 1 or > 99 ||
                    family.DefenceStyle != "none" || family.MobDefinitionIds is null || family.MobDefinitionIds.Length != 0 ||
                    family.MilestonePoints is not { Length: 5 } thresholds ||
                    thresholds.Any(value => value is < 1 or > 1000000000) ||
                    thresholds.Zip(thresholds.Skip(1)).Any(pair => pair.First >= pair.Second))
                    errors.Add("Flowers requires no Mob members, no defence, Insight 1–99 and five strictly increasing positive point thresholds.");
                continue;
            }
            if (family.MilestonePoints is not null) errors.Add("Only Flowers authors milestone point thresholds.");
            if (family.FamilyId is not ("slime" or "beasts") ||
                family.DisplayName != (family.FamilyId == "slime" ? "Slime" : "Beasts") ||
                family.RequiredLevel != (family.FamilyId == "slime" ? 3 : 5) || family.DefenceStyle != "melee" ||
                family.MobDefinitionIds is null || family.MobDefinitionIds.Length == 0 || family.MobDefinitionIds.Any(string.IsNullOrWhiteSpace))
                errors.Add("Slime (Insight 3) and Beasts (Insight 5) require explicit members and melee defence.");
        }
        var members = Families.SelectMany(f => f.MobDefinitionIds ?? []).ToArray();
        if (members.Distinct(StringComparer.Ordinal).Count() != members.Length)
            errors.Add("Each Mob definition may belong to only one family.");
        if (FindFamily("slime") is not { } slime || slime.MobDefinitionIds is null ||
            !slime.MobDefinitionIds.Contains("slime") || !slime.MobDefinitionIds.Contains("cellar_slime"))
            errors.Add("Preserve both existing Slime members.");
        if (Specimens is null || Specimens.Length == 0 || Specimens.Any(s => s is null))
            errors.Add("At least one complete specimen is required.");
        else
        {
            if (Specimens.Select(s => s.ItemId).Distinct(StringComparer.Ordinal).Count() != Specimens.Length)
                errors.Add("Each specimen item may appear once.");
            foreach (var specimen in Specimens)
                if (string.IsNullOrWhiteSpace(specimen.ItemId) || FindFamily(specimen.FamilyId) is null ||
                    specimen.RequiredLevel is < 1 or > 99 || specimen.BaseXp is < 1 or > 100000 ||
                    specimen.MasteryPoints is < 1 or > 1000000 || items is not null && !items.Contains(specimen.ItemId))
                    errors.Add("Specimens require an enabled item, an authored family, level 1–99 and bounded positive XP/points.");
            if (Specimens.Any(higher => Specimens.Any(lower => higher.FamilyId == lower.FamilyId && higher.RequiredLevel > lower.RequiredLevel &&
                (higher.BaseXp <= lower.BaseXp || higher.MasteryPoints <= lower.MasteryPoints))))
                errors.Add("Higher-level specimens must award more XP and mastery points than lower-level specimens.");
            if (FindSpecimen("slime_goo") is not { RequiredLevel: 1, BaseXp: 5, MasteryPoints: 1 })
                errors.Add("Basic Goo remains level 1, 5 XP and 1 mastery point.");
            if (FindSpecimen("slime_goo")?.FamilyId != "slime") errors.Add("Goo remains a Slime specimen.");
            if (FindFamily("beasts") is not null && FindSpecimen("rat_tail") is not
                { FamilyId: "beasts", RequiredLevel: 5, BaseXp: 10, MasteryPoints: 2 })
                errors.Add("Rat Tail remains level 5, 10 XP and 2 Beast mastery points.");
        }
        if (FindFamily("flowers") is not null && (FindSpecimen("inventory_484_flowers") is not
                { FamilyId: "flowers", RequiredLevel: 1, BaseXp: 10, MasteryPoints: 1 } ||
                (Specimens ?? []).Any(s => s is not null && s.FamilyId == "flowers" && s.ItemId != "inventory_484_flowers")))
            errors.Add("Flowers studies only inventory_484_flowers at Insight 1 for 10 XP and 1 point.");
        if (StudyDurationMs != 1800) errors.Add("Inventory study remains 1800 milliseconds.");
        if (LecternDefinitionId != "study_lectern") errors.Add("Insight requires study_lectern.");
        if (StationXpPercent != 125 || StationAutoMs != 1800 || StationManualMs != 600)
            errors.Add("Lectern study remains 125% XP, 1800 ms automatic and 600 ms manual.");
        if (FocusDrainMs is < 1000 or > 60000 || FocusReductionPercent is < 1 or > 20 || Focuses.Any(f => f.Level is < 1 or > 99))
            errors.Add("Focus drain must be 1000–60000 ms, reduction 1–20 percent and levels 1–99.");
        if (GuardedMindLevel != 1 || GuardedMindDefencePercent != 5 || GuardedMindMinimumDefence != 1 ||
            GuardedMindDrainMs != 36000 || GuardedMindReadyMs != 600)
            errors.Add("Guarded Mind remains Insight 1, +5% Defence (minimum +1), 36000 ms drain and 600 ms readiness.");
        if (MeasuredForceLevel != 4 || MeasuredForceStrengthPercent != 5 || MeasuredForceMinimumStrength != 1 ||
            MeasuredForceDrainMs != GuardedMindDrainMs || MeasuredForceReadyMs != GuardedMindReadyMs)
            errors.Add("Measured Force remains Insight 4, +5% melee Strength (minimum +1), 36000 ms drain and 600 ms readiness.");
        if (SteadyHandLevel != 7 || SteadyHandAttackPercent != 5 || SteadyHandMinimumAttack != 1 ||
            SteadyHandDrainMs != GuardedMindDrainMs || SteadyHandReadyMs != GuardedMindReadyMs)
            errors.Add("Steady Hand remains Insight 7, +5% effective attack (minimum +1), 36000 ms drain and 600 ms readiness.");
        if (KeenAimLevel != 8 || KeenAimRangedPercent != 5 || KeenAimMinimumRanged != 1 ||
            KeenAimDrainMs != GuardedMindDrainMs || KeenAimReadyMs != GuardedMindReadyMs)
            errors.Add("Keen Aim remains Insight 8, +5% effective ranged (minimum +1), 36000 ms drain and 600 ms readiness.");
        if (ArcaneClarityLevel != 9 || ArcaneClarityMagicAccuracyPercent != 5 || ArcaneClarityMinimumMagicAccuracy != 1 ||
            ArcaneClarityDrainMs != GuardedMindDrainMs || ArcaneClarityReadyMs != GuardedMindReadyMs)
            errors.Add("Arcane Clarity remains Insight 9, +5% effective magic accuracy (minimum +1), 36000 ms drain and 600 ms readiness.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items) =>
        Specimens is null ? ["Specimens are required."] : Specimens.Where(s => s is null || !items.TryGetValue(s.ItemId, out var stackable) || stackable)
            .Select(_ => "Study specimens must be enabled and non-stackable.").ToArray();
}

public sealed record LoreFamily(
    [property: JsonPropertyName("family_id")] string FamilyId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("mob_definition_ids")] string[] MobDefinitionIds,
    [property: JsonPropertyName("defence_style")] string DefenceStyle,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("milestone_points"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long[]? MilestonePoints = null)
{
    [JsonIgnore] public bool IsFlowers => FamilyId == "flowers";
    [JsonIgnore] public int MilestoneCount => IsFlowers ? 5 : FamilyMastery.MaximumMilestones;
    public long Threshold(int milestone) => IsFlowers ? MilestonePoints![milestone - 1] : FamilyMastery.Threshold(milestone);
    public int EligibleMilestones(long points, int level)
    {
        if (level < RequiredLevel) return 0;
        var earned = 0;
        while (earned < MilestoneCount && points >= Threshold(earned + 1)) earned++;
        return earned;
    }
}
public sealed record LoreSpecimen(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("family_id")] string FamilyId,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("base_xp")] int BaseXp,
    [property: JsonPropertyName("mastery_points")] int MasteryPoints);
public sealed record InsightFocus(string SkillId, int Level);

// Shared deterministic arithmetic for runtime rewards and Studio's milestone
// preview. These approved constants are not a generic reward scripting system.
public static class FamilyMastery
{
    public const int MaximumMilestones = 140;
    public static long Threshold(int milestone) => checked(20L * milestone + (long)milestone * (milestone - 1) / 2);
    public static int EligibleMilestones(long points, int baseLevel, int requiredLevel)
    {
        if (baseLevel < requiredLevel) return 0;
        var earned = 0;
        while (earned < MaximumMilestones && points >= Threshold(earned + 1)) earned++;
        return earned;
    }
    // Category order: melee/ranged/magic accuracy, melee defence, then the three
    // strengths. Legacy accuracy is a floor, never an extra milestone or bonus.
    public static int Bonus(int milestones, int category, int legacyAccuracy = 0)
    {
        var earned = Math.Clamp((milestones + 6 - category) / 7, 0, 20) * 50;
        return Math.Max(earned, category < 3 ? legacyAccuracy : 0);
    }
}
