using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Equipment;

public sealed class EquipmentSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "equipment";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("equipment_slot_definitions"),
        AuthoringSchemaRequirement.Table("skill_definitions"),
        AuthoringSchemaRequirement.Table("item_skill_requirements"),
        AuthoringSchemaRequirement.Table("item_skill_modifiers"),
        AuthoringSchemaRequirement.Table("item_combat_profiles"),
        AuthoringSchemaRequirement.Table("item_combat_bonuses"),
        AuthoringSchemaRequirement.Column("equipment_slot_definitions", "slot_id"),
        AuthoringSchemaRequirement.Column("equipment_slot_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("equipment_slot_definitions", "sort_order"),
        AuthoringSchemaRequirement.Column("skill_definitions", "skill_id"),
        AuthoringSchemaRequirement.Column("skill_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("skill_definitions", "sort_order"),
        AuthoringSchemaRequirement.Column("item_skill_requirements", "item_id"),
        AuthoringSchemaRequirement.Column("item_skill_requirements", "skill_id"),
        AuthoringSchemaRequirement.Column("item_skill_requirements", "required_value"),
        AuthoringSchemaRequirement.Column("item_skill_modifiers", "item_id"),
        AuthoringSchemaRequirement.Column("item_skill_modifiers", "skill_id"),
        AuthoringSchemaRequirement.Column("item_skill_modifiers", "modifier_value"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "item_id"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "profile_id"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "attack_type"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "accuracy_style"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "minimum_range_tiles"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "maximum_range_tiles"),
        AuthoringSchemaRequirement.Column("item_combat_profiles", "attack_speed_units"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "attack_thrust"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "attack_slash"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "attack_crush"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "attack_ranged"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "attack_magic"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "strength_melee"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "strength_ranged"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "strength_magic"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "defence_thrust"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "defence_slash"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "defence_crush"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "defence_ranged"),
        AuthoringSchemaRequirement.Column("item_combat_bonuses", "defence_magic"),
        AuthoringSchemaRequirement.Constraint("item_definitions_equipment_slot_id_fkey"),
        AuthoringSchemaRequirement.Constraint("item_skill_requirements_required_value_check"),
        AuthoringSchemaRequirement.Constraint("item_combat_profiles_attack_type_check"),
        AuthoringSchemaRequirement.Constraint("item_combat_profiles_accuracy_style_check"),
        AuthoringSchemaRequirement.Constraint(
            "item_combat_profiles_attack_type_accuracy_style_check"),
        AuthoringSchemaRequirement.Constraint("item_combat_profiles_attack_speed_units_check")
    ];
}
