using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class AuthoringDatabaseConnectionFactory
{
    private readonly ConnectionProfilesOptions _options;

    public AuthoringDatabaseConnectionFactory(IOptions<ConnectionProfilesOptions> options)
    {
        _options = options.Value;
    }

    public string ActiveProfile => _options.Active;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Profiles.TryGetValue(_options.Active, out var profile))
        {
            throw new AuthoringDatabaseUnavailableException(
                $"Connection profile '{_options.Active}' is not defined.");
        }

        if (string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            throw new AuthoringDatabaseUnavailableException(
                $"Connection profile '{_options.Active}' has no connection string.");
        }

        var timeout = Math.Clamp(profile.CommandTimeoutSeconds, 1, 30);
        var builder = new NpgsqlConnectionStringBuilder(profile.ConnectionString)
        {
            Timeout = timeout,
            CommandTimeout = timeout
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class AuthoringDatabaseUnavailableException : Exception
{
    public AuthoringDatabaseUnavailableException(string message)
        : base(message)
    {
    }
}
