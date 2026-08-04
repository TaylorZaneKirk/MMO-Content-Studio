using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Dialogues;

public sealed class DialogueSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "prototype-dialogue-authoring-v1";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("dialogue_definitions"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "dialogue_definition_id"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "publication_state"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "schema_version"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "metadata_description"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "notes"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "created_at_utc"),
        AuthoringSchemaRequirement.Column("dialogue_definitions", "updated_at_utc"),
        AuthoringSchemaRequirement.Table("dialogue_entry_points"),
        AuthoringSchemaRequirement.Column("dialogue_entry_points", "dialogue_definition_id"),
        AuthoringSchemaRequirement.Column("dialogue_entry_points", "entry_id"),
        AuthoringSchemaRequirement.Column("dialogue_entry_points", "node_id"),
        AuthoringSchemaRequirement.Column("dialogue_entry_points", "priority"),
        AuthoringSchemaRequirement.Column("dialogue_entry_points", "entry_order"),
        AuthoringSchemaRequirement.Table("dialogue_nodes"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "dialogue_definition_id"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "node_id"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "node_type"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "speaker"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "text"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "next_node_id"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "dismissible"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "canvas_x"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "canvas_y"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "editor_notes"),
        AuthoringSchemaRequirement.Column("dialogue_nodes", "node_order"),
        AuthoringSchemaRequirement.Table("dialogue_choices"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "dialogue_definition_id"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "node_id"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "choice_id"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "text"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "target_node_id"),
        AuthoringSchemaRequirement.Column("dialogue_choices", "choice_order"),
        AuthoringSchemaRequirement.Constraint("dialogue_definitions_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_definitions_display_name_nonblank_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_definitions_publication_state_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_definitions_schema_version_positive_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_definitions_current_schema_version_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_entry_points_entry_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_entry_points_node_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_entry_points_priority_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_entry_points_entry_order_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_nodes_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_nodes_supported_node_type_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_nodes_canvas_finite_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_nodes_node_order_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_nodes_next_node_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_choices_choice_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_choices_target_node_id_format_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_choices_text_nonblank_check"),
        AuthoringSchemaRequirement.Constraint("dialogue_choices_choice_order_check"),
        AuthoringSchemaRequirement.Trigger("dialogue_entry_points", "dialogue_entry_points_touch_definition_updated_at"),
        AuthoringSchemaRequirement.Trigger("dialogue_nodes", "dialogue_nodes_touch_definition_updated_at"),
        AuthoringSchemaRequirement.Trigger("dialogue_choices", "dialogue_choices_touch_definition_updated_at")
    ];
}
