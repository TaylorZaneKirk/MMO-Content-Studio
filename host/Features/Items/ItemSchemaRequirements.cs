using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Items;

public sealed class ItemSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "items";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("item_definitions"),
        AuthoringSchemaRequirement.Table("character_inventory"),
        AuthoringSchemaRequirement.Table("character_equipment"),
        AuthoringSchemaRequirement.Table("ground_items"),
        AuthoringSchemaRequirement.Column("item_definitions", "item_id"),
        AuthoringSchemaRequirement.Column("item_definitions", "item_name"),
        AuthoringSchemaRequirement.Column("item_definitions", "icon_texture_path"),
        AuthoringSchemaRequirement.Column("item_definitions", "equipment_slot_id"),
        AuthoringSchemaRequirement.Column("item_definitions", "runtime_enabled"),
        AuthoringSchemaRequirement.Column("item_definitions", "required_strength"),
        AuthoringSchemaRequirement.Column("item_definitions", "updated_at"),
        AuthoringSchemaRequirement.Trigger(
            "item_definitions",
            "item_definitions_runtime_disable_guard")
    ];
}
