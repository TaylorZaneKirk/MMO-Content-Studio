using System.Text.Json.Serialization;
namespace MMO.Project.Lore;

// One authored subject and one reward in V1. No registry or future tier engine.
public sealed record LoreSettings(
    [property: JsonPropertyName("goo_item_id")] string GooItemId,
    [property: JsonPropertyName("mob_definition_id")] string MobDefinitionId,
    [property: JsonPropertyName("study_level")] int StudyLevel,
    [property: JsonPropertyName("study_duration_ms")] int StudyDurationMs,
    [property: JsonPropertyName("study_xp")] int StudyXp,
    [property: JsonPropertyName("mastery_studies")] int MasteryStudies,
    [property: JsonPropertyName("mastery_level")] int MasteryLevel,
    [property: JsonPropertyName("accuracy_basis_points")] int AccuracyBasisPoints)
{
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

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        if (GooItemId != "slime_goo" || items is not null && !items.Contains(GooItemId)) errors.Add("V1 requires enabled slime_goo.");
        if (MobDefinitionId != "slime") errors.Add("V1 mastery belongs to the slime definition.");
        if (StudyLevel is < 1 or > 99 || MasteryLevel < StudyLevel || MasteryLevel > 99) errors.Add("Levels must be 1–99; mastery cannot precede study.");
        if (StudyDurationMs is < 600 or > 60000) errors.Add("Study duration must be 600–60000 milliseconds.");
        if (StudyXp is < 1 or > 100000 || MasteryStudies is < 1 or > 1000000) errors.Add("Positive bounded XP and study count are required.");
        if (AccuracyBasisPoints is < 1 or > 100) errors.Add("V1 accuracy bonus must be 1–100 basis points (at most 1%).");
        if (LecternDefinitionId != "study_lectern") errors.Add("Insight foundation requires study_lectern.");
        if (StationXpPercent is < 100 or > 200 || StationAutoMs is < 600 or > 60000 || StationManualMs is < 600 or > 60000)
            errors.Add("Station XP must be 100–200 percent; durations 600–60000 ms.");
        if (FocusDrainMs is < 1000 or > 60000 || FocusReductionPercent is < 1 or > 20 || Focuses.Any(f => f.Level is < 1 or > 99))
            errors.Add("Focus drain must be 1000–60000 ms, reduction 1–20 percent and levels 1–99.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items) =>
        items.TryGetValue(GooItemId ?? "", out var stackable) && !stackable ? [] : ["Goo must be enabled and non-stackable."];
}

public sealed record InsightFocus(string SkillId, int Level);
