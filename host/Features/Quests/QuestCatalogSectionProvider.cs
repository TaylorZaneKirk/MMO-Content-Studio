using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Quests;

public sealed class QuestCatalogSectionProvider(
    QuestAuthoringService quests) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "quests";

    public int SortOrder => 650;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await quests.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Select(quest => new ContentCatalogEntry(
                        quest.QuestId,
                        quest.DisplayName,
                        quest.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(
            ContentType,
            "Quests",
            true,
            entries);
    }
}
