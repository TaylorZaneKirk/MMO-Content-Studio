using System.Text.Json.Serialization;
namespace MMO.Project.Crafting;

// Published production facts. One furniture entry owns its recipe and placement
// facts; adding furniture does not add another runtime production lifecycle.
public sealed record CraftingSettings(
    [property: JsonPropertyName("saw_item_id")] string SawItemId,
    [property: JsonPropertyName("hammer_item_id")] string HammerItemId,
    [property: JsonPropertyName("log_item_id")] string LogItemId,
    [property: JsonPropertyName("nails_item_id")] string NailsItemId,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("sawing")] IReadOnlyList<SawingRecipeSettings> Sawing,
    [property: JsonPropertyName("furniture")] IReadOnlyList<FurnitureRecipeSettings> Furniture)
{
    [JsonPropertyName("arrows")] public ArrowCraftingSettings? Arrows { get; init; }
    [JsonPropertyName("spinning")] public SpinningCraftingSettings? Spinning { get; init; }
    [JsonIgnore] public IReadOnlyList<string> BindingIds => [SawItemId, HammerItemId, LogItemId, NailsItemId,
        .. (Sawing ?? []).Where(s => s is not null).SelectMany(s => new[] { s.LogItemId, s.PlankItemId }).Where(id => id != LogItemId),
        .. (Furniture ?? []).Select(f => f?.ItemId ?? ""), .. (Spinning is { } spin ? new[] { spin.WoolItemId, spin.YarnItemId } : Array.Empty<string>())];
    [JsonIgnore] public IReadOnlyList<string> ItemIds => BindingIds.Concat((Furniture ?? [])
        .Where(f => f is not null).SelectMany(f => (f.Inputs ?? []).Where(i => i is not null).Select(i => i.ItemId).Concat(f.RequiredToolIds ?? []))).Distinct().ToArray();
    public FurnitureRecipeSettings? FindFurniture(string itemId) => Furniture?.FirstOrDefault(f => f?.ItemId == itemId);

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unavailable Crafting item '{id}'.");
        if (BindingIds.Distinct().Count() != BindingIds.Count) errors.Add("Item bindings must be distinct.");
        if (StationDefinitionId != "home_workbench") errors.Add("Furniture station must be home_workbench.");
        if (Furniture is null || Furniture.Count == 0) errors.Add("At least one furniture recipe is required.");
        var recipeIds = new HashSet<string> { "cut_arrow_shafts", "make_headless_arrows", "make_bronze_arrows", "make_iron_arrows", "make_steel_arrows", "spin_wool" };
        if (Sawing is null || Sawing.Count == 0) errors.Add("At least one sawing recipe is required.");
        var logs = new HashSet<string>();
        foreach (var saw in Sawing ?? [])
        {
            if (saw is null) { errors.Add("Sawing entries must be objects."); continue; }
            if (string.IsNullOrWhiteSpace(saw.RecipeId) || !recipeIds.Add(saw.RecipeId)) errors.Add("Sawing recipe IDs must be present and unique.");
            if (string.IsNullOrWhiteSpace(saw.DisplayName)) errors.Add("Sawing display name is required.");
            if (!logs.Add(saw.LogItemId) || saw.LogItemId == saw.PlankItemId || saw.PlankItemId == LogItemId)
                errors.Add("Sawing needs unique logs and distinct plank outputs.");
            if (saw.Level is < 1 or > 99 || saw.XpTenths is < 0 or > 10000000 || saw.DurationMs is < 1 or > 86400000)
                errors.Add("Sawing requires level 1–99, XP 0–10000000 tenths and duration 1–86400000 ms.");
        }
        foreach (var f in Furniture ?? [])
        {
            if (f is null) { errors.Add("Furniture entries must be objects."); continue; }
            if (string.IsNullOrWhiteSpace(f.RecipeId) || !recipeIds.Add(f.RecipeId)) errors.Add("Furniture recipe IDs must be present and unique.");
            if (string.IsNullOrWhiteSpace(f.DisplayName)) errors.Add("Furniture display name is required.");
            if (f.Level is < 1 or > 99 || f.PlacementLevel is < 1 or > 99) errors.Add("Furniture levels must be 1–99.");
            if (f.DurationMs is < 1 or > 86400000 || f.XpTenths is < 0 or > 10000000) errors.Add("Invalid furniture duration or XP.");
            if (f.Inputs is null || f.Inputs.Count is < 1 or > 8 || f.Inputs.Any(i => i is null || string.IsNullOrWhiteSpace(i.ItemId) || i.Quantity is < 1 or > 10000 || i.ItemId == f.ItemId) ||
                f.Inputs.Where(i => i is not null).Select(i => i.ItemId).Distinct().Count() != f.Inputs.Count)
                errors.Add("Furniture needs distinct material inputs with quantities 1–10000.");
            if (f.RequiredToolIds is null || f.RequiredToolIds.Count > 4 || f.RequiredToolIds.Any(string.IsNullOrWhiteSpace) ||
                f.RequiredToolIds.Distinct().Count() != f.RequiredToolIds.Count || f.RequiredToolIds.Contains(f.ItemId) ||
                (f.Inputs ?? []).Any(i => i is not null && f.RequiredToolIds.Contains(i.ItemId)))
                errors.Add("Furniture tools must be distinct from consumed materials and output.");
            if (!f.OccupiesFurnitureSpace || (f.PlacementLayer == "floor"
                ? f.FootprintWidthTiles != 2 || f.FootprintHeightTiles != 1 || f.BlocksMovement
                : f.PlacementLayer != "furniture" || f.FootprintWidthTiles != 1 || f.FootprintHeightTiles != 1))
                errors.Add("Use 1×1 furniture or a nonblocking 2×1 floor runner.");
            foreach (var path in f.PlacementLayer == "floor" ? new[] { f.EastTexturePath, f.SouthTexturePath } : new[] { f.EastTexturePath, f.WestTexturePath })
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.Ordinal) || path.Contains(".."))
                    errors.Add("Furniture directions require project PNG asset paths.");
        }
        if (Arrows is not null) errors.AddRange(Arrows.Validate(items, ItemIds));
        if (Spinning is { } spin && (spin.WoolItemId != "raw_wool" || spin.StationDefinitionId != "spinning_wheel" ||
            spin.Level is < 1 or > 99 || spin.XpTenths is < 0 or > 10000000 || spin.DurationMs is < 1 or > 86400000))
            errors.Add("Spinning requires spinning_wheel, level 1–99 and valid XP/duration.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> items)
    {
        var errors = new List<string>();
        foreach (var id in BindingIds.Where(id => id != NailsItemId).Concat((Furniture ?? []).Where(f => f is not null).SelectMany(f => f.RequiredToolIds ?? [])).Distinct())
            if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked) errors.Add($"'{id}' must be enabled/nonstackable.");
        if (string.IsNullOrWhiteSpace(NailsItemId) || !items.TryGetValue(NailsItemId, out var nails) || !nails) errors.Add("Nails must be enabled/stackable.");
        if (Arrows is not null)
            foreach (var id in Arrows.ItemIds)
                if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var stacked) || stacked != (id != Arrows.KnifeItemId))
                    errors.Add($"Arrow item '{id}' has an unavailable or incorrect stack shape.");
        return errors;
    }
}

