// Reports the World Object feature's relational schema through normal host health.
using MMO.ContentStudio.AuthoringHost.Health;
namespace MMO.ContentStudio.AuthoringHost.Features.WorldObjects;
public sealed class WorldObjectSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "world-object-authoring-v1";
    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("world_object_definitions"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "definition_id"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "publication_state"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "blocks_movement"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "footprint_width_tiles"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "footprint_height_tiles"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "visual_texture_path"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "source_width"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "source_height"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "visual_anchor_offset_x"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "visual_anchor_offset_y"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "visual_render_scale"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "visual_animation_fps"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "created_at_utc"),
        AuthoringSchemaRequirement.Column("world_object_definitions", "updated_at_utc"),
        AuthoringSchemaRequirement.Table("world_object_interactions"),
        AuthoringSchemaRequirement.Column("world_object_interactions", "definition_id"),
        AuthoringSchemaRequirement.Column("world_object_interactions", "interaction_order"),
        AuthoringSchemaRequirement.Column("world_object_interactions", "action_id"),
        AuthoringSchemaRequirement.Column("world_object_interactions", "label"),
        AuthoringSchemaRequirement.Column("world_object_interactions", "is_default"),
        AuthoringSchemaRequirement.Table("world_object_animation_frames"),
        AuthoringSchemaRequirement.Column("world_object_animation_frames", "definition_id"),
        AuthoringSchemaRequirement.Column("world_object_animation_frames", "frame_order"),
        AuthoringSchemaRequirement.Column("world_object_animation_frames", "texture_path"),
    ];
}
