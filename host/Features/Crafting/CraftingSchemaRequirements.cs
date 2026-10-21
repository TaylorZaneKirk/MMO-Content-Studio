using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Crafting;

public sealed class CraftingSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "crafting";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("crafting_definitions"),
        AuthoringSchemaRequirement.Column("crafting_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("character_home_furniture"),
        AuthoringSchemaRequirement.Column("character_home_furniture", "item_id"),
        AuthoringSchemaRequirement.Trigger("character_home_furniture", "home_furniture_wall_footprint_guard")];
}
