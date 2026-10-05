// Owns one authored recipe aggregate and ordered inputs. Expected timestamps
// and a row lock protect edits; inserts retain primary-key concurrency protection.
using System.Data;
using MMO.Project.Blacksmithing;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class BlacksmithingRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<BlacksmithingSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT recipe_id,display_name,publication_state FROM blacksmithing_recipes WHERE @s='' OR recipe_id ILIKE '%'||@s||'%' OR display_name ILIKE '%'||@s||'%' ORDER BY display_name,recipe_id", connection);
        command.Parameters.AddWithValue("s", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BlacksmithingSummary>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }
    public async Task<BlacksmithingDefinition?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await LoadAsync(connection, transaction, id, false, cancellationToken);
    }
    private static async Task<BlacksmithingDefinition?> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string id, bool locked, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT display_name,operation,station_definition_id,output_item_id,output_quantity,required_level,xp_tenths,duration_ms,required_inventory_tool_id,publication_state,updated_at FROM blacksmithing_recipes WHERE recipe_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        BlacksmithingRecipe recipe; string state; DateTimeOffset updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            recipe = new(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), [], reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8));
            state = reader.GetString(9); updated = reader.GetFieldValue<DateTimeOffset>(10);
        }
        command.CommandText = "SELECT item_id,quantity FROM blacksmithing_recipe_inputs WHERE recipe_id=@id ORDER BY input_order";
        var inputs = new List<BlacksmithingInput>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) inputs.Add(new(reader.GetString(0), reader.GetInt32(1)));
        return new(id, state, recipe with { Inputs = inputs.AsReadOnly() }, updated);
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(BlacksmithingRecipe recipe, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        var items = new HashSet<string>(StringComparer.Ordinal);
        var stations = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT item_id FROM item_definitions WHERE runtime_enabled", connection);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)) while (await reader.ReadAsync(cancellationToken)) items.Add(reader.GetString(0));
        command.CommandText = "SELECT definition_id FROM world_object_definitions WHERE publication_state='Published'";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)) while (await reader.ReadAsync(cancellationToken)) stations.Add(reader.GetString(0));
        return recipe.Validate(items, stations);
    }
    public async Task<BlacksmithingDefinition?> MutateAsync(string id, string operation, BlacksmithingRecipe draft, DateTimeOffset? expected, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, id, true, cancellationToken);
        if (existing?.UpdatedAtUtc != expected) throw new BlacksmithingConcurrencyException();
        await using var command = new NpgsqlCommand("", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        if (operation == "delete") command.CommandText = "DELETE FROM blacksmithing_recipes WHERE recipe_id=@id";
        else if (operation == "save_draft")
        {
            command.CommandText = """
                INSERT INTO blacksmithing_recipes(recipe_id,display_name,operation,station_definition_id,output_item_id,output_quantity,required_level,xp_tenths,duration_ms,required_inventory_tool_id)
                VALUES(@id,@name,@operation,@station,@output,@quantity,@level,@xp,@duration,@tool)
                """;
            if (existing is not null) command.CommandText = """
                UPDATE blacksmithing_recipes SET display_name=@name,operation=@operation,station_definition_id=@station,output_item_id=@output,output_quantity=@quantity,required_level=@level,xp_tenths=@xp,duration_ms=@duration,required_inventory_tool_id=@tool,publication_state='Draft',updated_at=clock_timestamp() WHERE recipe_id=@id
                """;
            command.Parameters.AddWithValue("name", draft.DisplayName);
            command.Parameters.AddWithValue("operation", draft.Operation);
            command.Parameters.AddWithValue("station", draft.StationDefinitionId);
            command.Parameters.AddWithValue("output", draft.OutputItemId);
            command.Parameters.AddWithValue("quantity", draft.OutputQuantity);
            command.Parameters.AddWithValue("level", draft.RequiredLevel);
            command.Parameters.AddWithValue("xp", draft.XpTenths);
            command.Parameters.AddWithValue("duration", draft.DurationMs);
            command.Parameters.AddWithValue("tool", NpgsqlTypes.NpgsqlDbType.Text, (object?)draft.RequiredInventoryToolId ?? DBNull.Value);
        }
        else
        {
            command.CommandText = "UPDATE blacksmithing_recipes SET publication_state=@state,updated_at=clock_timestamp() WHERE recipe_id=@id";
            command.Parameters.AddWithValue("state", operation == "publish" ? "Published" : "Disabled");
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (operation == "save_draft")
        {
            command.CommandText = "DELETE FROM blacksmithing_recipe_inputs WHERE recipe_id=@id";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "INSERT INTO blacksmithing_recipe_inputs(recipe_id,input_order,item_id,quantity) VALUES(@id,@order,@item,@count)";
            for (var index = 0; index < draft.Inputs.Count; index++)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("order", index);
                command.Parameters.AddWithValue("item", draft.Inputs[index].ItemId);
                command.Parameters.AddWithValue("count", draft.Inputs[index].Quantity);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        var result = await LoadAsync(connection, transaction, id, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
public sealed class BlacksmithingConcurrencyException : Exception;
