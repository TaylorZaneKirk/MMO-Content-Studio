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
    [property: JsonPropertyName("burnt_output_item_id")] string? BurntOutputItemId = null)
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
        if (Ingredients is null || Ingredients.Count == 0) errors.Add("At least one ingredient is required.");
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
        if (Xp <= 0) errors.Add("Success XP must be positive.");
        if (SuccessLow is < 0 or > 256 || SuccessHigh is < 0 or > 256 || SuccessHigh < SuccessLow)
            errors.Add("Burn thresholds must satisfy 0 <= low <= high <= 256.");
        return errors;
    }
}
