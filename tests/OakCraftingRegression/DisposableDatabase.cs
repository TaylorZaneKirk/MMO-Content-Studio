using Npgsql;

// Test-only connection guard. The workflow runner creates a fresh socket-only cluster.
internal static class DisposableDatabase
{
    public static async Task<string> Connection()
    {
        var root = Environment.GetEnvironmentVariable("MMO_TEST_CLUSTER")
            ?? throw new InvalidOperationException("Supply MMO_TEST_CLUSTER from the disposable workflow runner.");
        root = Path.GetFullPath(root);
        if (!root.StartsWith("/tmp/mmo-db-", StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(root, "workflow-owned")) ||
            new DirectoryInfo(root).LinkTarget is not null ||
            new DirectoryInfo(Path.Combine(root, "socket")).LinkTarget is not null)
            throw new InvalidOperationException("Not a workflow-owned disposable cluster.");
        var database = Environment.GetEnvironmentVariable("MMO_TEST_DATABASE") ?? "fixture";
        if (database is not ("fixture" or "studio_fixture")) throw new InvalidOperationException("Invalid disposable database name.");
        var cs = new NpgsqlConnectionStringBuilder { Host = Path.Combine(root, "socket"), Port = 55439,
            Database = database, Username = Environment.UserName, Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('data_directory'), current_setting('listen_addresses')", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        if (Path.GetFullPath(reader.GetString(0)) != Path.Combine(root, "data") || reader.GetString(1) != "")
            throw new InvalidOperationException("Refusing a database outside the private socket-only cluster.");
        return cs;
    }
}
