namespace MMO.ContentStudio.AuthoringHost.Configuration;

public sealed class AuthoringHostOptions
{
    public const string SectionName = "AuthoringHost";

    public string ServiceName { get; init; } = "MMO Content Authoring Host";
    public string ListenUrl { get; init; } = "http://127.0.0.1:5187";
    public string ExpectedSchemaContract { get; init; } = "prototype-consumable-authoring-v1";
}

public sealed class ConnectionProfilesOptions
{
    public const string SectionName = "ConnectionProfiles";

    public string Active { get; init; } = "local";
    public Dictionary<string, ConnectionProfileOptions> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConnectionProfileOptions
{
    public string DisplayName { get; init; } = "Local development";
    public string ConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 5;
}

public sealed class AssetRootsOptions
{
    public const string SectionName = "AssetRoots";

    public Dictionary<string, string> Roots { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
