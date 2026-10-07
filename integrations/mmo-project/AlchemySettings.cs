using System.Text.Json.Serialization;
// Authored Alchemy V1 facts shared verbatim with Content Studio. Runtime state lives on Player.
namespace MMO.Project.Alchemy;
public sealed record AlchemySettings(
    [property: JsonPropertyName("crystal_item_id")] string CrystalItemId,
    [property: JsonPropertyName("hammer_item_id")] string HammerItemId,
    [property: JsonPropertyName("shard_item_id")] string ShardItemId,
    [property: JsonPropertyName("dust_item_id")] string DustItemId,
    [property: JsonPropertyName("flower_item_id")] string FlowerItemId,
    [property: JsonPropertyName("petals_item_id")] string PetalsItemId,
    [property: JsonPropertyName("bottle_item_id")] string BottleItemId,
    [property: JsonPropertyName("potion3_item_id")] string Potion3ItemId,
    [property: JsonPropertyName("potion2_item_id")] string Potion2ItemId,
    [property: JsonPropertyName("potion1_item_id")] string Potion1ItemId,
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
    [property: JsonPropertyName("prepare_level")] int PrepareLevel,
    [property: JsonPropertyName("prepare_duration_ms")] int PrepareDurationMs,
    [property: JsonPropertyName("prepare_xp_tenths")] int PrepareXpTenths,
    [property: JsonPropertyName("prepare_flower_quantity")] int PrepareFlowerQuantity,
    [property: JsonPropertyName("prepare_petals_quantity")] int PreparePetalsQuantity,
    [property: JsonPropertyName("brew_level")] int BrewLevel,
    [property: JsonPropertyName("brew_duration_ms")] int BrewDurationMs,
    [property: JsonPropertyName("brew_xp_tenths")] int BrewXpTenths,
    [property: JsonPropertyName("brew_bottle_quantity")] int BrewBottleQuantity,
    [property: JsonPropertyName("brew_dust_quantity")] int BrewDustQuantity,
    [property: JsonPropertyName("brew_petals_quantity")] int BrewPetalsQuantity,
    [property: JsonPropertyName("brew_potion_quantity")] int BrewPotionQuantity,
    [property: JsonPropertyName("attack_bonus_flat")] int AttackBonusFlat,
    [property: JsonPropertyName("attack_bonus_percent")] int AttackBonusPercent,
    [property: JsonPropertyName("decay_interval_ms")] int DecayIntervalMs,
    [property: JsonPropertyName("sip_cooldown_ms")] int SipCooldownMs)
{
    [JsonIgnore]
    public IReadOnlyList<string> ItemIds => [CrystalItemId, HammerItemId, ShardItemId, DustItemId, FlowerItemId, PetalsItemId, BottleItemId, Potion3ItemId, Potion2ItemId, Potion1ItemId];
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        foreach (var id in ItemIds)
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unavailable Alchemy item '{id}'.");
        if (ItemIds.Distinct().Count() != ItemIds.Count) errors.Add("Alchemy item bindings must be distinct.");
        if (string.IsNullOrWhiteSpace(StationDefinitionId)) errors.Add("Station definition required.");
        if (!double.IsFinite(CrystalLowPercent) || !double.IsFinite(CrystalHighPercent) || CrystalLowPercent < 0 || CrystalHighPercent > 100 || CrystalLowPercent > CrystalHighPercent || CrystalCapLevel is < 2 or > 99) errors.Add("Crystal curve requires increasing 0–100 percentages and cap level 2–99.");
        foreach (var level in new[] { CrushLevel, PrepareLevel, BrewLevel }) if (level is < 1 or > 99) errors.Add("Required levels must be 1–99.");
        foreach (var time in new[] { CrushDurationMs, PrepareDurationMs, BrewDurationMs, DecayIntervalMs, SipCooldownMs }) if (time is < 1 or > 86400000) errors.Add("Durations must be 1–86400000 milliseconds.");
        foreach (var xp in new[] { CrushXpTenths, PrepareXpTenths, BrewXpTenths }) if (xp is < 0 or > 10000000) errors.Add("XP tenths must be 0–10000000.");
        if (ShardMin < 1 || ShardMax < ShardMin || ShardMax > 1000000 || DustMin < 0 || DustMax < DustMin || DustMax > 1000000) errors.Add("Invalid inclusive crushing yield ranges.");
        foreach (var quantity in new[] { PrepareFlowerQuantity, PreparePetalsQuantity, BrewBottleQuantity, BrewDustQuantity, BrewPetalsQuantity, BrewPotionQuantity }) if (quantity is < 1 or > 10000) errors.Add("Recipe quantities must be 1–10000.");
        if (AttackBonusFlat is < 0 or > 99 || AttackBonusPercent is < 0 or > 100) errors.Add("Invalid Attack boost.");
        return errors;
    }
    public IReadOnlyList<string> ValidateItemShapes(IReadOnlyDictionary<string, bool> stackability)
    {
        var errors = new List<string>();
        foreach (var id in new[] { CrystalItemId, BottleItemId, Potion3ItemId, Potion2ItemId, Potion1ItemId })
            if (string.IsNullOrWhiteSpace(id) || !stackability.TryGetValue(id, out var stacked) || stacked) errors.Add($"Alchemy item '{id}' must be enabled and nonstackable.");
        foreach (var id in new[] { ShardItemId, DustItemId, PetalsItemId })
            if (string.IsNullOrWhiteSpace(id) || !stackability.TryGetValue(id, out var stacked) || !stacked) errors.Add($"Alchemy item '{id}' must be enabled and stackable.");
        return errors;
    }

}
