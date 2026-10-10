using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Lore;
public sealed class LoreSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "lore";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("lore_definitions"),
        AuthoringSchemaRequirement.Column("lore_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("character_lore"),
        AuthoringSchemaRequirement.Column("character_lore", "family_id"),
        AuthoringSchemaRequirement.Constraint("lore_flower_milestone_limit"),
        AuthoringSchemaRequirement.Column("character_lore", "mastery_points"),
        AuthoringSchemaRequirement.Column("character_lore", "unlocked_milestones"),
        AuthoringSchemaRequirement.Column("character_lore", "legacy_accuracy_basis_points"),
        AuthoringSchemaRequirement.Column("character_stats", "concentration_spent_units"),
        AuthoringSchemaRequirement.Column("character_skills", "experience_hundredths_remainder")];
}
