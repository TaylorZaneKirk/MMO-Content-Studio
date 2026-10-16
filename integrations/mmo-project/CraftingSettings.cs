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
    [JsonPropertyName("arrows")] public ArrowCraftingSettings? Arrows { get; init; }
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
        if (Arrows is not null) errors.AddRange(Arrows.Validate(items, ItemIds));
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items)
    {
        var errors = new List<string>();
        foreach (var id in new[] { SawItemId, HammerItemId, LogItemId, PlankItemId, ChairItemId })
            if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked) errors.Add($"'{id}' must be enabled/nonstackable.");
        if (string.IsNullOrWhiteSpace(NailsItemId) || !items.TryGetValue(NailsItemId, out var nails) || !nails) errors.Add("Nails must be enabled/stackable.");
        if (Arrows is not null)
        {
            foreach (var id in Arrows.ItemIds)
                if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked != (id != Arrows.KnifeItemId))
                    errors.Add($"Arrow item '{id}' has an unavailable or incorrect stack shape.");
        }
        return errors;
    }
}

// Four inventory recipes share the existing Crafting lifecycle, not furniture geometry.
public sealed record ArrowCraftingSettings(
    [property: JsonPropertyName("knife_item_id")] string KnifeItemId,
    [property: JsonPropertyName("shaft_item_id")] string ShaftItemId,
    [property: JsonPropertyName("headless_item_id")] string HeadlessItemId,
    [property: JsonPropertyName("feather_item_id")] string FeatherItemId,
    [property: JsonPropertyName("bronze_head_item_id")] string BronzeHeadItemId,
    [property: JsonPropertyName("iron_head_item_id")] string IronHeadItemId,
    [property: JsonPropertyName("bronze_arrow_item_id")] string BronzeArrowItemId,
    [property: JsonPropertyName("iron_arrow_item_id")] string IronArrowItemId,
    [property: JsonPropertyName("shafts")] ArrowCraftingStep Shafts,
    [property: JsonPropertyName("headless")] ArrowCraftingStep Headless,
    [property: JsonPropertyName("bronze")] ArrowCraftingStep Bronze,
    [property: JsonPropertyName("iron")] ArrowCraftingStep Iron)
{
    [JsonIgnore] public IReadOnlyList<string> ItemIds => [KnifeItemId, ShaftItemId, HeadlessItemId, FeatherItemId, BronzeHeadItemId, IronHeadItemId, BronzeArrowItemId, IronArrowItemId];
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items, IReadOnlyList<string> furnitureItems)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || (items is not null && !items.Contains(id)))
                errors.Add($"Missing or unavailable arrow item '{id}'.");
        if (ItemIds.Distinct().Count() != ItemIds.Count || ItemIds.Intersect(furnitureItems).Any())
            errors.Add("Arrow bindings must be distinct from each other and existing Crafting bindings.");
        foreach (var step in new[] { Shafts, Headless, Bronze, Iron })
            if (step is null || step.Level is < 1 or > 99 || step.XpTenths is < 0 or > 10000000 || step.DurationMs is < 1 or > 86400000)
                errors.Add("Arrow steps require level 1–99, XP 0–10000000 tenths and duration 1–86400000 ms.");
        return errors;
    }
}
public sealed record ArrowCraftingStep(
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs);
