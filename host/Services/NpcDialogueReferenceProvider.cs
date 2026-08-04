using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class NpcDialogueReferenceProvider
{
    private readonly AssetRootsOptions _options;

    public NpcDialogueReferenceProvider(IOptions<AssetRootsOptions> options)
    {
        _options = options.Value;
    }

    public Task<NpcDialogueReferenceSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalogPath = ResolveDialogueCatalogPath();
        if (catalogPath is null || !File.Exists(catalogPath))
        {
            return Task.FromResult(new NpcDialogueReferenceSet([], false, catalogPath));
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            if (!document.RootElement.TryGetProperty("dialogues", out var dialogues)
                || dialogues.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(new NpcDialogueReferenceSet([], false, catalogPath));
            }

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var dialogue in dialogues.EnumerateArray())
            {
                if (dialogue.TryGetProperty("dialogue_id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    ids.Add(id.GetString()!.Trim());
                }
            }

            var options = ids
                .Select(id => new AuthoringOption(id, id))
                .ToArray();
            return Task.FromResult(new NpcDialogueReferenceSet(options, true, catalogPath));
        }
        catch (JsonException)
        {
            return Task.FromResult(new NpcDialogueReferenceSet([], false, catalogPath));
        }
        catch (IOException)
        {
            return Task.FromResult(new NpcDialogueReferenceSet([], false, catalogPath));
        }
    }

    private string? ResolveDialogueCatalogPath()
    {
        if (!_options.Roots.TryGetValue("game_client_assets", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var assetsRoot = Path.GetFullPath(configured);
        var clientRoot = Directory.GetParent(assetsRoot);
        var prototypeRoot = clientRoot?.Parent;
        return prototypeRoot is null
            ? null
            : Path.Combine(prototypeRoot.FullName, "shared", "dialogues", "catalog.json");
    }
}

public sealed record NpcDialogueReferenceSet(
    IReadOnlyList<AuthoringOption> DialogueReferences,
    bool Complete,
    string? SourcePath);
