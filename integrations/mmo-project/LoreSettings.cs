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
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        if (GooItemId != "slime_goo" || items is not null && !items.Contains(GooItemId)) errors.Add("V1 requires enabled slime_goo.");
        if (MobDefinitionId != "slime") errors.Add("V1 mastery belongs to the slime definition.");
        if (StudyLevel is < 1 or > 99 || MasteryLevel < StudyLevel || MasteryLevel > 99) errors.Add("Levels must be 1–99; mastery cannot precede study.");
        if (StudyDurationMs is < 600 or > 60000) errors.Add("Study duration must be 600–60000 milliseconds.");
        if (StudyXp is < 1 or > 100000 || MasteryStudies is < 1 or > 1000000) errors.Add("Positive bounded XP and study count are required.");
        if (AccuracyBasisPoints is < 1 or > 100) errors.Add("V1 accuracy bonus must be 1–100 basis points (at most 1%).");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items) =>
        items.TryGetValue(GooItemId ?? "", out var stackable) && !stackable ? [] : ["Goo must be enabled and non-stackable."];
}
