using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Lore;
public sealed class LoreSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "lore";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("lore_definitions"),
        AuthoringSchemaRequirement.Column("lore_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("character_lore"),
        AuthoringSchemaRequirement.Column("character_stats", "concentration_drain_ticks"),
        AuthoringSchemaRequirement.Column("character_skills", "experience_hundredths_remainder")];
}
