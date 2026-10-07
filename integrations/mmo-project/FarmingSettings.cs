using System.Text.Json.Serialization;
namespace MMO.Project.Farming;

public sealed record FarmingSettings([property: JsonPropertyName("hoe_item_id")] string HoeItemId, [property: JsonPropertyName("bucket_item_id")] string BucketItemId, [property: JsonPropertyName("seed_item_id")] string SeedItemId, [property: JsonPropertyName("flower_item_id")] string FlowerItemId,
    [property: JsonPropertyName("till_seconds")] int TillSeconds = 3, [property: JsonPropertyName("growth_seconds")] int GrowthSeconds = 300, [property: JsonPropertyName("prepared_seconds")] int PreparedSeconds = 600,
    [property: JsonPropertyName("till_low_percent")] double TillLowPercent = 50, [property: JsonPropertyName("till_high_percent")] double TillHighPercent = 100, [property: JsonPropertyName("growth_low_percent")] double GrowthLowPercent = 60,
    [property: JsonPropertyName("growth_high_percent")] double GrowthHighPercent = 90, [property: JsonPropertyName("chance_cap_level")] int ChanceCapLevel = 50, [property: JsonPropertyName("water_bonus_percent")] double WaterBonusPercent = 25,
    [property: JsonPropertyName("till_xp")] int TillXp = 5, [property: JsonPropertyName("plant_xp")] int PlantXp = 5, [property: JsonPropertyName("harvest_xp")] int HarvestXp = 20)
{
    public double TillChance(int level) => Interpolate(TillLowPercent, TillHighPercent, level);
    public double GrowthChance(int level) => Interpolate(GrowthLowPercent, GrowthHighPercent, level);
    private double Interpolate(double low, double high, int level) => low + (high-low) * (Math.Clamp(level,1,ChanceCapLevel)-1) / (ChanceCapLevel-1);
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        foreach (var id in new[] { HoeItemId, BucketItemId, SeedItemId, FlowerItemId })
            if (string.IsNullOrWhiteSpace(id) || items is not null && !items.Contains(id)) errors.Add($"Missing or unpublished Farming item '{id}'.");
        if (TillSeconds <= 0 || GrowthSeconds <= 0 || PreparedSeconds <= 0) errors.Add("Durations must be positive seconds.");
        if (ChanceCapLevel is < 2 or > 99) errors.Add("Chance cap level must be 2–99.");
        foreach (var chance in new[] { TillLowPercent, TillHighPercent, GrowthLowPercent, GrowthHighPercent, WaterBonusPercent })
            if (!double.IsFinite(chance) || chance < 0 || chance > 100) errors.Add("Chances must be 0–100 percent.");
        if (TillLowPercent > TillHighPercent || GrowthLowPercent > GrowthHighPercent) errors.Add("Higher level cannot reduce success chance.");
        if (TillXp is < 0 or > 1000000 || PlantXp is < 0 or > 1000000 || HarvestXp is < 0 or > 1000000) errors.Add("XP must be between 0 and 1000000.");
        return errors;
    }
}
