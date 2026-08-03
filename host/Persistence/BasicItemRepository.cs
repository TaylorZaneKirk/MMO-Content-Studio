using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class BasicItemRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public BasicItemRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BasicItemRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                item_id,
                item_name,
                icon_texture_path,
                equipment_slot_id,
                runtime_enabled,
                required_strength,
                updated_at,
                exists (
                    select 1 from item_consumable_profiles cp
                    where cp.item_id = item_definitions.item_id
                ) as has_consumable_profile
            from item_definitions
            where @search is null
               or item_id ilike '%' || @search || '%'
               or item_name ilike '%' || @search || '%'
            order by item_name, item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<BasicItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task<BasicItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAsync(connection, null, itemId, false, cancellationToken);
    }

    public async Task<bool> HasLiveReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1 from character_inventory where item_id = @item_id
                union all
                select 1 from character_equipment where item_id = @item_id
                union all
                select 1 from ground_items where item_id = @item_id
            );
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> HasPublishedConsumableResultReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1
                from item_consumable_profiles profile
                join item_definitions source_item on source_item.item_id = profile.item_id
                where profile.result_item_id = @item_id
                  and source_item.runtime_enabled = true
            );
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<BasicItemRecord> SaveDraftAsync(
        string itemId,
        string displayName,
        string iconTexturePath,
        DateTimeOffset? expectedUpdatedAtUtc,
        bool expectNew,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, itemId, true, cancellationToken);
        if (expectNew && existing is not null)
        {
            throw new BasicItemConcurrencyException(itemId, existing.UpdatedAtUtc);
        }
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureBasicEditable(existing);
        if (existing is not null
            && !existing.RuntimeEnabled
            && string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal)
            && string.Equals(existing.IconTexturePath, iconTexturePath, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        const string sql = """
            insert into item_definitions (
                item_id,
                item_name,
                icon_texture_path,
                equipment_slot_id,
                runtime_enabled,
                required_strength,
                updated_at
            ) values (
                @item_id,
                @item_name,
                @icon_texture_path,
                null,
                false,
                1,
                now()
            )
            on conflict (item_id)
            do update set
                item_name = excluded.item_name,
                icon_texture_path = excluded.icon_texture_path,
                equipment_slot_id = null,
                required_strength = 1,
                runtime_enabled = false,
                updated_at = now();
            """;

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("item_name", displayName);
            command.Parameters.AddWithValue("icon_texture_path", iconTexturePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved item could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<BasicItemRecord> SetPublicationAsync(
        string itemId,
        bool runtimeEnabled,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new BasicItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureBasicEditable(existing);
        if (existing.RuntimeEnabled == runtimeEnabled)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        const string sql = """
            update item_definitions
            set runtime_enabled = @runtime_enabled,
                updated_at = now()
            where item_id = @item_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("runtime_enabled", runtimeEnabled);
            command.Parameters.AddWithValue("item_id", itemId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Published item could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new BasicItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureBasicEditable(existing);
        if (existing.RuntimeEnabled)
        {
            throw new BasicItemPublishedDeleteException(itemId);
        }

        const string sql = "delete from item_definitions where item_id = @item_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<BasicItemRecord?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                item_id,
                item_name,
                icon_texture_path,
                equipment_slot_id,
                runtime_enabled,
                required_strength,
                updated_at,
                exists (
                    select 1 from item_consumable_profiles cp
                    where cp.item_id = item_definitions.item_id
                ) as has_consumable_profile
            from item_definitions
            where item_id = @item_id
            """ + (forUpdate ? " for update;" : ";");

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static BasicItemRecord ReadRecord(NpgsqlDataReader reader)
    {
        var equipmentOrdinal = reader.GetOrdinal("equipment_slot_id");
        return new BasicItemRecord(
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("item_name")),
            reader.GetString(reader.GetOrdinal("icon_texture_path")),
            reader.IsDBNull(equipmentOrdinal) ? null : reader.GetString(equipmentOrdinal),
            reader.GetBoolean(reader.GetOrdinal("runtime_enabled")),
            reader.GetInt32(reader.GetOrdinal("required_strength")),
            reader.GetBoolean(reader.GetOrdinal("has_consumable_profile")),
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at")),
                    DateTimeKind.Utc)));
    }

    private static void EnsureBasicEditable(BasicItemRecord? existing)
    {
        if (existing is not null
            && (existing.EquipmentSlotId is not null
                || existing.RequiredStrength != 1
                || existing.HasConsumableProfile))
        {
            var workspace = existing.HasConsumableProfile ? "Consumables" : "the future Equipment workspace";
            throw new BasicItemKindConflictException(
                existing.ItemId,
                $"This item must be edited in {workspace}.");
        }
    }

    private static void EnsureExpectedVersion(
        BasicItemRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (existing is null)
        {
            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new BasicItemConcurrencyException(existing.ItemId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record BasicItemRecord(
    string ItemId,
    string DisplayName,
    string IconTexturePath,
    string? EquipmentSlotId,
    bool RuntimeEnabled,
    int RequiredStrength,
    bool HasConsumableProfile,
    DateTimeOffset UpdatedAtUtc);

public sealed class BasicItemNotFoundException : Exception
{
    public BasicItemNotFoundException(string itemId)
        : base($"Item '{itemId}' does not exist.")
    {
    }
}

public sealed class BasicItemKindConflictException : Exception
{
    public BasicItemKindConflictException(string itemId, string message)
        : base($"Item '{itemId}' cannot be edited here. {message}")
    {
    }
}

public sealed class BasicItemConcurrencyException : Exception
{
    public BasicItemConcurrencyException(string itemId, DateTimeOffset currentUpdatedAtUtc)
        : base($"Item '{itemId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset CurrentUpdatedAtUtc { get; }
}

public sealed class BasicItemPublishedDeleteException : Exception
{
    public BasicItemPublishedDeleteException(string itemId)
        : base($"Item '{itemId}' must be disabled before it can be deleted.")
    {
    }
}
