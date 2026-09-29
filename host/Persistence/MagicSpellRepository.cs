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
                splash_render_scale, splash_sound_path, projectile_source_facing
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
                ProjectileSourceFacing: reader.GetString(21));
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
                    splash_sound_path, projectile_source_facing)
                VALUES (@id, @display_name, @tier, @element, @required_magic_level, @shard_cost,
                    @successful_hit_min_damage, @base_max_hit, @base_cast_xp_tenths, @state, @icon_texture_path,
                    @projectile_animation_fps, @projectile_render_scale, @projectile_rotates_to_travel, @cast_sound_path,
                    @impact_animation_fps, @impact_render_scale, @impact_sound_path, @splash_animation_fps,
                    @splash_render_scale, @splash_sound_path, @projectile_source_facing)
                """ : """
                UPDATE magic_combat_spells SET display_name=@display_name, tier=@tier, element=@element,
                    required_magic_level=@required_magic_level, shard_cost=@shard_cost,
                    successful_hit_min_damage=@successful_hit_min_damage, base_max_hit=@base_max_hit,
                    base_cast_xp_tenths=@base_cast_xp_tenths, publication_state=@state,
                    icon_texture_path=@icon_texture_path, projectile_animation_fps=@projectile_animation_fps,
                    projectile_render_scale=@projectile_render_scale,
                    projectile_rotates_to_travel=@projectile_rotates_to_travel,
                    projectile_source_facing=@projectile_source_facing, cast_sound_path=@cast_sound_path,
                    impact_animation_fps=@impact_animation_fps, impact_render_scale=@impact_render_scale,
                    impact_sound_path=@impact_sound_path, splash_animation_fps=@splash_animation_fps,
                    splash_render_scale=@splash_render_scale, splash_sound_path=@splash_sound_path,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE spell_id=@id
                """;
            command.Parameters.AddWithValue("icon_texture_path", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.IconTexturePath ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_animation_fps", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ProjectileAnimationFps ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_render_scale", NpgsqlTypes.NpgsqlDbType.Double, (object?)draft.ProjectileRenderScale ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_rotates_to_travel", NpgsqlTypes.NpgsqlDbType.Boolean, (object?)draft.ProjectileRotatesToTravel ?? DBNull.Value);
            command.Parameters.AddWithValue("projectile_source_facing", draft.ProjectileSourceFacing);
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
