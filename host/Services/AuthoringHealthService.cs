using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Health;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class AuthoringHealthService
{
    private readonly ConnectionProfilesOptions _connectionProfiles;
    private readonly AssetRootsOptions _assetRoots;
    private readonly AuthoringHostOptions _hostOptions;
    private readonly IReadOnlyList<IAuthoringSchemaRequirementProvider> _schemaRequirementProviders;
    private readonly SchemaHealthInspector _schemaHealthInspector;
    private readonly ILogger<AuthoringHealthService> _logger;

    public AuthoringHealthService(
        IOptions<ConnectionProfilesOptions> connectionProfiles,
        IOptions<AssetRootsOptions> assetRoots,
        IOptions<AuthoringHostOptions> hostOptions,
        IEnumerable<IAuthoringSchemaRequirementProvider> schemaRequirementProviders,
        SchemaHealthInspector schemaHealthInspector,
        ILogger<AuthoringHealthService> logger)
    {
        _connectionProfiles = connectionProfiles.Value;
        _assetRoots = assetRoots.Value;
        _hostOptions = hostOptions.Value;
        _schemaRequirementProviders = schemaRequirementProviders.ToArray();
        _schemaHealthInspector = schemaHealthInspector;
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
            var requirements = _schemaRequirementProviders
                .SelectMany(provider => provider.GetRequirements())
                .DistinctBy(requirement => requirement.Key, StringComparer.Ordinal)
                .ToArray();

            var checks = new List<HealthCheck>(requirements.Length);
            foreach (var requirement in requirements)
            {
                checks.Add(await _schemaHealthInspector.CheckAsync(
                    connection,
                    requirement,
                    cancellationToken));
            }

            if (checks.Count == 0)
            {
                checks.Add(new HealthCheck(
                    "schema_requirements",
                    HealthState.Unhealthy,
                    "No authoring features registered database schema requirements."));
            }

            var status = checks.All(check => check.Status == HealthState.Healthy)
                ? HealthState.Healthy
                : HealthState.Unhealthy;

            return new DatabaseHealth(
                status,
                activeProfile,
                databaseName,
                status == HealthState.Healthy ? _hostOptions.ExpectedSchemaContract : null,
                status == HealthState.Healthy
                    ? "Database connection and required authoring-feature schema checks passed."
                    : "Database connected, but one or more authoring-feature schema checks failed. Apply the required MMO Project migrations.",
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
