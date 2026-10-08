using System.Text.Json.Serialization;
namespace MMO.Project.Lore;

// The authored study document. One Slime family is supported; specimens carry
// independent eligibility, XP and point weights. Focus rules share this document.
public sealed record LoreSettings
{
    [JsonPropertyName("study_duration_ms")] public int StudyDurationMs { get; init; } = 1800;
    [JsonPropertyName("family")] public LoreFamily? Family { get; init; }
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
    [JsonIgnore] public IReadOnlyList<InsightFocus> Focuses => [
        new("fishing", FishingFocusLevel), new("cooking", CookingFocusLevel),
        new("mining", MiningFocusLevel), new("blacksmithing", BlacksmithingFocusLevel),
        new("woodcutting", WoodcuttingFocusLevel), new("crafting", CraftingFocusLevel),
        new("farming", FarmingFocusLevel), new("alchemy", AlchemyFocusLevel)];

    public LoreSpecimen? FindSpecimen(string itemId) => Specimens.FirstOrDefault(s => s.ItemId == itemId);
    public bool IncludesMob(string definitionId) => Family?.MobDefinitionIds.Contains(definitionId, StringComparer.Ordinal) == true;

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        if (Family is null || Family.FamilyId != "slime" || Family.DisplayName != "Slime" || Family.DefenceStyle != "melee" ||
            Family.MobDefinitionIds is null || Family.MobDefinitionIds.Length == 0 ||
            Family.MobDefinitionIds.Any(string.IsNullOrWhiteSpace) || !Family.MobDefinitionIds.Contains("slime", StringComparer.Ordinal) ||
            Family.MobDefinitionIds.Distinct(StringComparer.Ordinal).Count() != Family.MobDefinitionIds.Length)
            errors.Add("Slime requires an explicit unique member list and melee defence.");
        if (Specimens is null || Specimens.Length == 0 || Specimens.Any(s => s is null))
            errors.Add("At least one complete specimen is required.");
        else
        {
            if (Specimens.Select(s => s.ItemId).Distinct(StringComparer.Ordinal).Count() != Specimens.Length)
                errors.Add("Each specimen item may appear once.");
            foreach (var specimen in Specimens)
                if (string.IsNullOrWhiteSpace(specimen.ItemId) || specimen.FamilyId != "slime" ||
                    specimen.RequiredLevel is < 1 or > 99 || specimen.BaseXp is < 1 or > 100000 ||
                    specimen.MasteryPoints is < 1 or > 1000000 || items is not null && !items.Contains(specimen.ItemId))
                    errors.Add("Specimens require an enabled item, Slime family, level 1–99 and bounded positive XP/points.");
            if (Specimens.Any(higher => Specimens.Any(lower => higher.RequiredLevel > lower.RequiredLevel &&
                (higher.BaseXp <= lower.BaseXp || higher.MasteryPoints <= lower.MasteryPoints))))
                errors.Add("Higher-level specimens must award more XP and mastery points than lower-level specimens.");
            if (FindSpecimen("slime_goo") is not { RequiredLevel: 1, BaseXp: 5, MasteryPoints: 1 })
                errors.Add("Basic Goo remains level 1, 5 XP and 1 mastery point.");
        }
        if (StudyDurationMs != 1800) errors.Add("Inventory study remains 1800 milliseconds.");
        if (LecternDefinitionId != "study_lectern") errors.Add("Insight requires study_lectern.");
        if (StationXpPercent != 125 || StationAutoMs != 1800 || StationManualMs != 600)
            errors.Add("Lectern study remains 125% XP, 1800 ms automatic and 600 ms manual.");
        if (FocusDrainMs is < 1000 or > 60000 || FocusReductionPercent is < 1 or > 20 || Focuses.Any(f => f.Level is < 1 or > 99))
            errors.Add("Focus drain must be 1000–60000 ms, reduction 1–20 percent and levels 1–99.");
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
    [property: JsonPropertyName("defence_style")] string DefenceStyle);
public sealed record LoreSpecimen(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("family_id")] string FamilyId,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("base_xp")] int BaseXp,
    [property: JsonPropertyName("mastery_points")] int MasteryPoints);
public sealed record InsightFocus(string SkillId, int Level);

// Shared deterministic arithmetic for runtime rewards and Studio's milestone
// preview. These approved constants are not a generic reward scripting system.
public static class SlimeMastery
{
    public const int RequiredLevel = 3;
    public const int MaximumMilestones = 140;
    public static long Threshold(int milestone) => checked(20L * milestone + (long)milestone * (milestone - 1) / 2);
    public static int EligibleMilestones(long points, int baseLevel)
    {
        if (baseLevel < RequiredLevel) return 0;
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
