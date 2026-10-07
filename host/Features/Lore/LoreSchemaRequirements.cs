using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Lore;
public sealed class LoreSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "lore";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("lore_definitions"),
        AuthoringSchemaRequirement.Column("lore_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("character_lore")];
}
