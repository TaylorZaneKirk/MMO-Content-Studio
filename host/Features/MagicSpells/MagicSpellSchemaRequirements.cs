// Declares the schema owned by the combat-spell workspace.
using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.MagicSpells;
public sealed class MagicSpellSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "magic-spell-authoring-v1";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("magic_combat_spells"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "spell_id"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "publication_state"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "created_at"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "updated_at"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "display_name"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "tier"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "element"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "required_magic_level"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "shard_cost"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "successful_hit_min_damage"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "base_max_hit"),
        AuthoringSchemaRequirement.Column("magic_combat_spells", "base_cast_xp_tenths"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_id_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_name_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_publication_state_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_tier_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_element_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_required_magic_level_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_shard_cost_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_min_damage_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_max_hit_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_cast_xp_check"),
        AuthoringSchemaRequirement.Constraint("magic_combat_spells_timestamp_order_check"),
    ];
}
