using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Fishing;
public sealed class FishingSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "fishing";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("fishing_methods"),
        AuthoringSchemaRequirement.Column("fishing_methods", "published_method"),
        AuthoringSchemaRequirement.Trigger("fishing_methods", "fishing_publication_guard"),
        AuthoringSchemaRequirement.Trigger("item_definitions", "fishing_item_guard")];
}