// One log becomes one plank; the shared saw is retained. LogItemId above remains
// the ordinary-log binding for arrow shafts, not a second sawing recipe owner.
public sealed record SawingRecipeSettings(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("log_item_id")] string LogItemId,
    [property: JsonPropertyName("plank_item_id")] string PlankItemId,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs);

public sealed record FurnitureRecipeSettings(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("inputs")] IReadOnlyList<FurnitureMaterial> Inputs,
    [property: JsonPropertyName("required_tool_ids")] IReadOnlyList<string> RequiredToolIds,
    [property: JsonPropertyName("placement_level")] int PlacementLevel,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("occupies_furniture_space")] bool OccupiesFurnitureSpace,
    [property: JsonPropertyName("blocks_movement")] bool BlocksMovement,
    [property: JsonPropertyName("east_texture_path")] string EastTexturePath,
    [property: JsonPropertyName("west_texture_path")] string WestTexturePath)
{
    [JsonPropertyName("placement_layer")] public string PlacementLayer { get; init; } = "furniture";
    [JsonPropertyName("south_texture_path")] public string SouthTexturePath { get; init; } = "";
    public int Width(string facing) => PlacementLayer == "floor" && facing == "south" ? FootprintHeightTiles : FootprintWidthTiles;
    public int Height(string facing) => PlacementLayer == "floor" && facing == "south" ? FootprintWidthTiles : FootprintHeightTiles;
    public string Texture(string facing) => facing == "east" ? EastTexturePath : facing == "south" ? SouthTexturePath : WestTexturePath;
    // Retained placements keep their exact item, geometry and art when recipes change.
    public bool SamePlacementFacts(FurnitureRecipeSettings other) => ItemId == other.ItemId &&
        FootprintWidthTiles == other.FootprintWidthTiles && FootprintHeightTiles == other.FootprintHeightTiles &&
        OccupiesFurnitureSpace == other.OccupiesFurnitureSpace && BlocksMovement == other.BlocksMovement &&
        EastTexturePath == other.EastTexturePath && WestTexturePath == other.WestTexturePath &&
        SouthTexturePath == other.SouthTexturePath && PlacementLayer == other.PlacementLayer;
}

