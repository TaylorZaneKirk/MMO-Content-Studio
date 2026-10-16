using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
using MMO.Project.Prototype.Server.Game;
using MMO.ContentStudio.AuthoringHost.Contracts;
namespace MMO.ContentStudio.AuthoringHost.Persistence;

// One recipe document and its last published snapshot. Expected versions plus a
// row lock protect edits; the primary key protects simultaneous first saves.
public sealed class CookingRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<CookingSummary>> ListAsync(string? search, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT recipe_id,draft->>'output_item_id',publication_state FROM cooking_recipes WHERE @s='' OR recipe_id ILIKE '%'||@s||'%' ORDER BY recipe_id", connection);
        command.Parameters.AddWithValue("s", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<CookingSummary>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }
    public async Task<CookingDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<CookingDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT draft,publication_state,updated_at,published_recipe IS NOT NULL FROM cooking_recipes WHERE recipe_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(id, reader.GetString(1), JsonSerializer.Deserialize<CookingRecipe>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2), reader.GetBoolean(3)) : null;
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(CookingRecipe recipe, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id FROM item_definitions WHERE runtime_enabled", connection);
        var items = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(reader.GetString(0));
        return recipe.Validate(items);
    }
    public async Task<CookingDefinition?> MutateAsync(string id, string operation, CookingRecipe draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected || (operation == "delete" && existing?.HasPublishedSnapshot == true))
            throw new CookingConcurrencyException();
        var sql = operation switch
        {
            "save_draft" when existing is null => "INSERT INTO cooking_recipes(recipe_id,draft) VALUES(@id,@draft)",
            "save_draft" => "UPDATE cooking_recipes SET draft=@draft,publication_state='Draft',updated_at=clock_timestamp() WHERE recipe_id=@id",
            "publish" => "UPDATE cooking_recipes SET published_recipe=draft,publication_state='Published',updated_at=clock_timestamp() WHERE recipe_id=@id",
            "disable" => "UPDATE cooking_recipes SET published_recipe=NULL,publication_state='Disabled',updated_at=clock_timestamp() WHERE recipe_id=@id",
            "delete" => "DELETE FROM cooking_recipes WHERE recipe_id=@id",
            _ => throw new InvalidOperationException("Unsupported Cooking operation.")
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("draft", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class CookingConcurrencyException : Exception;
