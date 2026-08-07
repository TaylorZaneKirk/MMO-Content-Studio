using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class RuntimeCatalogPublisherService : IRuntimeCatalogPublisher
{
    private static readonly (string Command, string Label, string OutputPath)[] Exports =
    [
        ("export-npc-catalog", "NPC", "shared/maps/npcs/catalog.json"),
        ("export-mob-catalog", "mob", "shared/maps/mobs/catalog.json"),
        ("export-dialogue-catalog", "dialogue", "shared/dialogues/catalog.json"),
        ("export-equipment-visual-catalog", "equipment visual", "client/actors/appearance/data/equipped_visuals/published_catalog_v1.json")
    ];

    private readonly ItemAssetService _assetService;
    private readonly ILogger<RuntimeCatalogPublisherService> _logger;
    private readonly string _activeProfile;
    private readonly string _connectionString;
    private readonly string? _prototypeRoot;

    public RuntimeCatalogPublisherService(
        IOptions<ConnectionProfilesOptions> connectionProfiles,
        ItemAssetService assetService,
        ILogger<RuntimeCatalogPublisherService> logger)
    {
        _assetService = assetService;
        _logger = logger;
        _activeProfile = connectionProfiles.Value.Active;

        if (connectionProfiles.Value.Profiles.TryGetValue(_activeProfile, out var profile))
        {
            _connectionString = profile.ConnectionString;
        }
        else
        {
            _connectionString = string.Empty;
        }

        _prototypeRoot = ResolvePrototypeRoot(_assetService.GetGameAssetsRoot());
    }

    public async Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(CancellationToken cancellationToken)
    {
        var messages = new List<ApiError>();
        var projectDirectory = ResolveMapPublisherProjectDirectory();
        if (projectDirectory is null)
        {
            return [
                new ApiError(
                    "map_catalog_publish_skipped",
                    "MapPublisher cannot be located from the configured game asset path. Run export commands from MMO Project manually.",
                    ValidationSeverity.Warning,
                    "publication_state",
                    "Update game_client_assets in appsettings and restart the host.")
            ];
        }

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return [
                new ApiError(
                    "map_catalog_publish_skipped",
                    "No active authoring database connection string is configured. Run export commands from MMO Project manually.",
                    ValidationSeverity.Warning,
                    "publication_state",
                    "Configure ConnectionProfiles.Active and run again.")
            ];
        }

        foreach (var (command, label, outputPath) in Exports)
        {
            var warning = await PublishSingleCatalogAsync(
                _prototypeRoot!,
                command,
                label,
                outputPath,
                cancellationToken);
            if (warning is not null)
            {
                messages.Add(warning);
            }
        }

        return messages;
    }

    private string? ResolveMapPublisherProjectDirectory()
    {
        if (string.IsNullOrWhiteSpace(_prototypeRoot))
        {
            return null;
        }

        var projectDirectory = Path.Combine(_prototypeRoot, "tools", "MapPublisher");
        return Directory.Exists(projectDirectory) ? projectDirectory : null;
    }

    private async Task<ApiError?> PublishSingleCatalogAsync(
        string prototypeRoot,
        string command,
        string label,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var projectDirectory = Path.Combine(prototypeRoot, "tools", "MapPublisher");
        try
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = prototypeRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(projectDirectory);
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(command);
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(outputPath);
            process.StartInfo.ArgumentList.Add("--connection-string");
            process.StartInfo.ArgumentList.Add(_connectionString);

            _logger.LogInformation(
                "Running {Command} from {WorkingDirectory} for {Profile} profile.",
                command,
                prototypeRoot,
                _activeProfile);

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "MapPublisher {Command} completed successfully for profile {Profile}.", command, _activeProfile);
                return null;
            }

            var details = CombineOutput(stdout, stderr);
            var guidance =
                $"dotnet run --project tools/MapPublisher -- {command} --output {outputPath} --connection-string <active-profile-connection-string>";
            _logger.LogWarning(
                "MapPublisher {Command} failed with exit code {ExitCode} for profile {Profile}: {Details}",
                command,
                process.ExitCode,
                _activeProfile,
                details);

            return new ApiError(
                "map_catalog_publish_warning",
                $"MapPublisher failed to refresh the runtime {label} catalog. Exit code {process.ExitCode}.",
                ValidationSeverity.Warning,
                "publication_state",
                $"Run manually: {guidance}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to execute {Command} for profile {Profile}.",
                command,
                _activeProfile);

            return new ApiError(
                "map_catalog_publish_warning",
                $"MapPublisher failed to refresh the runtime {label} catalog: {exception.Message}",
                ValidationSeverity.Warning,
                "publication_state",
                "Inspect host logs and run the export command manually.");
        }
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        var message = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            message.Append(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (message.Length > 0)
            {
                message.Append(' ');
            }

            message.Append(stderr.Trim());
        }

        return message.ToString();
    }

    private static string? ResolvePrototypeRoot(string? gameAssetsRoot)
    {
        if (string.IsNullOrWhiteSpace(gameAssetsRoot))
        {
            return null;
        }

        var current = new DirectoryInfo(gameAssetsRoot);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "shared")) &&
                Directory.Exists(Path.Combine(current.FullName, "client", "assets")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
