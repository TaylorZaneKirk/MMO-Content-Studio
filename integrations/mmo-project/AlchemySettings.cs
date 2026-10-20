using System.Text.Json.Serialization;
// Authored Alchemy facts shared verbatim with Content Studio. Runtime state lives on Player.
namespace MMO.Project.Alchemy;
public sealed record AlchemySettings(
    [property: JsonPropertyName("crystal_item_id")] string CrystalItemId,
    [property: JsonPropertyName("hammer_item_id")] string HammerItemId,
    [property: JsonPropertyName("shard_item_id")] string ShardItemId,
    [property: JsonPropertyName("dust_item_id")] string DustItemId,
    [property: JsonPropertyName("bottle_item_id")] string BottleItemId,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("crystal_low_percent")] double CrystalLowPercent,
    [property: JsonPropertyName("crystal_high_percent")] double CrystalHighPercent,
    [property: JsonPropertyName("crystal_cap_level")] int CrystalCapLevel,
    [property: JsonPropertyName("crush_level")] int CrushLevel,
    [property: JsonPropertyName("crush_duration_ms")] int CrushDurationMs,
    [property: JsonPropertyName("crush_xp_tenths")] int CrushXpTenths,
    [property: JsonPropertyName("shard_min")] int ShardMin,
    [property: JsonPropertyName("shard_max")] int ShardMax,
    [property: JsonPropertyName("dust_min")] int DustMin,
    [property: JsonPropertyName("dust_max")] int DustMax,
    [property: JsonPropertyName("decay_interval_ms")] int DecayIntervalMs,
    [property: JsonPropertyName("sip_cooldown_ms")] int SipCooldownMs,
    [property: JsonPropertyName("recipes")] IReadOnlyList<AlchemyRecipe> Recipes,
    [property: JsonPropertyName("potions")] IReadOnlyList<AlchemyPotion> Potions)
{
    [JsonIgnore] public IReadOnlyList<string> ItemIds => new[] { CrystalItemId, HammerItemId, ShardItemId, DustItemId, BottleItemId }
        .Concat((Recipes ?? []).SelectMany(r => r.Inputs.Select(i => i.ItemId).Concat(r.Outputs.Select(o => o.ItemId))))
        .Concat((Potions ?? []).SelectMany(p => new[] { p.Potion3ItemId, p.Potion2ItemId, p.Potion1ItemId })).Distinct().ToArray();
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        if (Recipes is null || Recipes.Count == 0 || Potions is null || Potions.Count == 0)
            return ["Alchemy requires authored recipes and potions."];
        if (Recipes.Any(r => r is null || r.Inputs is null || r.Outputs is null || r.Inputs.Any(i => i is null) || r.Outputs.Any(o => o is null)) || Potions.Any(p => p is null))
            return ["Each recipe needs input/output arrays and every recipe, ingredient and potion row must be an object."];
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unavailable Alchemy item '{id}'.");
        if (string.IsNullOrWhiteSpace(StationDefinitionId)) errors.Add("Station definition required.");
        if (!double.IsFinite(CrystalLowPercent) || !double.IsFinite(CrystalHighPercent) || CrystalLowPercent < 0 || CrystalHighPercent > 100 || CrystalLowPercent > CrystalHighPercent || CrystalCapLevel is < 2 or > 99) errors.Add("Invalid crystal chance curve.");
        if (CrushLevel is < 1 or > 99 || CrushXpTenths is < 0 or > 10000000) errors.Add("Invalid crystal crushing level or XP.");
        foreach (var time in new[] { CrushDurationMs, DecayIntervalMs, SipCooldownMs })
            if (time is < 1 or > 86400000) errors.Add("Durations must be 1–86400000 milliseconds.");
        if (ShardMin < 1 || ShardMax < ShardMin || ShardMax > 1000000 || DustMin < 0 || DustMax < DustMin || DustMax > 1000000) errors.Add("Invalid crushing yields.");
        if (Recipes.Select(r => r.RecipeId).Distinct().Count() != Recipes.Count) errors.Add("Recipe IDs must be unique.");
        foreach (var recipe in Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.RecipeId) || recipe.RecipeId == "crush_crystal" || string.IsNullOrWhiteSpace(recipe.DisplayName)) errors.Add("Each station recipe requires a unique ID and name.");
            if (recipe.RequiredLevel is < 1 or > 99 || recipe.XpTenths is < 0 or > 10000000 || recipe.DurationMs is < 1 or > 86400000) errors.Add("Invalid recipe level, XP or duration.");
            if (recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0 || recipe.Inputs.Any(i => i.Quantity is < 1 or > 10000) || recipe.Outputs.Any(o => o.Minimum is < 1 or > 10000 || o.Maximum != o.Minimum)) errors.Add("Station recipes require positive, guaranteed quantities.");
            if (recipe.Inputs.Select(i => i.ItemId).Distinct().Count() != recipe.Inputs.Count || recipe.Outputs.Select(i => i.ItemId).Distinct().Count() != recipe.Outputs.Count) errors.Add("Combine duplicate recipe items in one row.");
            if (recipe.Operation != "alchemy" || recipe.StationDefinitionId != StationDefinitionId || recipe.RequiredInventoryToolId is not null) errors.Add("Station recipes use the Alchemy lab and no inventory tool.");
        }
        var doses = Potions.SelectMany(p => new[] { p.Potion3ItemId, p.Potion2ItemId, p.Potion1ItemId }).ToArray();
        if (doses.Distinct().Count() != doses.Length || doses.Contains(BottleItemId)) errors.Add("Potion dose items must be distinct.");
        foreach (var potion in Potions)
            if (potion.Stat is not ("attack" or "strength" or "defence") || potion.BonusFlat is < 0 or > 99 || potion.BonusPercent is < 0 or > 100) errors.Add("Invalid combat-stat potion.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> stackability)
    {
        var errors = new List<string>();
        if (Potions is null || Potions.Any(p => p is null)) return ["Potion rows are required."];
        foreach (var id in new[] { CrystalItemId, BottleItemId }.Concat((Potions ?? []).SelectMany(p => new[] { p.Potion3ItemId, p.Potion2ItemId, p.Potion1ItemId })))
            if (string.IsNullOrWhiteSpace(id) || !stackability.TryGetValue(id, out var stacked) || stacked) errors.Add($"Alchemy item '{id}' must be enabled and nonstackable.");
        foreach (var id in new[] { ShardItemId, DustItemId })
            if (string.IsNullOrWhiteSpace(id) || !stackability.TryGetValue(id, out var stacked) || !stacked) errors.Add($"Alchemy item '{id}' must be enabled and stackable.");
        return errors;
    }
    public AlchemyPotion? FindPotion(string? id) => Potions.FirstOrDefault(p => id == p.Potion3ItemId || id == p.Potion2ItemId || id == p.Potion1ItemId);
}
public sealed record AlchemyPotion(
    [property: JsonPropertyName("stat")] string Stat,
    [property: JsonPropertyName("potion3_item_id")] string Potion3ItemId,
    [property: JsonPropertyName("potion2_item_id")] string Potion2ItemId,
    [property: JsonPropertyName("potion1_item_id")] string Potion1ItemId,
    [property: JsonPropertyName("bonus_flat")] int BonusFlat,
    [property: JsonPropertyName("bonus_percent")] int BonusPercent);
public sealed record AlchemyInput([property: JsonPropertyName("item_id")] string ItemId, [property: JsonPropertyName("quantity")] int Quantity);
public sealed record AlchemyOutput([property: JsonPropertyName("item_id")] string ItemId, [property: JsonPropertyName("minimum")] int Minimum, [property: JsonPropertyName("maximum")] int Maximum);
public sealed record AlchemyRecipe(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("inputs")] IReadOnlyList<AlchemyInput> Inputs,
    [property: JsonPropertyName("outputs")] IReadOnlyList<AlchemyOutput> Outputs,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("required_inventory_tool_id")] string? RequiredInventoryToolId)
{
    [JsonIgnore] public string OutputItemId => Outputs[0].ItemId;
    [JsonIgnore] public int OutputQuantity => Outputs[0].Minimum;
}
