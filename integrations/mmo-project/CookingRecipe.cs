using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace MMO.Project.Prototype.Server.Game;

// Published content facts shared by the runtime, Studio and exporter. No activity state.
public sealed record CookingIngredient(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("quantity")] int Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CookingRecipe(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<CookingIngredient> Ingredients,
    [property: JsonPropertyName("output_item_id")] string OutputItemId,
    [property: JsonPropertyName("output_quantity")] int OutputQuantity,
    [property: JsonPropertyName("required_cooking_level")] int RequiredCookingLevel,
    [property: JsonPropertyName("xp")] int Xp,
    [property: JsonPropertyName("success_low")] int SuccessLow,
    [property: JsonPropertyName("success_high")] int SuccessHigh,
    [property: JsonPropertyName("burnt_output_item_id")] string? BurntOutputItemId = null,
    [property: JsonPropertyName("operation")] string Operation = "cook",
    [property: JsonPropertyName("duration_ms")] int DurationMs = 2400,
    [property: JsonPropertyName("additional_outputs")] IReadOnlyList<CookingIngredient>? AdditionalOutputs = null)
{
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null)
    {
        var errors = new List<string>();
        bool Id(string? id) => id is not null && Regex.IsMatch(id, "^[a-z][a-z0-9_]*$");
        void Item(string? id)
        {
            if (!Id(id) || (items is not null && !items.Contains(id!)))
                errors.Add($"Missing, invalid or unpublished item '{id}'.");
        }
        if (!Id(RecipeId)) errors.Add("A stable recipe ID is required.");
        if (Ingredients is null || (Ingredients.Count == 0 && Operation != "pick_wheat")) errors.Add("At least one ingredient is required.");
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in Ingredients)
            {
                if (input is null) { errors.Add("An ingredient row is required."); continue; }
                Item(input.ItemId);
                if (!seen.Add(input.ItemId)) errors.Add("Combine duplicate ingredients into one quantity.");
                if (input.Quantity <= 0) errors.Add("Ingredient quantities must be positive.");
            }
        }
        Item(OutputItemId);
        if (BurntOutputItemId is not null) Item(BurntOutputItemId);
        if (OutputQuantity <= 0) errors.Add("Output quantity must be positive.");
        if (RequiredCookingLevel is < 1 or > 99) errors.Add("Cooking level must be 1–99.");
        if (Operation is not ("cook" or "pick_wheat" or "mill" or "mix" or "shape" or "fill")) errors.Add("Unknown Cooking operation.");
        if (DurationMs is < 600 or > 60000 || DurationMs % 600 != 0) errors.Add("Duration must be whole 600 ms ticks, at most one minute.");
        if (Operation == "cook" && DurationMs != 2400) errors.Add("Stove Cooking uses 2400 ms.");
        if (Operation == "cook" && Xp <= 0) errors.Add("Success XP must be positive.");
        if (Operation != "cook" && (Xp != 0 || RequiredCookingLevel != 1 || SuccessLow != 256 || SuccessHigh != 256 || BurntOutputItemId is not null))
            errors.Add("Preparation must be guaranteed, Cooking 1, zero XP and without burnt output.");
        if (Operation == "pick_wheat" && Ingredients?.Count != 0) errors.Add("Wheat picking has no inventory input.");
        if (Operation is "mix" or "shape" && Ingredients?.Count != 2) errors.Add("Inventory preparation requires two distinct ingredients.");
        var outputs = new HashSet<string>(StringComparer.Ordinal) { OutputItemId };
        foreach (var output in AdditionalOutputs ?? [])
        {
            if (output is null) { errors.Add("An output row is required."); continue; }
            Item(output.ItemId);
            if (!outputs.Add(output.ItemId) || output.Quantity <= 0) errors.Add("Outputs must be distinct with positive quantities.");
        }
        if (Operation == "cook" && AdditionalOutputs?.Count > 0) errors.Add("Stove Cooking uses one output.");
        if (SuccessLow is < 0 or > 256 || SuccessHigh is < 0 or > 256 || SuccessHigh < SuccessLow)
            errors.Add("Burn thresholds must satisfy 0 <= low <= high <= 256.");
        return errors;
    }
}
