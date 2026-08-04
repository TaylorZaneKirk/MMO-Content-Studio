using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Dialogues;

public sealed class DialogueCatalogSectionProvider(
    DialogueAuthoringService dialogues) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "dialogues";

    public int SortOrder => 600;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await dialogues.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Select(dialogue => new ContentCatalogEntry(
                        dialogue.DialogueDefinitionId,
                        dialogue.DisplayName,
                        dialogue.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(
            ContentType,
            "Dialogue",
            true,
            entries);
    }
}
