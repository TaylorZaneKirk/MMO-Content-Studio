// Owns combat-spell validation, preview and lifecycle decisions. The repository
// atomically persists content; no casting or gameplay state lives here.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class MagicSpellAuthoringService(
    MagicSpellRepository repository, ItemAssetService assets,
    ILogger<MagicSpellAuthoringService> logger)
{
    public Task<AuthoringOperationResult<MagicSpellCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<MagicSpellCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<MagicSpellDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<MagicSpellDefinition>.Success(definition)
            : AuthoringOperationResult<MagicSpellDefinition>.Failure(Error("spell_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<MagicSpellPreview>> PreviewAsync(string definitionId,
        MagicSpellRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        var changes = new List<MagicSpellChange>();
        if (request.Draft is not null)
        {
            var before = existing is null ? default : JsonSerializer.SerializeToElement(existing.Draft);
            foreach (var field in JsonSerializer.SerializeToElement(request.Draft).EnumerateObject())
            {
                JsonElement previous = default;
                if (before.ValueKind == JsonValueKind.Object) before.TryGetProperty(field.Name, out previous);
                if (previous.ValueKind == JsonValueKind.Undefined || previous.GetRawText() != field.Value.GetRawText())
                    changes.Add(new(field.Name, previous.ValueKind == JsonValueKind.Undefined ? null : previous.Clone(), field.Value.Clone()));
            }
        }
        var nextState = operation switch { "publish" or "save_and_publish" => "Published", "disable" => "Disabled", "delete" => null, _ => "Draft" };
        if (existing?.PublicationState != nextState) changes.Add(new("publication_state", existing?.PublicationState, nextState));
        return AuthoringOperationResult<MagicSpellPreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify the committed authoring result.
    public Task<AuthoringOperationResult<MagicSpellMutation>> MutateAsync(string definitionId, string operation,
        MagicSpellRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<MagicSpellMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("MagicSpell mutation failed reload-and-verify.");
        return AuthoringOperationResult<MagicSpellMutation>.Success(new(operation, verified, messages));
    });

    private List<ApiError> ValidateOperation(string definitionId, string operation, MagicSpellRequest request,
        MagicSpellDefinition? existing)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "save_and_publish" or "publish" or "disable" or "delete"))
            messages.Add(Error("invalid_operation", "Choose Save Draft, Save & Publish, Publish, Disable or Delete."));
        if (!StableId(definitionId)) messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("spell_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation is not ("save_draft" or "save_and_publish"))
            messages.Add(Error("spell_not_found", "Save a draft first."));
        if (operation is not ("save_draft" or "save_and_publish") && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_spell_changes", "Save edited fields as a draft before this operation."));
        if (operation == "delete" && existing?.PublicationState != "Disabled")
            messages.Add(Error("spell_still_published", "Disable the definition before deleting it."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (string.IsNullOrWhiteSpace(draft.DisplayName)) messages.Add(Error("missing_name", "Display name is required.", "display_name"));
        if (draft.Tier is < 1 or > 4) messages.Add(Error("invalid_tier", "Tier must be 1 through 4.", "tier"));
        if (draft.Element is not ("air" or "earth" or "fire" or "water"))
            messages.Add(Error("invalid_element", "Choose Air, Earth, Fire or Water.", "element"));
        if (draft.RequiredMagicLevel is < 1 or > 99)
            messages.Add(Error("invalid_magic_level", "Required Magic level must be 1 through 99.", "required_magic_level"));
        if (draft.ShardCost <= 0) messages.Add(Error("invalid_shard_cost", "Shard cost must be positive.", "shard_cost"));
        if (draft.SuccessfulHitMinDamage < 0 || draft.BaseMaxHit < draft.SuccessfulHitMinDamage)
            messages.Add(Error("invalid_damage_band", "Damage must satisfy 0 <= minimum <= maximum.", "base_max_hit"));
        if (draft.BaseCastXpTenths < 0)
            messages.Add(Error("invalid_cast_xp", "Base cast XP tenths must be nonnegative.", "base_cast_xp_tenths"));
        if (draft.CastMode is not ("selected_combat" or "explicit_technique"))
            messages.Add(Error("invalid_cast_mode", "Choose Selected combat or Explicit technique.", "cast_mode"));
        // Optional as a group, including for fixed-force Air drafts and Published spells.
        var noAirMastery = draft.ForceMasteryMagicLevelsPerStep is null &&
            draft.ForceMasteryForcePerStep is null &&
            draft.ForceMasteryMaxForce is null &&
            draft.DisplacementMasteryMagicLevelsPerStep is null &&
            draft.DisplacementMasteryTilesPerStep is null &&
            draft.DisplacementMasteryMaxTiles is null;
        var validAirMastery = draft.ImpactEffect == "air_displacement" &&
            draft.ForceMasteryMagicLevelsPerStep is > 0 &&
            draft.ForceMasteryForcePerStep is > 0 &&
            draft.ForceMasteryMaxForce is > 0 &&
            draft.DisplacementMasteryMagicLevelsPerStep is > 0 &&
            draft.DisplacementMasteryTilesPerStep is > 0 &&
            draft.DisplacementMasteryMaxTiles is > 0 &&
            draft.ForceMasteryMaxForce >= draft.Force &&
            draft.DisplacementMasteryMaxTiles >= draft.MaxDisplacementTiles;
        if (!noAirMastery && !validAirMastery)
            messages.Add(Error("invalid_air_mastery_shape",
                "Air mastery requires all six positive values, with mastered limits at least their base values. Other effects require empty mastery fields.",
                "impact_effect"));
        var forceShape = draft.ImpactEffect switch
        {
            null or "earth_matter" or "burning_terrain" or "slippery_terrain" => draft.Force is null && draft.ForceFalloffPerTile is null && draft.MaxDisplacementTiles is null,
            "air_displacement" => draft.Force is > 0 && draft.ForceFalloffPerTile is >= 0 && draft.MaxDisplacementTiles is > 0,
            _ => false
        };
        if (!forceShape)
            messages.Add(Error("invalid_force_shape", "Air displacement requires positive force and maximum tiles, and nonnegative falloff; None requires empty force fields.", "impact_effect"));
        if (draft.TargetMode is not ("mob" or "tile" or "physical"))
            messages.Add(Error("invalid_target_mode", "Choose Mob, Tile or Physical.", "target_mode"));
        var noMatter = draft.ManifestationBaseSuccessPercent is null && draft.ManifestationMagicLevelsPerStep is null &&
            draft.ManifestationSuccessPercentPerStep is null && draft.MatterLifetimeMilliseconds is null &&
            draft.MatterCapacityMagicLevelsPerStep is null && draft.MatterMaxActive is null &&
            draft.MatterPhysicalWeight is null && draft.MatterVisualTexturePath is null && draft.MatterVisualRenderScale is null;
        if (draft.ImpactEffect != "earth_matter" && !noMatter)
            messages.Add(Error("invalid_matter_shape", "Only Earth matter uses manifestation, capacity, physical weight and matter visuals.", "impact_effect"));
        if (draft.ManifestationBaseSuccessPercent is < 0 or > 100 ||
            draft.ManifestationMagicLevelsPerStep is <= 0 || draft.ManifestationSuccessPercentPerStep is < 0 or > 100 ||
            draft.MatterLifetimeMilliseconds is <= 0 || draft.MatterCapacityMagicLevelsPerStep is <= 0 ||
            draft.MatterPhysicalWeight is <= 0 || draft.MatterMaxActive is <= 0 || draft.MatterVisualRenderScale is { } scale && (!double.IsFinite(scale) || scale <= 0))
            messages.Add(Error("invalid_matter_number", "Percentages must be 0–100; step sizes, lifetime, capacity, weight and scale must be positive.", "impact_effect"));
        var noFire = draft.IgnitionBaseSuccessPercent is null && draft.IgnitionMagicLevelsPerStep is null &&
            draft.IgnitionSuccessPercentPerStep is null && draft.BurningLifetimeMilliseconds is null &&
            draft.BurningCapacityMagicLevelsPerStep is null && draft.BurningMaxActive is null &&
            draft.BurningMinDamage is null && draft.BurningMaxDamage is null &&
            draft.BurningHazardCooldownMilliseconds is null && (draft.BurningVisualFrames is null or { Count: 0 }) &&
            draft.BurningVisualAnimationFps is null &&
            draft.BurningVisualRenderScale is null;
        if (draft.ImpactEffect != "burning_terrain" && !noFire)
            messages.Add(Error("invalid_fire_shape", "Only burning terrain uses Fire ignition and hazard fields.", "impact_effect"));
        if (draft.IgnitionBaseSuccessPercent is < 0 or > 100 || draft.IgnitionMagicLevelsPerStep is <= 0 ||
            draft.IgnitionSuccessPercentPerStep is < 0 or > 100 || draft.BurningLifetimeMilliseconds is <= 0 ||
            draft.BurningCapacityMagicLevelsPerStep is <= 0 || draft.BurningMaxActive is <= 0 ||
            draft.BurningMinDamage is <= 0 || draft.BurningMaxDamage is <= 0 ||
            draft.BurningHazardCooldownMilliseconds is <= 0 ||
            draft.BurningMinDamage > draft.BurningMaxDamage ||
            draft.BurningVisualRenderScale is { } fireScale && (!double.IsFinite(fireScale) || fireScale <= 0) ||
            draft.BurningVisualAnimationFps is { } fireFps && (!double.IsFinite(fireFps) || fireFps <= 0))
            messages.Add(Error("invalid_fire_number", "Fire percentages must be 0–100; other Fire values must be positive and maximum damage must cover minimum damage.", "impact_effect"));
        var noSlick = draft.SlickBaseSuccessPercent is null && draft.SlickMagicLevelsPerStep is null &&
            draft.SlickSuccessPercentPerStep is null && draft.SlickLifetimeMilliseconds is null &&
            draft.SlickCapacityMagicLevelsPerStep is null && draft.SlickMaxActive is null &&
            (draft.SlickVisualFrames is null or { Count: 0 }) && draft.SlickVisualAnimationFps is null &&
            draft.SlickVisualRenderScale is null;
        if (draft.ImpactEffect != "slippery_terrain" && !noSlick)
            messages.Add(Error("invalid_slick_shape", "Only slippery terrain uses Water manifestation and slick fields.", "impact_effect"));
        if (draft.SlickBaseSuccessPercent is < 0 or > 100 || draft.SlickMagicLevelsPerStep is <= 0 ||
            draft.SlickSuccessPercentPerStep is < 0 or > 100 || draft.SlickLifetimeMilliseconds is <= 0 ||
            draft.SlickCapacityMagicLevelsPerStep is <= 0 || draft.SlickMaxActive is <= 0 ||
            draft.SlickVisualRenderScale is { } slickScale && (!double.IsFinite(slickScale) || slickScale <= 0) ||
            draft.SlickVisualAnimationFps is { } slickFps && (!double.IsFinite(slickFps) || slickFps <= 0))
            messages.Add(Error("invalid_slick_number", "Water percentages must be 0–100; other Water values must be positive.", "impact_effect"));
        if (operation is "publish" or "save_and_publish")
        {
            var supported = draft.CastMode == "selected_combat" && draft.TargetMode == "mob" && draft.ImpactEffect is null && noMatter && noFire ||
                draft.CastMode == "explicit_technique" && draft.TargetMode == "physical" && draft.Element == "air" &&
                draft.ImpactEffect == "air_displacement" && draft.SuccessfulHitMinDamage == 0 && draft.BaseMaxHit == 0 && noMatter && noFire ||
                draft.CastMode == "explicit_technique" && draft.TargetMode == "tile" && draft.Element == "earth" &&
                draft.ImpactEffect == "earth_matter" && draft.SuccessfulHitMinDamage == 0 && draft.BaseMaxHit == 0 &&
                draft.ManifestationBaseSuccessPercent is not null && draft.ManifestationMagicLevelsPerStep is not null &&
                draft.ManifestationSuccessPercentPerStep is not null && draft.MatterLifetimeMilliseconds is not null &&
                draft.MatterPhysicalWeight is > 0 && draft.MatterCapacityMagicLevelsPerStep is not null && draft.MatterMaxActive is not null &&
                draft.MatterVisualTexturePath is not null && draft.MatterVisualRenderScale is not null && noFire ||
                draft.CastMode == "explicit_technique" && draft.TargetMode == "tile" && draft.Element == "fire" &&
                draft.ImpactEffect == "burning_terrain" && draft.SuccessfulHitMinDamage == 0 && draft.BaseMaxHit == 0 &&
                noMatter && noAirMastery && draft.IgnitionBaseSuccessPercent is not null &&
                draft.IgnitionMagicLevelsPerStep is not null && draft.IgnitionSuccessPercentPerStep is not null &&
                draft.BurningLifetimeMilliseconds is not null && draft.BurningCapacityMagicLevelsPerStep is not null &&
                draft.BurningMaxActive is not null && draft.BurningMinDamage is not null && draft.BurningMaxDamage is not null &&
                draft.BurningHazardCooldownMilliseconds is not null && draft.BurningVisualFrames is { Count: >= 2 } &&
                draft.BurningVisualAnimationFps is not null &&
                draft.BurningVisualRenderScale is not null ||
                draft.CastMode == "explicit_technique" && draft.TargetMode == "tile" && draft.Element == "water" &&
                draft.ImpactEffect == "slippery_terrain" && draft.SuccessfulHitMinDamage == 0 && draft.BaseMaxHit == 0 &&
                noMatter && noFire && noAirMastery && draft.SlickBaseSuccessPercent is not null &&
                draft.SlickMagicLevelsPerStep is not null && draft.SlickSuccessPercentPerStep is not null &&
                draft.SlickLifetimeMilliseconds is not null && draft.SlickCapacityMagicLevelsPerStep is not null &&
                draft.SlickMaxActive is not null && draft.SlickVisualFrames is { Count: >= 1 } &&
                draft.SlickVisualAnimationFps is not null && draft.SlickVisualRenderScale is not null;
            if (!supported)
                messages.Add(Error("unsupported_spell_shape", "Publish selected Mob combat, a zero-damage physical Air technique, or a complete zero-damage tile Earth, Fire or Water technique.", "cast_mode"));
        }
        ValidatePresentation(draft, operation, messages);
        return messages;
    }

    private void ValidatePresentation(MagicSpellDraft draft, string operation, List<ApiError> messages)
    {
        var publishing = operation is "publish" or "save_and_publish";
        if (draft.ProjectileSourceFacing is not ("right" or "down" or "left" or "up"))
            messages.Add(Error("invalid_projectile_source_facing", "Choose Right, Down, Left or Up.", "projectile_source_facing"));
        if (!double.IsFinite(draft.ProjectileHomingStrength) || draft.ProjectileHomingStrength is <= 0 or > 1)
            messages.Add(Error("invalid_projectile_homing_strength", "Homing strength must be greater than 0 and at most 1.", "projectile_homing_strength"));
        CheckPath(draft.MatterVisualTexturePath, "matter_visual_texture_path", false);
        if (draft.BurningVisualFrames is { Count: > 64 })
            messages.Add(Error("too_many_fire_frames", "Persistent Fire supports at most 64 ordered frames.", "burning_visual_frames"));
        foreach (var path in draft.BurningVisualFrames ?? [])
        {
            if (path is null) messages.Add(Error("invalid_asset_path", "A Fire frame path is required.", "burning_visual_frames"));
            else CheckPath(path, "burning_visual_frames", false);
        }
        if (draft.SlickVisualFrames is { Count: > 64 })
            messages.Add(Error("too_many_slick_frames", "Persistent Water supports at most 64 ordered frames.", "slick_visual_frames"));
        foreach (var path in draft.SlickVisualFrames ?? [])
        {
            if (path is null) messages.Add(Error("invalid_asset_path", "A Water frame path is required.", "slick_visual_frames"));
            else CheckPath(path, "slick_visual_frames", false);
        }
        CheckPath(draft.IconTexturePath, "icon_texture_path", false);
        CheckPath(draft.CastSoundPath, "cast_sound_path", true);
        CheckPath(draft.ImpactSoundPath, "impact_sound_path", true);
        CheckPath(draft.SplashSoundPath, "splash_sound_path", true);
        CheckPhase("projectile", draft.ProjectileFrames, draft.ProjectileAnimationFps, draft.ProjectileRenderScale);
        CheckPhase("impact", draft.ImpactFrames, draft.ImpactAnimationFps, draft.ImpactRenderScale);
        CheckPhase("splash", draft.SplashFrames, draft.SplashAnimationFps, draft.SplashRenderScale);

        void CheckPhase(string phase, IReadOnlyList<string>? frames, double? fps, double? scale)
        {
            if ((fps is { } f && (!double.IsFinite(f) || f <= 0)) ||
                (scale is { } s && (!double.IsFinite(s) || s <= 0)))
                messages.Add(Error("invalid_fx_number", "FPS and scale must be finite positive values or empty.", phase));
            if ((frames?.Count > 0 && scale is null) || (frames?.Count > 1 && fps is null))
                messages.Add(Error("incomplete_fx_phase", "Frames require scale; multiple frames also require FPS.", phase));
            foreach (var path in frames ?? [])
            {
                if (path is null) messages.Add(Error("invalid_asset_path", "A frame path is required.", phase));
                else CheckPath(path, phase + "_frames", false);
            }
        }

        void CheckPath(string? path, string field, bool audio)
        {
            if (path is null) return;
            // Match existing game-asset conventions before resolving against the
            // shared configured root. Audio uses the same root as PNG previews.
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/", StringComparison.Ordinal) ||
                path.Contains('\\') || path["res://".Length..].Contains(':') ||
                path["res://assets/".Length..].Split('/').Any(segment => segment is ".." or "." or ""))
            {
                messages.Add(Error("invalid_asset_path", "Use a canonical res://assets/ path without traversal.", field));
                return;
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (audio ? extension is not (".wav" or ".ogg" or ".mp3") : extension != ".png")
            {
                messages.Add(Error("invalid_asset_type", audio ? "Use WAV, OGG or MP3 audio." : "Use PNG frames/icons.", field));
                return;
            }
            var root = assets.GetGameAssetsRoot();
            var exists = audio ? root is not null && File.Exists(Path.Combine(root, path["res://assets/".Length..]))
                : assets.ResolveGameAssetPng(path, "Spell image").Exists;
            if (!exists)
                messages.Add(new("spell_asset_unavailable", "Asset does not exist or the game asset root is unavailable.",
                    publishing ? ValidationSeverity.Error : ValidationSeverity.Warning, field));
        }
    }

    private static bool StableId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9]*(_[a-z0-9]+)*$");
    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, MagicSpellRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (MagicSpellConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("spell_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("spell_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState is "P0001" or "23503" or "23514")
        { return AuthoringOperationResult<T>.Failure(Error("spell_dependency_or_validation", error.MessageText)); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "MagicSpell database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "MagicSpell database operation failed. Check schema health and host logs."));
        }
    }
}
