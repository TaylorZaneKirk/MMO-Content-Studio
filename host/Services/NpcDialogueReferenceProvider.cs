using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class NpcDialogueReferenceProvider
{
    private readonly AssetRootsOptions _options;
    private readonly IDialogueRepository? _dialogueRepository;

    public NpcDialogueReferenceProvider(IOptions<AssetRootsOptions> options)
        : this(options, null)
    {
    }

    public NpcDialogueReferenceProvider(
        IOptions<AssetRootsOptions> options,
        IDialogueRepository? dialogueRepository)
    {
        _options = options.Value;
        _dialogueRepository = dialogueRepository;
    }

    public async Task<NpcDialogueReferenceSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dialogueRepository is not null)
        {
            var records = await _dialogueRepository.ListAsync(null, cancellationToken);
            var options = records
                .Where(record => record.PublicationState == "Published")
                .OrderBy(record => record.DialogueDefinitionId, StringComparer.Ordinal)
                .Select(record => new AuthoringOption(record.DialogueDefinitionId, record.DisplayName))
                .ToArray();
            return new NpcDialogueReferenceSet(options, true, "authoring:dialogue_definitions");
        }

        var catalogPath = ResolveDialogueCatalogPath();
        if (catalogPath is null || !File.Exists(catalogPath))
        {
            return new NpcDialogueReferenceSet([], false, catalogPath);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            if (!document.RootElement.TryGetProperty("dialogues", out var dialogues)
                || dialogues.ValueKind != JsonValueKind.Array)
            {
                return new NpcDialogueReferenceSet([], false, catalogPath);
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
            return new NpcDialogueReferenceSet(options, true, catalogPath);
        }
        catch (JsonException)
        {
            return new NpcDialogueReferenceSet([], false, catalogPath);
        }
        catch (IOException)
        {
            return new NpcDialogueReferenceSet([], false, catalogPath);
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
