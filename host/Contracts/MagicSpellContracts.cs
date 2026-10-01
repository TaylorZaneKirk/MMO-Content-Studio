// Defines scalar combat-spell drafts and the preview/publication API payloads.
using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

// Scalar ordinary combat-spell content and its authoring lifecycle payloads.
public sealed record MagicSpellDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("tier")] int Tier,
    [property: JsonPropertyName("element")] string Element,
    [property: JsonPropertyName("required_magic_level")] int RequiredMagicLevel,
    [property: JsonPropertyName("shard_cost")] int ShardCost,
    [property: JsonPropertyName("successful_hit_min_damage")] int SuccessfulHitMinDamage,
    [property: JsonPropertyName("base_max_hit")] int BaseMaxHit,
    [property: JsonPropertyName("base_cast_xp_tenths")] int BaseCastXpTenths,
    [property: JsonPropertyName("icon_texture_path")] string? IconTexturePath = null,
    [property: JsonPropertyName("projectile_animation_fps")] double? ProjectileAnimationFps = null,
    [property: JsonPropertyName("projectile_render_scale")] double? ProjectileRenderScale = null,
    [property: JsonPropertyName("projectile_rotates_to_travel")] bool ProjectileRotatesToTravel = true,
    [property: JsonPropertyName("cast_sound_path")] string? CastSoundPath = null,
    [property: JsonPropertyName("impact_animation_fps")] double? ImpactAnimationFps = null,
    [property: JsonPropertyName("impact_render_scale")] double? ImpactRenderScale = null,
    [property: JsonPropertyName("impact_sound_path")] string? ImpactSoundPath = null,
    [property: JsonPropertyName("splash_animation_fps")] double? SplashAnimationFps = null,
    [property: JsonPropertyName("splash_render_scale")] double? SplashRenderScale = null,
    [property: JsonPropertyName("splash_sound_path")] string? SplashSoundPath = null,
    [property: JsonPropertyName("projectile_frames")] IReadOnlyList<string>? ProjectileFrames = null,
    [property: JsonPropertyName("impact_frames")] IReadOnlyList<string>? ImpactFrames = null,
    [property: JsonPropertyName("splash_frames")] IReadOnlyList<string>? SplashFrames = null,
    [property: JsonPropertyName("projectile_source_facing")] string ProjectileSourceFacing = "right",
    [property: JsonPropertyName("projectile_homing_enabled")] bool ProjectileHomingEnabled = false,
    [property: JsonPropertyName("projectile_homing_strength")] double ProjectileHomingStrength = 1.0,
    [property: JsonPropertyName("cast_mode")] string CastMode = "selected_combat",
    [property: JsonPropertyName("impact_effect")] string? ImpactEffect = null,
    [property: JsonPropertyName("force")] int? Force = null,
    [property: JsonPropertyName("force_falloff_per_tile")] int? ForceFalloffPerTile = null,
    [property: JsonPropertyName("max_displacement_tiles")] int? MaxDisplacementTiles = null,
    [property: JsonPropertyName("target_mode")] string TargetMode = "mob",
    [property: JsonPropertyName("manifestation_base_success_percent")] int? ManifestationBaseSuccessPercent = null,
    [property: JsonPropertyName("manifestation_magic_levels_per_step")] int? ManifestationMagicLevelsPerStep = null,
    [property: JsonPropertyName("manifestation_success_percent_per_step")] int? ManifestationSuccessPercentPerStep = null,
    [property: JsonPropertyName("matter_lifetime_milliseconds")] int? MatterLifetimeMilliseconds = null,
    [property: JsonPropertyName("matter_capacity_magic_levels_per_step")] int? MatterCapacityMagicLevelsPerStep = null,
    [property: JsonPropertyName("matter_max_active")] int? MatterMaxActive = null,
    [property: JsonPropertyName("matter_visual_texture_path")] string? MatterVisualTexturePath = null,
    [property: JsonPropertyName("matter_visual_render_scale")] double? MatterVisualRenderScale = null,
    [property: JsonPropertyName("force_mastery_magic_levels_per_step")] int? ForceMasteryMagicLevelsPerStep = null,
    [property: JsonPropertyName("force_mastery_force_per_step")] int? ForceMasteryForcePerStep = null,
    [property: JsonPropertyName("force_mastery_max_force")] int? ForceMasteryMaxForce = null,
    [property: JsonPropertyName("displacement_mastery_magic_levels_per_step")] int? DisplacementMasteryMagicLevelsPerStep = null,
    [property: JsonPropertyName("displacement_mastery_tiles_per_step")] int? DisplacementMasteryTilesPerStep = null,
    [property: JsonPropertyName("displacement_mastery_max_tiles")] int? DisplacementMasteryMaxTiles = null,
    [property: JsonPropertyName("matter_physical_weight")] int? MatterPhysicalWeight = null);
public sealed record MagicSpellDefinition(
    [property: JsonPropertyName("spell_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] MagicSpellDraft Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record MagicSpellSummary(
    [property: JsonPropertyName("spell_id")] string DefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record MagicSpellCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MagicSpellSummary> Items);

public sealed record MagicSpellRequest(
    [property: JsonPropertyName("draft")] MagicSpellDraft Draft,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("target_operation")] string? TargetOperation);

public sealed record MagicSpellPreview(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<MagicSpellChange> Changes);

public sealed record MagicSpellChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] object? Before,
    [property: JsonPropertyName("after")] object? After);

public sealed record MagicSpellMutation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("definition")] MagicSpellDefinition? Definition,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record MagicSpellOptions(
    [property: JsonPropertyName("game_assets_root")] string? GameAssetsRoot);
