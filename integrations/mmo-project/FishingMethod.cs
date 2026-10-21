using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace MMO.Project.Prototype.Server.Game;

// Immutable authored facts for one explicit spot action. Array order is catch priority:
// each eligible catch gets its own roll; only a miss proceeds to the next catch.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FishingMethod(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("tool_item_id")] string ToolItemId,
    [property: JsonPropertyName("consumable_item_id")] string? ConsumableItemId,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("interval_ms")] int IntervalMs,
    [property: JsonPropertyName("catches")] IReadOnlyList<FishingCatch> Catches)
{
    [JsonIgnore] public int RequiredLevel => Catches.Min(catchFact => catchFact.RequiredLevel);

    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        bool Id(string? id) => id is not null && Regex.IsMatch(id, "^[a-z][a-z0-9_]*$");
        void Item(string? id)
        {
            if (!Id(id) || (items is not null && !items.Contains(id!))) errors.Add($"Unknown published item '{id}'.");
        }
        // Fishing actions cannot shadow another existing world-object command.
        if (new[] { "alchemy", "bank", "chop", "clear_drain", "close", "cook", "crafting", "enter", "enter_cellar", "enter_home", "fill", "forge", "inspect", "leave_cellar", "lock_gate", "mill", "mine", "open", "pick_wheat", "place_crate", "repair_cart", "restore_concentration", "smelt", "study_auto", "study_manual", "use", "return", "claim", "climb" }.Contains(ActionId))
            errors.Add("Action ID is reserved by another world-object behavior.");
        if (!Id(ActionId)) errors.Add("Use a stable lowercase action ID.");
        if (string.IsNullOrWhiteSpace(DisplayName)) errors.Add("Method display name is required.");
        Item(ToolItemId);
        if (ConsumableItemId is not null) Item(ConsumableItemId);
        if (ConsumableItemId is null ? ConsumeQuantity != 0 : ConsumeQuantity is < 1 or > 1000)
            errors.Add("Choose no consumable and quantity0, or a consumable and quantity1..1000 per successful catch.");
        if (ConsumableItemId == ToolItemId) errors.Add("The reusable tool cannot also be the consumed item.");
        if (IntervalMs is < 600 or > 60000) errors.Add("Attempt interval must be600..60000 milliseconds.");
        if (Catches is null || Catches.Count == 0) errors.Add("At least one ordered catch is required.");
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var catchFact in Catches)
            {
                if (catchFact is null) { errors.Add("Catch rows cannot be null."); continue; }
                Item(catchFact.ItemId);
                if (catchFact.ItemId == ConsumableItemId) errors.Add("The caught item cannot also be the consumed item.");
                if (!seen.Add(catchFact.ItemId)) errors.Add("Catch item IDs must be unique within a method.");
                if (catchFact.RequiredLevel is < 1 or > 99) errors.Add("Catch level must be1..99.");
                if (catchFact.Xp is < 1 or > 100000) errors.Add("Catch XP must be1..100000.");
                if (catchFact.SuccessLow is < 0 or > 256 || catchFact.SuccessHigh < catchFact.SuccessLow || catchFact.SuccessHigh > 256)
                    errors.Add("Catch curve must satisfy0 <= low <= high <=256.");
            }
        }
        return errors;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FishingCatch(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("xp")] int Xp,
    [property: JsonPropertyName("success_low")] int SuccessLow,
    [property: JsonPropertyName("success_high")] int SuccessHigh);