public sealed record FurnitureMaterial([property: JsonPropertyName("item_id")] string ItemId, [property: JsonPropertyName("quantity")] int Quantity);

// Inventory arrow recipes share the existing Crafting lifecycle, not furniture geometry.
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
    [property: JsonPropertyName("iron")] ArrowCraftingStep Iron,
    [property: JsonPropertyName("steel_head_item_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SteelHeadItemId = null,
    [property: JsonPropertyName("steel_arrow_item_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SteelArrowItemId = null,
    [property: JsonPropertyName("steel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ArrowCraftingStep? Steel = null)
{
    // Optional as a complete group so older published catalogs remain valid until release.
    [JsonIgnore] public bool HasSteel => SteelHeadItemId is not null || SteelArrowItemId is not null || Steel is not null;
    [JsonIgnore] public IReadOnlyList<string> ItemIds => [KnifeItemId, ShaftItemId, HeadlessItemId, FeatherItemId, BronzeHeadItemId, IronHeadItemId, BronzeArrowItemId, IronArrowItemId,
        .. HasSteel ? new[] { SteelHeadItemId ?? "", SteelArrowItemId ?? "" } : Array.Empty<string>()];
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items, IReadOnlyList<string> furnitureItems)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || (items is not null && !items.Contains(id)))
                errors.Add($"Missing or unavailable arrow item '{id}'.");
        if (ItemIds.Distinct().Count() != ItemIds.Count || ItemIds.Intersect(furnitureItems).Any())
            errors.Add("Arrow bindings must be distinct from each other and existing Crafting bindings.");
        foreach (var step in HasSteel ? new[] { Shafts, Headless, Bronze, Iron, Steel } : new[] { Shafts, Headless, Bronze, Iron })
            if (step is null || step.Level is < 1 or > 99 || step.XpTenths is < 0 or > 10000000 || step.DurationMs is < 1 or > 86400000)
                errors.Add("Arrow steps require level 1–99, XP 0–10000000 tenths and duration 1–86400000 ms.");
        return errors;
    }
}
public sealed record ArrowCraftingStep(
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs);

// One ordinary station recipe; Crafting retains the shared production lifecycle.
public sealed record SpinningCraftingSettings(
    [property: JsonPropertyName("wool_item_id")] string WoolItemId,
    [property: JsonPropertyName("yarn_item_id")] string YarnItemId,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs);
