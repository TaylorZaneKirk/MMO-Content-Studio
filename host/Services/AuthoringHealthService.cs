using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class AuthoringHealthService
{
    private readonly ConnectionProfilesOptions _connectionProfiles;
    private readonly AssetRootsOptions _assetRoots;
    private readonly AuthoringHostOptions _hostOptions;
    private readonly ILogger<AuthoringHealthService> _logger;

    public AuthoringHealthService(
        IOptions<ConnectionProfilesOptions> connectionProfiles,
        IOptions<AssetRootsOptions> assetRoots,
        IOptions<AuthoringHostOptions> hostOptions,
        ILogger<AuthoringHealthService> logger)
    {
        _connectionProfiles = connectionProfiles.Value;
        _assetRoots = assetRoots.Value;
        _hostOptions = hostOptions.Value;
        _logger = logger;
    }

    public async Task<AuthoringHealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var database = await CheckDatabaseAsync(cancellationToken);
        var assetRoots = CheckAssetRoots();
        var statuses = assetRoots.Select(root => root.Status).Append(database.Status).ToArray();

        var overall = statuses.Any(status => status is HealthState.Unhealthy)
            ? HealthState.Unhealthy
            : statuses.Any(status => status is HealthState.Degraded or HealthState.Unconfigured)
                ? HealthState.Degraded
                : HealthState.Healthy;

        return new AuthoringHealthResponse(overall, database, assetRoots);
    }

    private async Task<DatabaseHealth> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        var activeProfile = _connectionProfiles.Active;
        if (!_connectionProfiles.Profiles.TryGetValue(activeProfile, out var profile))
        {
            return new DatabaseHealth(
                HealthState.Unconfigured,
                activeProfile,
                null,
                null,
                $"Connection profile '{activeProfile}' is not defined.",
                []);
        }

        if (string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            return new DatabaseHealth(
                HealthState.Unconfigured,
                activeProfile,
                null,
                null,
                "The active connection profile has no connection string.",
                []);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(profile.ConnectionString)
            {
                Timeout = Math.Clamp(profile.CommandTimeoutSeconds, 1, 30),
                CommandTimeout = Math.Clamp(profile.CommandTimeoutSeconds, 1, 30)
            };

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var databaseName = connection.Database;
            var checks = new List<HealthCheck>
            {
                await CheckTableAsync(connection, "item_definitions", cancellationToken),
                await CheckTableAsync(connection, "character_inventory", cancellationToken),
                await CheckTableAsync(connection, "character_equipment", cancellationToken),
                await CheckTableAsync(connection, "ground_items", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "item_id", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "item_name", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "icon_texture_path", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "equipment_slot_id", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "runtime_enabled", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "required_strength", cancellationToken),
                await CheckColumnAsync(connection, "item_definitions", "updated_at", cancellationToken),
                await CheckTriggerAsync(
                    connection,
                    "item_definitions",
                    "item_definitions_runtime_disable_guard",
                    cancellationToken)
            };

            var status = checks.All(check => check.Status == HealthState.Healthy)
                ? HealthState.Healthy
                : HealthState.Unhealthy;

            return new DatabaseHealth(
                status,
                activeProfile,
                databaseName,
                status == HealthState.Healthy ? _hostOptions.ExpectedSchemaContract : null,
                status == HealthState.Healthy
                    ? "Database connection and required T1 basic-item schema checks passed."
                    : "Database connected, but one or more required schema checks failed.",
                checks);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Content Studio database health check failed for profile {Profile}", activeProfile);
            return new DatabaseHealth(
                HealthState.Unhealthy,
                activeProfile,
                null,
                null,
                "Unable to connect to the configured PostgreSQL database.",
                [new HealthCheck("postgres_connection", HealthState.Unhealthy, exception.Message)]);
        }
    }

    private static async Task<HealthCheck> CheckTableAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'public'
                  and table_name = @table_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck($"table:{tableName}", HealthState.Healthy, $"Required table '{tableName}' exists.")
            : new HealthCheck($"table:{tableName}", HealthState.Unhealthy, $"Required table '{tableName}' is missing.");
    }

    private static async Task<HealthCheck> CheckColumnAsync(
        NpgsqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.columns
                where table_schema = 'public'
                  and table_name = @table_name
                  and column_name = @column_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("column_name", columnName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck($"column:{tableName}.{columnName}", HealthState.Healthy, $"Required column '{tableName}.{columnName}' exists.")
            : new HealthCheck($"column:{tableName}.{columnName}", HealthState.Unhealthy, $"Required column '{tableName}.{columnName}' is missing.");
    }

    private static async Task<HealthCheck> CheckTriggerAsync(
        NpgsqlConnection connection,
        string tableName,
        string triggerName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.triggers
                where event_object_schema = 'public'
                  and event_object_table = @table_name
                  and trigger_name = @trigger_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("trigger_name", triggerName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck(
                $"trigger:{triggerName}",
                HealthState.Healthy,
                $"Required trigger '{triggerName}' exists on '{tableName}'.")
            : new HealthCheck(
                $"trigger:{triggerName}",
                HealthState.Unhealthy,
                $"Required trigger '{triggerName}' is missing from '{tableName}'.");
    }

    private IReadOnlyList<AssetRootHealth> CheckAssetRoots()
    {
        if (_assetRoots.Roots.Count == 0)
        {
            return
            [
                new AssetRootHealth(
                    "configuration",
                    string.Empty,
                    HealthState.Unconfigured,
                    "No asset roots are configured.")
            ];
        }

        return _assetRoots.Roots
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    return new AssetRootHealth(
                        pair.Key,
                        pair.Value,
                        HealthState.Unconfigured,
                        "Asset-root path is empty.");
                }

                try
                {
                    var fullPath = Path.GetFullPath(pair.Value);
                    return Directory.Exists(fullPath)
                        ? new AssetRootHealth(pair.Key, fullPath, HealthState.Healthy, "Asset root exists.")
                        : new AssetRootHealth(pair.Key, fullPath, HealthState.Degraded, "Asset root does not exist yet.");
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    return new AssetRootHealth(
                        pair.Key,
                        pair.Value,
                        HealthState.Unhealthy,
                        $"Asset-root path is invalid: {exception.Message}");
                }
            })
            .ToArray();
    }
}
