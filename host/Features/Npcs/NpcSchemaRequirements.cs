using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Npcs;

public sealed class NpcSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "prototype-npc-authoring-v1";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("npc_definitions"),
        AuthoringSchemaRequirement.Column("npc_definitions", "npc_definition_id"),
        AuthoringSchemaRequirement.Column("npc_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("npc_definitions", "publication_state"),
        AuthoringSchemaRequirement.Column("npc_definitions", "visual_texture_path"),
        AuthoringSchemaRequirement.Column("npc_definitions", "source_width"),
        AuthoringSchemaRequirement.Column("npc_definitions", "source_height"),
        AuthoringSchemaRequirement.Column("npc_definitions", "visual_anchor_offset_x"),
        AuthoringSchemaRequirement.Column("npc_definitions", "visual_anchor_offset_y"),
        AuthoringSchemaRequirement.Column("npc_definitions", "visual_render_scale"),
        AuthoringSchemaRequirement.Column("npc_definitions", "footprint_width_tiles"),
        AuthoringSchemaRequirement.Column("npc_definitions", "footprint_height_tiles"),
        AuthoringSchemaRequirement.Column("npc_definitions", "movement_behavior"),
        AuthoringSchemaRequirement.Column("npc_definitions", "wander_radius_tiles"),
        AuthoringSchemaRequirement.Column("npc_definitions", "tick_interval_ms"),
        AuthoringSchemaRequirement.Column("npc_definitions", "idle_chance"),
        AuthoringSchemaRequirement.Column("npc_definitions", "interaction_enabled"),
        AuthoringSchemaRequirement.Column("npc_definitions", "interaction_range_tiles"),
        AuthoringSchemaRequirement.Column("npc_definitions", "default_interaction"),
        AuthoringSchemaRequirement.Column("npc_definitions", "default_dialogue_id"),
        AuthoringSchemaRequirement.Column("npc_definitions", "notes"),
        AuthoringSchemaRequirement.Column("npc_definitions", "created_at_utc"),
        AuthoringSchemaRequirement.Column("npc_definitions", "updated_at_utc"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_id_format_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_display_name_nonblank_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_publication_state_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_visual_texture_path_nonblank_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_source_dimensions_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_visual_numbers_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_footprint_positive_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_initial_footprint_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_movement_behavior_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_wander_radius_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_movement_consistency_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_tick_interval_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_idle_chance_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_interaction_range_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_default_interaction_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_dialogue_reference_check"),
        AuthoringSchemaRequirement.Constraint("npc_definitions_timestamp_order_check")
    ];
}
