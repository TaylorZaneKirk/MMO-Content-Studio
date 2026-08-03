using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record MobCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<MobDefinitionSummary> Items);

public sealed record MobDefinitionSummary(
    [property: JsonPropertyName("mob_definition_id")] string MobDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("max_health")] int MaxHealth,
    [property: JsonPropertyName("combat_faction_id")] string? CombatFactionId,
    [property: JsonPropertyName("combat_faction_display_name")] string? CombatFactionDisplayName,
    [property: JsonPropertyName("has_combat_profile")] bool HasCombatProfile,
    [property: JsonPropertyName("guaranteed_drop_count")] int GuaranteedDropCount,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MobDefinition(
    [property: JsonPropertyName("mob_definition_id")] string MobDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("max_health")] int MaxHealth,
    [property: JsonPropertyName("movement_speed_tiles_per_second")] double MovementSpeedTilesPerSecond,
    [property: JsonPropertyName("combat_faction_id")] string? CombatFactionId,
    [property: JsonPropertyName("combat_faction_display_name")] string? CombatFactionDisplayName,
    [property: JsonPropertyName("can_proactively_target_hostile_mobs")] bool CanProactivelyTargetHostileMobs,
    [property: JsonPropertyName("mob_detection_radius_tiles")] int MobDetectionRadiusTiles,
    [property: JsonPropertyName("mob_target_scan_interval_ms")] int MobTargetScanIntervalMs,
    [property: JsonPropertyName("mob_target_scan_candidate_limit")] int MobTargetScanCandidateLimit,
    [property: JsonPropertyName("primary_combat_profile")] MobCombatProfileDefinition? PrimaryCombatProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("guaranteed_drops")] IReadOnlyList<MobDropDefinition> GuaranteedDrops,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record MobCombatProfileDefinition(
    [property: JsonPropertyName("attack_type")] string AttackType,
    [property: JsonPropertyName("accuracy_style")] string? AccuracyStyle,
    [property: JsonPropertyName("minimum_range_tiles")] int MinimumRangeTiles,
    [property: JsonPropertyName("maximum_range_tiles")] int MaximumRangeTiles,
    [property: JsonPropertyName("attack_speed_units")] int AttackSpeedUnits,
    [property: JsonPropertyName("attack_level")] int AttackLevel,
    [property: JsonPropertyName("strength_level")] int StrengthLevel,
    [property: JsonPropertyName("defence_level")] int DefenceLevel);

public sealed record MobDropDefinition(
    [property: JsonPropertyName("drop_order")] int DropOrder,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("item_display_name")] string ItemDisplayName,
    [property: JsonPropertyName("stack_count")] int StackCount);

public sealed record MobDropDraft(
    [property: JsonPropertyName("drop_order")] int DropOrder,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("stack_count")] int StackCount);

public sealed record SaveMobDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("max_health")] int MaxHealth,
    [property: JsonPropertyName("movement_speed_tiles_per_second")] double MovementSpeedTilesPerSecond,
    [property: JsonPropertyName("combat_faction_id")] string? CombatFactionId,
    [property: JsonPropertyName("can_proactively_target_hostile_mobs")] bool CanProactivelyTargetHostileMobs,
    [property: JsonPropertyName("mob_detection_radius_tiles")] int MobDetectionRadiusTiles,
    [property: JsonPropertyName("mob_target_scan_interval_ms")] int MobTargetScanIntervalMs,
    [property: JsonPropertyName("mob_target_scan_candidate_limit")] int MobTargetScanCandidateLimit,
    [property: JsonPropertyName("primary_combat_profile")] MobCombatProfileDefinition? PrimaryCombatProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("guaranteed_drops")] IReadOnlyList<MobDropDraft>? GuaranteedDrops,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record MobPreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("max_health")] int MaxHealth,
    [property: JsonPropertyName("movement_speed_tiles_per_second")] double MovementSpeedTilesPerSecond,
    [property: JsonPropertyName("combat_faction_id")] string? CombatFactionId,
    [property: JsonPropertyName("can_proactively_target_hostile_mobs")] bool CanProactivelyTargetHostileMobs,
    [property: JsonPropertyName("mob_detection_radius_tiles")] int MobDetectionRadiusTiles,
    [property: JsonPropertyName("mob_target_scan_interval_ms")] int MobTargetScanIntervalMs,
    [property: JsonPropertyName("mob_target_scan_candidate_limit")] int MobTargetScanCandidateLimit,
    [property: JsonPropertyName("primary_combat_profile")] MobCombatProfileDefinition? PrimaryCombatProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("guaranteed_drops")] IReadOnlyList<MobDropDraft>? GuaranteedDrops,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record MobPublicationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record MobValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record MobMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("mob")] MobDefinition Mob,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record MobAuthoringOptionsResponse(
    [property: JsonPropertyName("publication_states")] IReadOnlyList<AuthoringOption> PublicationStates,
    [property: JsonPropertyName("attack_types")] IReadOnlyList<AuthoringOption> AttackTypes,
    [property: JsonPropertyName("accuracy_styles")] IReadOnlyList<AuthoringOption> AccuracyStyles,
    [property: JsonPropertyName("faction_dispositions")] IReadOnlyList<AuthoringOption> FactionDispositions,
    [property: JsonPropertyName("combat_bonus_fields")] IReadOnlyList<AuthoringOption> CombatBonusFields,
    [property: JsonPropertyName("defaults")] MobAuthoringDefaults Defaults);

public sealed record MobAuthoringDefaults(
    [property: JsonPropertyName("attack_type")] string AttackType,
    [property: JsonPropertyName("accuracy_style")] string AccuracyStyle,
    [property: JsonPropertyName("minimum_range_tiles")] int MinimumRangeTiles,
    [property: JsonPropertyName("maximum_range_tiles")] int MaximumRangeTiles,
    [property: JsonPropertyName("attack_speed_units")] int AttackSpeedUnits,
    [property: JsonPropertyName("attack_speed_unit_milliseconds")] int AttackSpeedUnitMilliseconds,
    [property: JsonPropertyName("movement_speed_tiles_per_second")] double MovementSpeedTilesPerSecond,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("can_proactively_target_hostile_mobs")] bool CanProactivelyTargetHostileMobs,
    [property: JsonPropertyName("mob_detection_radius_tiles")] int MobDetectionRadiusTiles,
    [property: JsonPropertyName("mob_target_scan_interval_ms")] int MobTargetScanIntervalMs,
    [property: JsonPropertyName("mob_target_scan_candidate_limit")] int MobTargetScanCandidateLimit);
