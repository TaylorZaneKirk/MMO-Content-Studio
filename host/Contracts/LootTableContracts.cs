using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record LootTableCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<LootTableSummary> Items);

public sealed record LootTableSummary(
    [property: JsonPropertyName("loot_table_id")] string LootTableId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("group_count")] int GroupCount,
    [property: JsonPropertyName("outcome_count")] int OutcomeCount,
    [property: JsonPropertyName("expected_total_reference_value")] LootExactValue ExpectedTotalReferenceValue,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record LootTableDefinition(
    [property: JsonPropertyName("loot_table_id")] string LootTableId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("content_fingerprint")] string? ContentFingerprint,
    [property: JsonPropertyName("groups")] IReadOnlyList<LootRollGroupDefinition> Groups,
    [property: JsonPropertyName("expected_value")] LootExpectedValueReport ExpectedValue,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record LootRollGroupDefinition(
    [property: JsonPropertyName("roll_group_id")] string RollGroupId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("section_kind")] string SectionKind,
    [property: JsonPropertyName("roll_kind")] string RollKind,
    [property: JsonPropertyName("roll_count")] int RollCount,
    [property: JsonPropertyName("pre_roll_failure_behavior")] string? PreRollFailureBehavior,
    [property: JsonPropertyName("pre_roll_success_sequence_behavior")] string? PreRollSuccessSequenceBehavior,
    [property: JsonPropertyName("pre_roll_success_main_behavior")] string? PreRollSuccessMainBehavior,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("outcomes")] IReadOnlyList<LootOutcomeDefinition> Outcomes);

public sealed record LootOutcomeDefinition(
    [property: JsonPropertyName("outcome_id")] string OutcomeId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("outcome_kind")] string OutcomeKind,
    [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("item_display_name")] string? ItemDisplayName,
    [property: JsonPropertyName("nested_loot_table_id")] string? NestedLootTableId,
    [property: JsonPropertyName("min_quantity")] int? MinQuantity,
    [property: JsonPropertyName("max_quantity")] int? MaxQuantity,
    [property: JsonPropertyName("weight")] int? Weight,
    [property: JsonPropertyName("probability_numerator")] long? ProbabilityNumerator,
    [property: JsonPropertyName("probability_denominator")] long? ProbabilityDenominator);

public sealed record SaveLootTableDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("groups")] IReadOnlyList<LootRollGroupDraft>? Groups,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record LootTablePreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("groups")] IReadOnlyList<LootRollGroupDraft>? Groups,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record LootRollGroupDraft(
    [property: JsonPropertyName("roll_group_id")] string RollGroupId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("section_kind")] string SectionKind,
    [property: JsonPropertyName("roll_kind")] string RollKind,
    [property: JsonPropertyName("roll_count")] int RollCount,
    [property: JsonPropertyName("pre_roll_failure_behavior")] string? PreRollFailureBehavior,
    [property: JsonPropertyName("pre_roll_success_sequence_behavior")] string? PreRollSuccessSequenceBehavior,
    [property: JsonPropertyName("pre_roll_success_main_behavior")] string? PreRollSuccessMainBehavior,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("outcomes")] IReadOnlyList<LootOutcomeDraft>? Outcomes);

public sealed record LootOutcomeDraft(
    [property: JsonPropertyName("outcome_id")] string OutcomeId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("outcome_kind")] string OutcomeKind,
    [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("nested_loot_table_id")] string? NestedLootTableId,
    [property: JsonPropertyName("min_quantity")] int? MinQuantity,
    [property: JsonPropertyName("max_quantity")] int? MaxQuantity,
    [property: JsonPropertyName("weight")] int? Weight,
    [property: JsonPropertyName("probability_numerator")] long? ProbabilityNumerator,
    [property: JsonPropertyName("probability_denominator")] long? ProbabilityDenominator);

public sealed record LootTablePublicationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record LootTableValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<AuthoringChange> Changes,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("expected_value")] LootExpectedValueReport ExpectedValue);

