// Persists one complete Shop and its ordered stock under the parent version lock.
using System.Data;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class ShopRepository(AuthoringDatabaseConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<ShopSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT shop_definition_id, display_name, publication_state FROM shop_definitions
            WHERE @search = '' OR shop_definition_id ILIKE '%' || @search || '%' OR display_name ILIKE '%' || @search || '%'
            ORDER BY display_name, shop_definition_id
            """, connection);
        command.Parameters.AddWithValue("search", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ShopSummary>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return items;
    }

    public async Task<IReadOnlyList<ShopItemOption>> LoadItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT item_id, item_name, runtime_enabled, shop_policy, npc_buy_price, npc_sell_price
            FROM item_definitions ORDER BY item_name, item_id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ShopItemOption>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetInt64(5)));
        return items;
    }

    public async Task<bool> HasNpcReferencesAsync(string definitionId, bool publishedOnly, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM npc_definitions WHERE shop_definition_id = @id
                AND (NOT @published OR publication_state = 'Published'))
            """, connection);
        command.Parameters.AddWithValue("id", definitionId);
        command.Parameters.AddWithValue("published", publishedOnly);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<ShopDefinition?> LoadAsync(string definitionId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
    }

    private static async Task<ShopDefinition?> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string definitionId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT display_name, buys_unstocked_items, price_change_per_stock_percent, notes, publication_state, updated_at
            FROM shop_definitions WHERE shop_definition_id = @id
            """ + (forUpdate ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", definitionId);
        ShopDraft draft;
        string state;
        DateTimeOffset updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            draft = new(reader.GetString(0), reader.GetBoolean(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3), []);
            state = reader.GetString(4);
            updated = reader.GetFieldValue<DateTimeOffset>(5);
        }
        var stock = new List<ShopStockItem>();
        command.CommandText = "SELECT item_id, default_stock, restock_ticks FROM shop_stock_items WHERE shop_definition_id=@id ORDER BY stock_order, item_id";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) stock.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        return new(definitionId, state, draft with { Stock = stock }, updated);
    }

    public async Task<ShopDefinition?> MutateAsync(string definitionId, string operation,
        ShopDraft draft, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, definitionId, true, cancellationToken);
        if (existing?.UpdatedAtUtc != expectedUpdatedAtUtc || (existing is null && operation != "save_draft"))
            throw new ShopConcurrencyException();
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        command.Parameters.AddWithValue("id", definitionId);
        if (operation == "delete")
        {
            if (existing!.PublicationState != "Disabled") throw new ShopConcurrencyException();
            command.CommandText = "DELETE FROM shop_definitions WHERE shop_definition_id=@id";
        }
        else if (operation is "save_draft" or "save_and_publish")
        {
            command.CommandText = existing is null ? """
                INSERT INTO shop_definitions (shop_definition_id, display_name, buys_unstocked_items, price_change_per_stock_percent, notes)
                VALUES (@id,@name,@buys,@rate,@notes)
                """ : """
                UPDATE shop_definitions SET display_name=@name, buys_unstocked_items=@buys,
                    price_change_per_stock_percent=@rate, notes=@notes, publication_state=@state,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE shop_definition_id=@id
                """;
            command.Parameters.AddWithValue("name", draft.DisplayName);
            command.Parameters.AddWithValue("buys", draft.BuysUnstockedItems);
            command.Parameters.AddWithValue("rate", draft.PriceChangePerStockPercent);
            command.Parameters.Add("notes", NpgsqlDbType.Text).Value = (object?)draft.Notes ?? DBNull.Value;
            if (existing is not null)
                command.Parameters.AddWithValue("state", operation == "save_and_publish" ? "Published" : "Draft");
        }
        else
        {
            command.CommandText = """
                UPDATE shop_definitions SET publication_state=@state,
                    updated_at=greatest(clock_timestamp(), updated_at + interval '1 microsecond') WHERE shop_definition_id=@id
                """;
            command.Parameters.AddWithValue("state", operation == "publish" ? "Published" : "Disabled");
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (operation is "save_draft" or "save_and_publish")
        {
            command.CommandText = "DELETE FROM shop_stock_items WHERE shop_definition_id=@id";
            await command.ExecuteNonQueryAsync(cancellationToken);
            for (var index = 0; index < draft.Stock.Count; index++)
            {
                var row = draft.Stock[index];
                await using var child = new NpgsqlCommand("""
                    INSERT INTO shop_stock_items(shop_definition_id,stock_order,item_id,default_stock,restock_ticks)
                    VALUES (@id,@order,@item,@stock,@ticks)
                    """, connection, transaction);
                child.Parameters.AddWithValue("id", definitionId);
                child.Parameters.AddWithValue("order", index);
                child.Parameters.AddWithValue("item", row.ItemId);
                child.Parameters.AddWithValue("stock", row.DefaultStock);
                child.Parameters.AddWithValue("ticks", row.RestockTicks);
                await child.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        var saved = await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }
}
public sealed class ShopConcurrencyException : Exception;
