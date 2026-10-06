// Persists spell scalars and ordered presentation frames. A row lock and expected updated_at protect
// author edits; the primary key rejects concurrent creation of the same identity.
using System.Data;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class MagicSpellRepository(AuthoringDatabaseConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<MagicSpellSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT spell_id, display_name, publication_state FROM magic_combat_spells
            WHERE @search = '' OR spell_id ILIKE '%' || @search || '%' OR display_name ILIKE '%' || @search || '%'
            ORDER BY display_name, spell_id
            """, connection);
        command.Parameters.AddWithValue("search", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<MagicSpellSummary>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return items;
    }

    public async Task<MagicSpellDefinition?> LoadAsync(string definitionId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
    }

    private static async Task<MagicSpellDefinition?> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string definitionId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT display_name, tier, element, required_magic_level, shard_cost,
                successful_hit_min_damage, base_max_hit, base_cast_xp_tenths, publication_state, updated_at,
                icon_texture_path, projectile_animation_fps, projectile_render_scale, projectile_rotates_to_travel,
                cast_sound_path, impact_animation_fps, impact_render_scale, impact_sound_path, splash_animation_fps,
                splash_render_scale, splash_sound_path, projectile_source_facing,
                projectile_homing_enabled, projectile_homing_strength,
                cast_mode, impact_effect, force, force_falloff_per_tile, max_displacement_tiles,
                target_mode, manifestation_base_success_percent, manifestation_magic_levels_per_step,
                manifestation_success_percent_per_step, matter_lifetime_milliseconds,
                matter_capacity_magic_levels_per_step, matter_max_active, matter_visual_texture_path, matter_visual_render_scale,
                force_mastery_magic_levels_per_step, force_mastery_force_per_step, force_mastery_max_force, displacement_mastery_magic_levels_per_step, displacement_mastery_tiles_per_step, displacement_mastery_max_tiles, matter_physical_weight,
                ignition_base_success_percent, ignition_magic_levels_per_step, ignition_success_percent_per_step, burning_lifetime_milliseconds, burning_capacity_magic_levels_per_step, burning_max_active, burning_min_damage, burning_max_damage, burning_hazard_cooldown_milliseconds, burning_visual_frames, burning_visual_animation_fps, burning_visual_render_scale,
                slick_base_success_percent, slick_magic_levels_per_step, slick_success_percent_per_step, slick_lifetime_milliseconds, slick_capacity_magic_levels_per_step, slick_max_active, slick_visual_frames, slick_visual_animation_fps, slick_visual_render_scale
            FROM magic_combat_spells WHERE spell_id = @id
            """ + (forUpdate ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", definitionId);
        MagicSpellDraft draft;
        string state;
        DateTimeOffset updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            draft = new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12),
                reader.GetBoolean(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetDouble(15),
                reader.IsDBNull(16) ? null : reader.GetDouble(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetDouble(18),
                reader.IsDBNull(19) ? null : reader.GetDouble(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                ProjectileSourceFacing: reader.GetString(21),
                ProjectileHomingEnabled: reader.GetBoolean(22),
                ProjectileHomingStrength: reader.GetDouble(23),
                CastMode: reader.GetString(24),
                ImpactEffect: reader.IsDBNull(25) ? null : reader.GetString(25),
                Force: reader.IsDBNull(26) ? null : reader.GetInt32(26),
                ForceFalloffPerTile: reader.IsDBNull(27) ? null : reader.GetInt32(27),
                MaxDisplacementTiles: reader.IsDBNull(28) ? null : reader.GetInt32(28),
                TargetMode: reader.GetString(29),
                ManifestationBaseSuccessPercent: reader.IsDBNull(30) ? null : reader.GetInt32(30),
                ManifestationMagicLevelsPerStep: reader.IsDBNull(31) ? null : reader.GetInt32(31),
                ManifestationSuccessPercentPerStep: reader.IsDBNull(32) ? null : reader.GetInt32(32),
                MatterLifetimeMilliseconds: reader.IsDBNull(33) ? null : reader.GetInt32(33),
                MatterCapacityMagicLevelsPerStep: reader.IsDBNull(34) ? null : reader.GetInt32(34),
                MatterMaxActive: reader.IsDBNull(35) ? null : reader.GetInt32(35),
                MatterVisualTexturePath: reader.IsDBNull(36) ? null : reader.GetString(36),
                MatterVisualRenderScale: reader.IsDBNull(37) ? null : reader.GetDouble(37),
                ForceMasteryMagicLevelsPerStep: reader.IsDBNull(38) ? null : reader.GetInt32(38),
                ForceMasteryForcePerStep: reader.IsDBNull(39) ? null : reader.GetInt32(39),
                ForceMasteryMaxForce: reader.IsDBNull(40) ? null : reader.GetInt32(40),
                DisplacementMasteryMagicLevelsPerStep: reader.IsDBNull(41) ? null : reader.GetInt32(41),
                DisplacementMasteryTilesPerStep: reader.IsDBNull(42) ? null : reader.GetInt32(42),
                DisplacementMasteryMaxTiles: reader.IsDBNull(43) ? null : reader.GetInt32(43),
                MatterPhysicalWeight: reader.IsDBNull(44) ? null : reader.GetInt32(44),
                IgnitionBaseSuccessPercent: reader.IsDBNull(45) ? null : reader.GetInt32(45),
                IgnitionMagicLevelsPerStep: reader.IsDBNull(46) ? null : reader.GetInt32(46),
                IgnitionSuccessPercentPerStep: reader.IsDBNull(47) ? null : reader.GetInt32(47),
                BurningLifetimeMilliseconds: reader.IsDBNull(48) ? null : reader.GetInt32(48),
                BurningCapacityMagicLevelsPerStep: reader.IsDBNull(49) ? null : reader.GetInt32(49),
                BurningMaxActive: reader.IsDBNull(50) ? null : reader.GetInt32(50),
                BurningMinDamage: reader.IsDBNull(51) ? null : reader.GetInt32(51),
                BurningMaxDamage: reader.IsDBNull(52) ? null : reader.GetInt32(52),
                BurningHazardCooldownMilliseconds: reader.IsDBNull(53) ? null : reader.GetInt32(53),
                BurningVisualFrames: reader.IsDBNull(54) ? null : Array.AsReadOnly(reader.GetFieldValue<string[]>(54)),
                BurningVisualAnimationFps: reader.IsDBNull(55) ? null : reader.GetDouble(55),
                BurningVisualRenderScale: reader.IsDBNull(56) ? null : reader.GetDouble(56),
                SlickBaseSuccessPercent: reader.IsDBNull(57) ? null : reader.GetInt32(57),
                SlickMagicLevelsPerStep: reader.IsDBNull(58) ? null : reader.GetInt32(58),
                SlickSuccessPercentPerStep: reader.IsDBNull(59) ? null : reader.GetInt32(59),
                SlickLifetimeMilliseconds: reader.IsDBNull(60) ? null : reader.GetInt32(60),
                SlickCapacityMagicLevelsPerStep: reader.IsDBNull(61) ? null : reader.GetInt32(61),
                SlickMaxActive: reader.IsDBNull(62) ? null : reader.GetInt32(62),
                SlickVisualFrames: reader.IsDBNull(63) ? null : Array.AsReadOnly(reader.GetFieldValue<string[]>(63)),
                SlickVisualAnimationFps: reader.IsDBNull(64) ? null : reader.GetDouble(64),
                SlickVisualRenderScale: reader.IsDBNull(65) ? null : reader.GetDouble(65));
            state = reader.GetString(8);
            updated = reader.GetFieldValue<DateTimeOffset>(9);
        }
        var frames = new Dictionary<string, List<string>>
        { ["projectile"] = [], ["impact"] = [], ["splash"] = [] };
        await using var frameCommand = new NpgsqlCommand("""
            SELECT phase, texture_path FROM magic_spell_presentation_frames
            WHERE spell_id=@id ORDER BY phase, frame_order;
            """, connection, transaction);
        frameCommand.Parameters.AddWithValue("id", definitionId);
        await using var frameReader = await frameCommand.ExecuteReaderAsync(cancellationToken);
        while (await frameReader.ReadAsync(cancellationToken))
            frames[frameReader.GetString(0)].Add(frameReader.GetString(1));
        draft = draft with { ProjectileFrames = frames["projectile"].AsReadOnly(),
            ImpactFrames = frames["impact"].AsReadOnly(), SplashFrames = frames["splash"].AsReadOnly() };
        return new(definitionId, state, draft, updated);
    }

    // Lock the current definition, reject a stale editor, then save all scalar
    // fields, ordered frames and publication state together. Reload inside the same transaction.
    public async Task<MagicSpellDefinition?> MutateAsync(string definitionId, string operation,
        MagicSpellDraft draft, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, definitionId, true, cancellationToken);
        if (existing?.UpdatedAtUtc != expectedUpdatedAtUtc || (existing is null && operation is not ("save_draft" or "save_and_publish")))
            throw new MagicSpellConcurrencyException();
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        command.Parameters.AddWithValue("id", definitionId);
        if (operation == "delete")
        {
            if (existing!.PublicationState != "Disabled") throw new MagicSpellConcurrencyException();
            command.CommandText = "DELETE FROM magic_combat_spells WHERE spell_id=@id";
        }
        else if (operation is "save_draft" or "save_and_publish")
        {
            command.CommandText = existing is null ? """
                INSERT INTO magic_combat_spells (spell_id, display_name, tier, element,
                    required_magic_level, shard_cost, successful_hit_min_damage, base_max_hit,
                    base_cast_xp_tenths, publication_state, icon_texture_path, projectile_animation_fps,
                    projectile_render_scale, projectile_rotates_to_travel, cast_sound_path, impact_animation_fps,
                    impact_render_scale, impact_sound_path, splash_animation_fps, splash_render_scale,
                    splash_sound_path, projectile_source_facing, projectile_homing_enabled,
                    projectile_homing_strength, cast_mode, impact_effect, force, force_falloff_per_tile, max_displacement_tiles,
                target_mode, manifestation_base_success_percent, manifestation_magic_levels_per_step,
                manifestation_success_percent_per_step, matter_lifetime_milliseconds,
                matter_capacity_magic_levels_per_step, matter_max_active, matter_visual_texture_path, matter_visual_render_scale,
                    force_mastery_magic_levels_per_step, force_mastery_force_per_step, force_mastery_max_force, displacement_mastery_magic_levels_per_step, displacement_mastery_tiles_per_step, displacement_mastery_max_tiles, matter_physical_weight, ignition_base_success_percent, ignition_magic_levels_per_step, ignition_success_percent_per_step, burning_lifetime_milliseconds, burning_capacity_magic_levels_per_step, burning_max_active, burning_min_damage, burning_max_damage, burning_hazard_cooldown_milliseconds, burning_visual_frames, burning_visual_animation_fps, burning_visual_render_scale, slick_base_success_percent, slick_magic_levels_per_step, slick_success_percent_per_step, slick_lifetime_milliseconds, slick_capacity_magic_levels_per_step, slick_max_active, slick_visual_frames, slick_visual_animation_fps, slick_visual_render_scale)
                VALUES (@id, @display_name, @tier, @element, @required_magic_level, @shard_cost,
                    @successful_hit_min_damage, @base_max_hit, @base_cast_xp_tenths, @state, @icon_texture_path,
                    @projectile_animation_fps, @projectile_render_scale, @projectile_rotates_to_travel, @cast_sound_path,
                    @impact_animation_fps, @impact_render_scale, @impact_sound_path, @splash_animation_fps,
                    @splash_render_scale, @splash_sound_path, @projectile_source_facing,
                    @projectile_homing_enabled, @projectile_homing_strength, @cast_mode, @impact_effect, @force, @force_falloff_per_tile, @max_displacement_tiles,
                    @target_mode, @manifestation_base_success_percent, @manifestation_magic_levels_per_step,
                    @manifestation_success_percent_per_step, @matter_lifetime_milliseconds,
                    @matter_capacity_magic_levels_per_step, @matter_max_active, @matter_visual_texture_path, @matter_visual_render_scale,
                    @force_mastery_magic_levels_per_step, @force_mastery_force_per_step, @force_mastery_max_force, @displacement_mastery_magic_levels_per_step, @displacement_mastery_tiles_per_step, @displacement_mastery_max_tiles, @matter_physical_weight, @ignition_base_success_percent, @ignition_magic_levels_per_step, @ignition_success_percent_per_step, @burning_lifetime_milliseconds, @burning_capacity_magic_levels_per_step, @burning_max_active, @burning_min_damage, @burning_max_damage, @burning_hazard_cooldown_milliseconds, @burning_visual_frames, @burning_visual_animation_fps, @burning_visual_render_scale, @slick_base_success_percent, @slick_magic_levels_per_step, @slick_success_percent_per_step, @slick_lifetime_milliseconds, @slick_capacity_magic_levels_per_step, @slick_max_active, @slick_visual_frames, @slick_visual_animation_fps, @slick_visual_render_scale)
                """ : """
                UPDATE magic_combat_spells SET display_name=@display_name, tier=@tier, element=@element,
                    required_magic_level=@required_magic_level, shard_cost=@shard_cost,
                    successful_hit_min_damage=@successful_hit_min_damage, base_max_hit=@base_max_hit,
                    base_cast_xp_tenths=@base_cast_xp_tenths, publication_state=@state,
                    icon_texture_path=@icon_texture_path, projectile_animation_fps=@projectile_animation_fps,
                    projectile_render_scale=@projectile_render_scale,
                    projectile_rotates_to_travel=@projectile_rotates_to_travel,
                    projectile_source_facing=@projectile_source_facing,
                    projectile_homing_enabled=@projectile_homing_enabled,
                    projectile_homing_strength=@projectile_homing_strength,
                    target_mode=@target_mode,
                    matter_physical_weight=@matter_physical_weight,
                    ignition_base_success_percent=@ignition_base_success_percent,
                    ignition_magic_levels_per_step=@ignition_magic_levels_per_step,
                    ignition_success_percent_per_step=@ignition_success_percent_per_step,
                    burning_lifetime_milliseconds=@burning_lifetime_milliseconds,
                    burning_capacity_magic_levels_per_step=@burning_capacity_magic_levels_per_step,
                    burning_max_active=@burning_max_active,
                    burning_min_damage=@burning_min_damage,
                    burning_max_damage=@burning_max_damage,
                    burning_hazard_cooldown_milliseconds=@burning_hazard_cooldown_milliseconds,
                    burning_visual_frames=@burning_visual_frames,
                    burning_visual_animation_fps=@burning_visual_animation_fps,
                    burning_visual_render_scale=@burning_visual_render_scale,
                    slick_base_success_percent=@slick_base_success_percent,
                    slick_magic_levels_per_step=@slick_magic_levels_per_step,
                    slick_success_percent_per_step=@slick_success_percent_per_step,
                    slick_lifetime_milliseconds=@slick_lifetime_milliseconds,
                    slick_capacity_magic_levels_per_step=@slick_capacity_magic_levels_per_step,
                    slick_max_active=@slick_max_active,
                    slick_visual_frames=@slick_visual_frames,
                    slick_visual_animation_fps=@slick_visual_animation_fps,
                    slick_visual_render_scale=@slick_visual_render_scale,
                    manifestation_base_success_percent=@manifestation_base_success_percent,
                    manifestation_magic_levels_per_step=@manifestation_magic_levels_per_step,
                    manifestation_success_percent_per_step=@manifestation_success_percent_per_step,
                    matter_lifetime_milliseconds=@matter_lifetime_milliseconds,
                    matter_capacity_magic_levels_per_step=@matter_capacity_magic_levels_per_step,
                    matter_max_active=@matter_max_active,
                    matter_visual_texture_path=@matter_visual_texture_path,
                    matter_visual_render_scale=@matter_visual_render_scale,
                    force_mastery_magic_levels_per_step=@force_mastery_magic_levels_per_step,
                    force_mastery_force_per_step=@force_mastery_force_per_step,
                    force_mastery_max_force=@force_mastery_max_force,
                    displacement_mastery_magic_levels_per_step=@displacement_mastery_magic_levels_per_step,
                    displacement_mastery_tiles_per_step=@displacement_mastery_tiles_per_step,
                    displacement_mastery_max_tiles=@displacement_mastery_max_tiles,
                    cast_mode=@cast_mode, impact_effect=@impact_effect, force=@force,
                    force_falloff_per_tile=@force_falloff_per_tile, max_displacement_tiles=@max_displacement_tiles,
                    cast_sound_path=@cast_sound_path,
                    impact_animation_fps=@impact_animation_fps, impact_render_scale=@impact_render_scale,
                    impact_sound_path=@impact_sound_path, splash_animation_fps=@splash_animation_fps,
                    splash_render_scale=@splash_render_scale, splash_sound_path=@splash_sound_path,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE spell_id=@id
                """;
            command.Parameters.AddWithValue("matter_physical_weight", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.MatterPhysicalWeight ?? DBNull.Value);
            command.Parameters.AddWithValue("ignition_base_success_percent", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.IgnitionBaseSuccessPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("ignition_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.IgnitionMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("ignition_success_percent_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.IgnitionSuccessPercentPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_lifetime_milliseconds", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningLifetimeMilliseconds ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_capacity_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningCapacityMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_max_active", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningMaxActive ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_min_damage", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningMinDamage ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_max_damage", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningMaxDamage ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_hazard_cooldown_milliseconds", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.BurningHazardCooldownMilliseconds ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_visual_frames", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.BurningVisualFrames?.ToArray() ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_visual_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.BurningVisualAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("burning_visual_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.BurningVisualRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_base_success_percent", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickBaseSuccessPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_success_percent_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickSuccessPercentPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_lifetime_milliseconds", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickLifetimeMilliseconds ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_capacity_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickCapacityMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_max_active", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.SlickMaxActive ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_visual_frames", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.SlickVisualFrames?.ToArray() ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_visual_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.SlickVisualAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("slick_visual_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.SlickVisualRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("target_mode", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.TargetMode ?? DBNull.Value);
            command.Parameters.AddWithValue("manifestation_base_success_percent", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ManifestationBaseSuccessPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("manifestation_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ManifestationMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("manifestation_success_percent_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ManifestationSuccessPercentPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("matter_lifetime_milliseconds", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.MatterLifetimeMilliseconds ?? DBNull.Value);
            command.Parameters.AddWithValue("matter_capacity_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.MatterCapacityMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("matter_max_active", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.MatterMaxActive ?? DBNull.Value);
            command.Parameters.AddWithValue("matter_visual_texture_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.MatterVisualTexturePath ?? DBNull.Value);
            command.Parameters.AddWithValue("matter_visual_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.MatterVisualRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("force_mastery_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ForceMasteryMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("force_mastery_force_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ForceMasteryForcePerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("force_mastery_max_force", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ForceMasteryMaxForce ?? DBNull.Value);
            command.Parameters.AddWithValue("displacement_mastery_magic_levels_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.DisplacementMasteryMagicLevelsPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("displacement_mastery_tiles_per_step", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.DisplacementMasteryTilesPerStep ?? DBNull.Value);
            command.Parameters.AddWithValue("displacement_mastery_max_tiles", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.DisplacementMasteryMaxTiles ?? DBNull.Value);
            command.Parameters.AddWithValue("cast_mode", draft.CastMode);
            command.Parameters.AddWithValue("impact_effect", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.ImpactEffect ?? DBNull.Value);
            command.Parameters.AddWithValue("force", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.Force ?? DBNull.Value);
            command.Parameters.AddWithValue("force_falloff_per_tile", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.ForceFalloffPerTile ?? DBNull.Value);
            command.Parameters.AddWithValue("max_displacement_tiles", NpgsqlTypes.NpgsqlDbType.Integer, (object?)draft.MaxDisplacementTiles ?? DBNull.Value);
            command.Parameters.AddWithValue("icon_texture_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.IconTexturePath ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ProjectileAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ProjectileRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_rotates_to_travel", NpgsqlTypes.NpgsqlDbType.Boolean, (object?)draft.ProjectileRotatesToTravel ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_source_facing", draft.ProjectileSourceFacing);
            command.Parameters.AddWithValue("projectile_homing_enabled", draft.ProjectileHomingEnabled);
            command.Parameters.AddWithValue("projectile_homing_strength", draft.ProjectileHomingStrength);
            command.Parameters.AddWithValue("cast_sound_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.CastSoundPath ?? DBNull.Value);
            command.Parameters.AddWithValue("impact_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ImpactAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("impact_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ImpactRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("impact_sound_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.ImpactSoundPath ?? DBNull.Value);
            command.Parameters.AddWithValue("splash_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.SplashAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("splash_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.SplashRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("splash_sound_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.SplashSoundPath ?? DBNull.Value);
            command.Parameters.AddWithValue("display_name", draft.DisplayName);
            command.Parameters.AddWithValue("tier", draft.Tier);
            command.Parameters.AddWithValue("element", draft.Element);
            command.Parameters.AddWithValue("required_magic_level", draft.RequiredMagicLevel);
            command.Parameters.AddWithValue("shard_cost", draft.ShardCost);
            command.Parameters.AddWithValue("successful_hit_min_damage", draft.SuccessfulHitMinDamage);
            command.Parameters.AddWithValue("base_max_hit", draft.BaseMaxHit);
            command.Parameters.AddWithValue("base_cast_xp_tenths", draft.BaseCastXpTenths);
            command.Parameters.AddWithValue("state", operation == "save_and_publish" ? "Published" : "Draft");
        }
        else
        {
            command.CommandText = """
                UPDATE magic_combat_spells SET publication_state=@state,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE spell_id=@id
                """;
            command.Parameters.AddWithValue("state", operation == "publish" ? "Published" : "Disabled");
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (operation is "save_draft" or "save_and_publish")
        {
            await using var clear = new NpgsqlCommand("DELETE FROM magic_spell_presentation_frames WHERE spell_id=@id", connection, transaction);
            clear.Parameters.AddWithValue("id", definitionId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
            foreach (var (phase, frames) in new[] { ("projectile", draft.ProjectileFrames), ("impact", draft.ImpactFrames), ("splash", draft.SplashFrames) })
            {
                for (var index = 0; index < (frames?.Count ?? 0); index++)
                {
                    await using var frame = new NpgsqlCommand("""
                        INSERT INTO magic_spell_presentation_frames(spell_id, phase, frame_order, texture_path)
                        VALUES (@id, @phase, @order, @path);
                        """, connection, transaction);
                    frame.Parameters.AddWithValue("id", definitionId);
                    frame.Parameters.AddWithValue("phase", phase);
                    frame.Parameters.AddWithValue("order", index);
                    frame.Parameters.AddWithValue("path", frames![index]);
                    await frame.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
        var saved = await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }
}
public sealed class MagicSpellConcurrencyException : Exception;
