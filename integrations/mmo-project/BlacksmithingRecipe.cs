// Immutable authored recipe facts. No live activity or settlement state.
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace MMO.Project.Blacksmithing;

public sealed record BlacksmithingInput(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("quantity")] int Quantity);
public sealed record BlacksmithingRecipe(
    [property: JsonPropertyName("recipe_id")] string RecipeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("station_definition_id")] string StationDefinitionId,
    [property: JsonPropertyName("inputs")] IReadOnlyList<BlacksmithingInput> Inputs,
    [property: JsonPropertyName("output_item_id")] string OutputItemId,
    [property: JsonPropertyName("output_quantity")] int OutputQuantity,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("xp_tenths")] int XpTenths,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("required_inventory_tool_id")] string? RequiredInventoryToolId)
{
    // Drafts retain incomplete references. Published export and startup supply the
    // actual published item/station sets; malformed published facts never disappear.
    public IReadOnlyList<string> Validate(IReadOnlySet<string>? items = null, IReadOnlySet<string>? stations = null)
    {
        var errors = new List<string>();
        void Bad(string field, string reason) => errors.Add($"Recipe '{RecipeId}' {field}: {reason}");
        bool Id(string? id) => id is not null && Regex.IsMatch(id, "^[a-z][a-z0-9_]*$");
        void Item(string field, string? id)
        {
            if (!Id(id)) Bad(field, "requires a stable item ID");
            else if (items is not null && !items.Contains(id!)) Bad(field, $"item '{id}' is missing or unpublished");
        }
        if (!Id(RecipeId)) Bad("recipe_id", "requires a stable ID");
        if (string.IsNullOrWhiteSpace(DisplayName)) Bad("display_name", "required");
        if (Operation is not ("smelt" or "forge")) Bad("operation", "must be smelt or forge");
        var expectedStation = Operation == "smelt" ? "bronze_smelter" : "blacksmithing_anvil";
        if (StationDefinitionId != expectedStation) Bad("station_definition_id", $"operation requires '{expectedStation}'");
        if (stations is not null && !stations.Contains(StationDefinitionId)) Bad("station_definition_id", $"'{StationDefinitionId}' is missing or unpublished");
        if (Operation == "smelt" && RequiredInventoryToolId is not null) Bad("required_inventory_tool_id", "smelting requires no tool");
        if (Operation == "forge" && RequiredInventoryToolId != "blacksmithing_hammer") Bad("required_inventory_tool_id", "forging requires blacksmithing_hammer in inventory");
        if (RequiredInventoryToolId is not null) Item("required_inventory_tool_id", RequiredInventoryToolId);
        if (Inputs is null || Inputs.Count == 0) Bad("inputs", "at least one input required");
        else
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < Inputs.Count; index++)
            {
                var input = Inputs[index];
                if (input is null) { Bad($"inputs[{index}]", "row required"); continue; }
                Item($"inputs[{index}].item_id", input.ItemId);
                if (!ids.Add(input.ItemId)) Bad($"inputs[{index}].item_id", $"duplicate '{input.ItemId}'; use one row with its quantity");
                if (input.Quantity <= 0) Bad($"inputs[{index}].quantity", "must be positive");
            }
        }
        Item("output_item_id", OutputItemId);
        if (OutputQuantity <= 0) Bad("output_quantity", "must be positive");
        if (RequiredLevel is < 1 or > 99) Bad("required_level", "must be 1–99");
        if (XpTenths < 0) Bad("xp_tenths", "must be nonnegative integer tenths");
        if (DurationMs <= 0) Bad("duration_ms", "must be positive");
        return errors;
    }
}
