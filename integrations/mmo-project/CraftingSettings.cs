using System.Text.Json.Serialization;
namespace MMO.Project.Crafting;

// Published production facts. One furniture entry owns its recipe and placement
// facts; adding furniture does not add another runtime production lifecycle.
public sealed record CraftingSettings(
    [property: JsonPropertyName("saw_item_id")] string SawItemId,
    [property: JsonPropertyName("hammer_item_id")] string HammerItemId,
    [property: JsonPropertyName("log_item_id")] string LogItemId,
    [property: JsonPropertyName("plank_item_id")] string PlankItemId,
    [property: JsonPropertyName("nails_item_id")] string NailsItemId,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("saw_level")] int SawLevel,
    [property: JsonPropertyName("saw_xp_tenths")] int SawXpTenths,
    [property: JsonPropertyName("saw_duration_ms")] int SawDurationMs,
    [property: JsonPropertyName("furniture")] IReadOnlyList<FurnitureRecipeSettings> Furniture)
{
    [JsonPropertyName("arrows")] public ArrowCraftingSettings? Arrows { get; init; }
    [JsonIgnore] public IReadOnlyList<string> ItemIds => [SawItemId, HammerItemId, LogItemId, PlankItemId, NailsItemId,
        .. (Furniture ?? []).Select(f => f?.ItemId ?? "")];
    public FurnitureRecipeSettings? FindFurniture(string itemId) => Furniture?.FirstOrDefault(f => f?.ItemId == itemId);

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unavailable Crafting item '{id}'.");
        if (ItemIds.Distinct().Count() != ItemIds.Count) errors.Add("Item bindings must be distinct.");
        if (StationDefinitionId != "home_workbench") errors.Add("Furniture station must be home_workbench.");
        if (SawLevel is < 1 or > 99 || SawXpTenths is < 0 or > 10000000 || SawDurationMs is < 1 or > 86400000)
            errors.Add("Saw requires level 1–99, XP 0–10000000 tenths and duration 1–86400000 ms.");
        if (Furniture is null || Furniture.Count == 0) errors.Add("At least one furniture recipe is required.");
        var recipeIds = new HashSet<string> { "saw_plank", "cut_arrow_shafts", "make_headless_arrows", "make_bronze_arrows", "make_iron_arrows" };
        foreach (var f in Furniture ?? [])
        {
            if (f is null) { errors.Add("Furniture entries must be objects."); continue; }
            if (string.IsNullOrWhiteSpace(f.RecipeId) || !recipeIds.Add(f.RecipeId)) errors.Add("Furniture recipe IDs must be present and unique.");
            if (string.IsNullOrWhiteSpace(f.DisplayName)) errors.Add("Furniture display name is required.");
            if (f.Level is < 1 or > 99 || f.PlacementLevel is < 1 or > 99) errors.Add("Furniture levels must be 1–99.");
            if (f.DurationMs is < 1 or > 86400000 || f.XpTenths is < 0 or > 10000000) errors.Add("Invalid furniture duration or XP.");
            if (f.Planks is < 1 or > 10000 || f.Nails is < 1 or > 10000) errors.Add("Furniture ingredients must be 1–10000.");
            if (f.FootprintWidthTiles != 1 || f.FootprintHeightTiles != 1 || !f.OccupiesFurnitureSpace)
                errors.Add("Furniture requires a 1×1 occupied footprint.");
            foreach (var path in new[] { f.EastTexturePath, f.WestTexturePath })
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.Ordinal) || path.Contains(".."))
                    errors.Add("Furniture directions require project PNG asset paths.");
        }
        if (Arrows is not null) errors.AddRange(Arrows.Validate(items, ItemIds));
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds.Where(id => id != NailsItemId))
            if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked) errors.Add($"'{id}' must be enabled/nonstackable.");
        if (string.IsNullOrWhiteSpace(NailsItemId) || !items.TryGetValue(NailsItemId, out var nails) || !nails) errors.Add("Nails must be enabled/stackable.");
        if (Arrows is not null)
            foreach (var id in Arrows.ItemIds)
                if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked != (id != Arrows.KnifeItemId))
                    errors.Add($"Arrow item '{id}' has an unavailable or incorrect stack shape.");
        return errors;
    }
}

public sealed record FurnitureRecipeSettings(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("planks")] int Planks,
    [property: JsonPropertyName("nails")] int Nails,
    [property: JsonPropertyName("placement_level")] int PlacementLevel,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("occupies_furniture_space")] bool OccupiesFurnitureSpace,
    [property: JsonPropertyName("blocks_movement")] bool BlocksMovement,
    [property: JsonPropertyName("east_texture_path")] string EastTexturePath,
    [property: JsonPropertyName("west_texture_path")] string WestTexturePath)
{
    // Retained placements keep their exact item, geometry and art when recipes change.
    public bool SamePlacementFacts(FurnitureRecipeSettings other) => ItemId == other.ItemId &&
        FootprintWidthTiles == other.FootprintWidthTiles && FootprintHeightTiles == other.FootprintHeightTiles &&
        OccupiesFurnitureSpace == other.OccupiesFurnitureSpace && BlocksMovement == other.BlocksMovement &&
        EastTexturePath == other.EastTexturePath && WestTexturePath == other.WestTexturePath;
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
