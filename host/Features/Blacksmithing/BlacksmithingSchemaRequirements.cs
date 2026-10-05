using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Blacksmithing;

public sealed class BlacksmithingSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "blacksmithing";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("blacksmithing_recipes"),
        AuthoringSchemaRequirement.Table("blacksmithing_recipe_inputs"),
        AuthoringSchemaRequirement.Column("blacksmithing_recipes", "xp_tenths"),
        AuthoringSchemaRequirement.Constraint("blacksmithing_recipe_operation_shape"),
        AuthoringSchemaRequirement.Trigger("blacksmithing_recipes", "blacksmithing_publication_guard"),
        AuthoringSchemaRequirement.Trigger("blacksmithing_recipe_inputs", "blacksmithing_inputs_guard"),
        AuthoringSchemaRequirement.Trigger("item_definitions", "blacksmithing_item_guard"),
        AuthoringSchemaRequirement.Trigger("world_object_definitions", "blacksmithing_station_guard")
    ];
}
