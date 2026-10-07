using System.Text.Json.Serialization;
namespace MMO.Project.Crafting;

// Published V1 recipe and decorative furniture facts. Player owns all live state.
public sealed record CraftingSettings(
    [property: JsonPropertyName("saw_item_id")] string SawItemId,
    [property: JsonPropertyName("hammer_item_id")] string HammerItemId,
    [property: JsonPropertyName("log_item_id")] string LogItemId,
    [property: JsonPropertyName("plank_item_id")] string PlankItemId,
    [property: JsonPropertyName("nails_item_id")] string NailsItemId,
    [property: JsonPropertyName("chair_item_id")] string ChairItemId,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("saw_level")] int SawLevel,
    [property: JsonPropertyName("saw_xp_tenths")] int SawXpTenths,
    [property: JsonPropertyName("saw_duration_ms")] int SawDurationMs,
    [property: JsonPropertyName("chair_level")] int ChairLevel,
    [property: JsonPropertyName("chair_xp_tenths")] int ChairXpTenths,
    [property: JsonPropertyName("chair_duration_ms")] int ChairDurationMs,
    [property: JsonPropertyName("chair_planks")] int ChairPlanks,
    [property: JsonPropertyName("chair_nails")] int ChairNails,
    [property: JsonPropertyName("placement_level")] int PlacementLevel,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("occupies_furniture_space")] bool OccupiesFurnitureSpace,
    [property: JsonPropertyName("blocks_movement")] bool BlocksMovement,
    [property: JsonPropertyName("east_texture_path")] string EastTexturePath,
    [property: JsonPropertyName("west_texture_path")] string WestTexturePath)
{
    [JsonIgnore] public IReadOnlyList<string> ItemIds => [SawItemId, HammerItemId, LogItemId, PlankItemId, NailsItemId, ChairItemId];
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unavailable Crafting item '{id}'.");
        if (ItemIds.Distinct().Count() != ItemIds.Count) errors.Add("Item bindings must be distinct.");
        if (StationDefinitionId != "home_workbench") errors.Add("V1 station must be home_workbench.");
        foreach (var level in new[] { SawLevel, ChairLevel, PlacementLevel }) if (level is < 1 or > 99) errors.Add("Levels must be 1–99.");
        foreach (var time in new[] { SawDurationMs, ChairDurationMs }) if (time is < 1 or > 86400000) errors.Add("Duration must be 1–86400000 ms.");
        foreach (var xp in new[] { SawXpTenths, ChairXpTenths }) if (xp is < 0 or > 10000000) errors.Add("XP must be 0–10000000 tenths.");
        if (ChairPlanks is < 1 or > 10000 || ChairNails is < 1 or > 10000) errors.Add("Ingredient quantities must be 1–10000.");
        if (FootprintWidthTiles != 1 || FootprintHeightTiles != 1 || !OccupiesFurnitureSpace || BlocksMovement)
            errors.Add("V1 chairs require a 1×1 occupied footprint and walkable movement. Larger/blocking furniture is not supported.");
        foreach (var path in new[] { EastTexturePath, WestTexturePath })
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.Ordinal) || path.Contains("..")) errors.Add("Directions require project PNG asset paths.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items)
    {
        var errors = new List<string>();
        foreach (var id in new[] { SawItemId, HammerItemId, LogItemId, PlankItemId, ChairItemId })
            if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked) errors.Add($"'{id}' must be enabled/nonstackable.");
        if (string.IsNullOrWhiteSpace(NailsItemId) || !items.TryGetValue(NailsItemId, out var nails) || !nails) errors.Add("Nails must be enabled/stackable.");
        return errors;
    }
}
