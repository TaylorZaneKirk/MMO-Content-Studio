// Persists scalar combat spells. A row lock and expected updated_at protect
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
                successful_hit_min_damage, base_max_hit, base_cast_xp_tenths, publication_state, updated_at
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
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
            state = reader.GetString(8);
            updated = reader.GetFieldValue<DateTimeOffset>(9);
        }
        return new(definitionId, state, draft, updated);
    }

    // Lock the current definition, reject a stale editor, then save all scalar
    // fields and publication state together. Reload inside the same transaction.
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
                    base_cast_xp_tenths, publication_state)
                VALUES (@id, @display_name, @tier, @element, @required_magic_level, @shard_cost,
                    @successful_hit_min_damage, @base_max_hit, @base_cast_xp_tenths, @state)
                """ : """
                UPDATE magic_combat_spells SET display_name=@display_name, tier=@tier, element=@element,
                    required_magic_level=@required_magic_level, shard_cost=@shard_cost,
                    successful_hit_min_damage=@successful_hit_min_damage, base_max_hit=@base_max_hit,
                    base_cast_xp_tenths=@base_cast_xp_tenths, publication_state=@state,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE spell_id=@id
                """;
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
        var saved = await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }
}
public sealed class MagicSpellConcurrencyException : Exception;