public sealed record LootTableMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("loot_table")] LootTableDefinition LootTable,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record LootTableAuthoringOptionsResponse(
    [property: JsonPropertyName("publication_states")] IReadOnlyList<AuthoringOption> PublicationStates,
    [property: JsonPropertyName("section_kinds")] IReadOnlyList<AuthoringOption> SectionKinds,
    [property: JsonPropertyName("roll_kinds")] IReadOnlyList<AuthoringOption> RollKinds,
    [property: JsonPropertyName("outcome_kinds")] IReadOnlyList<AuthoringOption> OutcomeKinds,
    [property: JsonPropertyName("pre_roll_failure_behaviors")] IReadOnlyList<AuthoringOption> PreRollFailureBehaviors,
    [property: JsonPropertyName("pre_roll_success_sequence_behaviors")] IReadOnlyList<AuthoringOption> PreRollSuccessSequenceBehaviors,
    [property: JsonPropertyName("pre_roll_success_main_behaviors")] IReadOnlyList<AuthoringOption> PreRollSuccessMainBehaviors,
    [property: JsonPropertyName("item_options")] IReadOnlyList<LootItemOption> ItemOptions,
    [property: JsonPropertyName("loot_table_options")] IReadOnlyList<LootTableOption> LootTableOptions,
    [property: JsonPropertyName("supported_limits")] LootTableSupportedLimits SupportedLimits);

public sealed record LootItemOption(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("runtime_enabled")] bool RuntimeEnabled,
    [property: JsonPropertyName("reference_value")] long ReferenceValue);

public sealed record LootTableOption(
    [property: JsonPropertyName("loot_table_id")] string LootTableId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record LootTableSupportedLimits(
    [property: JsonPropertyName("max_nesting_depth")] int MaxNestingDepth,
    [property: JsonPropertyName("max_bounded_expansion")] int MaxBoundedExpansion);

public sealed record LootExpectedValueReport(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("total_reference_value")] LootExactValue TotalReferenceValue,
    [property: JsonPropertyName("item_totals")] IReadOnlyList<LootExpectedItemTotal> ItemTotals,
    [property: JsonPropertyName("section_totals")] IReadOnlyList<LootExpectedSectionTotal> SectionTotals,
    [property: JsonPropertyName("path_contributions")] IReadOnlyList<LootExpectedPathContribution> PathContributions,
    [property: JsonPropertyName("no_drop_probability")] LootExactValue NoDropProbability,
    [property: JsonPropertyName("currency_injection_configured")] bool CurrencyInjectionConfigured,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<ApiError> Diagnostics);

public sealed record LootExpectedItemTotal(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("expected_quantity")] LootExactValue ExpectedQuantity,
    [property: JsonPropertyName("expected_reference_value")] LootExactValue ExpectedReferenceValue,
    [property: JsonPropertyName("effective_probability")] LootExactValue EffectiveProbability,
    [property: JsonPropertyName("reference_value")] long ReferenceValue,
    [property: JsonPropertyName("zero_reference_value")] bool ZeroReferenceValue);

public sealed record LootExpectedSectionTotal(
    [property: JsonPropertyName("section_kind")] string SectionKind,
    [property: JsonPropertyName("expected_reference_value")] LootExactValue ExpectedReferenceValue);

public sealed record LootExpectedPathContribution(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("section_kind")] string SectionKind,
    [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("expected_quantity")] LootExactValue ExpectedQuantity,
    [property: JsonPropertyName("expected_reference_value")] LootExactValue ExpectedReferenceValue,
    [property: JsonPropertyName("effective_probability")] LootExactValue EffectiveProbability);

public sealed record LootExactValue(
    [property: JsonPropertyName("numerator")] string Numerator,
    [property: JsonPropertyName("denominator")] string Denominator,
    [property: JsonPropertyName("decimal_value")] decimal DecimalValue,
    [property: JsonPropertyName("display")] string Display);
