using MMO.ContentStudio.AuthoringHost.Health;

namespace MMO.ContentStudio.AuthoringHost.Features.Quests;

public sealed class QuestSchemaRequirements : IAuthoringSchemaRequirementProvider
{
    public string FeatureId => "prototype-quest-authoring-v1";

    public IReadOnlyList<AuthoringSchemaRequirement> GetRequirements() =>
    [
        AuthoringSchemaRequirement.Table("quest_definitions"),
        AuthoringSchemaRequirement.Column("quest_definitions", "quest_id"),
        AuthoringSchemaRequirement.Column("quest_definitions", "display_name"),
        AuthoringSchemaRequirement.Column("quest_definitions", "publication_state"),
        AuthoringSchemaRequirement.Column("quest_definitions", "schema_version"),
        AuthoringSchemaRequirement.Column("quest_definitions", "created_at_utc"),
        AuthoringSchemaRequirement.Column("quest_definitions", "updated_at_utc"),
        AuthoringSchemaRequirement.Table("quest_steps"),
        AuthoringSchemaRequirement.Column("quest_steps", "quest_id"),
        AuthoringSchemaRequirement.Column("quest_steps", "step_id"),
        AuthoringSchemaRequirement.Column("quest_steps", "display_name"),
        AuthoringSchemaRequirement.Column("quest_steps", "step_order"),
        AuthoringSchemaRequirement.Table("quest_transitions"),
        AuthoringSchemaRequirement.Column("quest_transitions", "quest_id"),
        AuthoringSchemaRequirement.Column("quest_transitions", "transition_id"),
        AuthoringSchemaRequirement.Column("quest_transitions", "source_status"),
        AuthoringSchemaRequirement.Column("quest_transitions", "source_step_id"),
        AuthoringSchemaRequirement.Column("quest_transitions", "target_status"),
        AuthoringSchemaRequirement.Column("quest_transitions", "target_step_id"),
        AuthoringSchemaRequirement.Column("quest_transitions", "transition_order"),
        AuthoringSchemaRequirement.Constraint("quest_definitions_id_format_check"),
        AuthoringSchemaRequirement.Constraint("quest_definitions_publication_state_check"),
        AuthoringSchemaRequirement.Constraint("quest_steps_step_id_format_check"),
        AuthoringSchemaRequirement.Constraint("quest_transitions_transition_id_format_check"),
        AuthoringSchemaRequirement.Constraint("quest_transitions_source_status_check"),
        AuthoringSchemaRequirement.Constraint("quest_transitions_target_status_check"),
        AuthoringSchemaRequirement.Trigger("quest_steps", "quest_steps_touch_definition_updated_at"),
        AuthoringSchemaRequirement.Trigger("quest_transitions", "quest_transitions_touch_definition_updated_at")
    ];
}
