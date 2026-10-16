using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Cooking;
public sealed class CookingSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "cooking";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("cooking_recipes"),
        AuthoringSchemaRequirement.Column("cooking_recipes", "published_recipe"),
        AuthoringSchemaRequirement.Trigger("cooking_recipes", "cooking_publication_guard"),
        AuthoringSchemaRequirement.Trigger("item_definitions", "cooking_item_guard"),
        AuthoringSchemaRequirement.Trigger("cooking_recipes", "cooking_recipe_delete_guard")];
}
