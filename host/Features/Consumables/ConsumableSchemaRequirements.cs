using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Consumables;

public sealed class ConsumableSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "consumables";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("item_consumable_profiles"),
        AuthoringSchemaRequirement.Table("item_consumable_requirements"),
        AuthoringSchemaRequirement.Table("item_consumable_effects"),
        AuthoringSchemaRequirement.Table("skill_definitions"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "use_action"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "consume_quantity"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "result_item_id"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "success_message"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "usable_in_combat"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "cooldown_ms"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "use_animation_id"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "use_sound_resource_path"),
        AuthoringSchemaRequirement.Column("item_consumable_profiles", "updated_at"),
        AuthoringSchemaRequirement.Column("item_consumable_requirements", "requirement_index"),
        AuthoringSchemaRequirement.Column("item_consumable_requirements", "requirement_type"),
        AuthoringSchemaRequirement.Column("item_consumable_requirements", "target_id"),
        AuthoringSchemaRequirement.Column("item_consumable_requirements", "minimum_value"),
        AuthoringSchemaRequirement.Column("item_consumable_effects", "effect_index"),
        AuthoringSchemaRequirement.Column("item_consumable_effects", "effect_type"),
        AuthoringSchemaRequirement.Column("item_consumable_effects", "target_id"),
        AuthoringSchemaRequirement.Column("item_consumable_effects", "minimum_amount"),
        AuthoringSchemaRequirement.Column("item_consumable_effects", "maximum_amount"),
        AuthoringSchemaRequirement.Constraint("item_consumable_profiles_use_action_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_profiles_consume_quantity_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_profiles_cooldown_ms_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_profiles_result_not_self_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_requirements_identity_key"),
        AuthoringSchemaRequirement.Constraint("item_consumable_requirements_index_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_requirements_type_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_requirements_skill_id_fkey"),
        AuthoringSchemaRequirement.Constraint("item_consumable_requirements_minimum_value_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_effects_identity_key"),
        AuthoringSchemaRequirement.Constraint("item_consumable_effects_index_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_effects_type_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_effects_resource_check"),
        AuthoringSchemaRequirement.Constraint("item_consumable_effects_amount_range_check"),
        AuthoringSchemaRequirement.Trigger(
            "item_definitions",
            "item_definitions_consumable_result_publication_guard"),
        AuthoringSchemaRequirement.Trigger(
            "item_consumable_profiles",
            "item_consumable_profiles_result_publication_guard")
    ];
}
