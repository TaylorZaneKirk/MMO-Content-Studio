using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ContentCatalogService(
    IEnumerable<IAuthoringCatalogSectionProvider> providers)
{
    private readonly IReadOnlyList<IAuthoringCatalogSectionProvider> _providers =
        providers
            .OrderBy(provider => provider.SortOrder)
            .ThenBy(provider => provider.ContentType, StringComparer.Ordinal)
            .ToArray();

    public async Task<ContentCatalogResponse> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureUniqueContentTypes();
        var sections = new List<ContentCatalogSection>(_providers.Count);
        foreach (var provider in _providers)
        {
            sections.Add(await provider.LoadAsync(cancellationToken));
        }

        return new ContentCatalogResponse(DateTimeOffset.UtcNow, sections);
    }

    private void EnsureUniqueContentTypes()
    {
        var duplicate = _providers
            .GroupBy(provider => provider.ContentType, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Multiple authoring catalog providers registered for '{duplicate.Key}'.");
        }
    }
}
