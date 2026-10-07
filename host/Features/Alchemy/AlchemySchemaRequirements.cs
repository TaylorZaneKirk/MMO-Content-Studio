using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.Alchemy;
public sealed class AlchemySchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "alchemy";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() => [
        AuthoringSchemaRequirement.Table("alchemy_definitions"),
        AuthoringSchemaRequirement.Column("alchemy_definitions","published_settings"),
        AuthoringSchemaRequirement.Table("character_attack_boost")];
}
