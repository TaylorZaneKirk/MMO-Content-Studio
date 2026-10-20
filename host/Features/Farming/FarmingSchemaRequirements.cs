using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Farming;
public sealed class FarmingSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "farming";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("farming_definitions"),
        AuthoringSchemaRequirement.Constraint("farming_authored_rows"),
        AuthoringSchemaRequirement.Column("farming_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("farming_patch_definitions"),
        AuthoringSchemaRequirement.Table("character_farming_patches")];
}
