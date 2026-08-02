using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Features.Catalog;

public interface IAuthoringCatalogSectionProvider
{
    string ContentType { get; }

    int SortOrder { get; }

    Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken);
}

public sealed class PlannedCatalogSectionProvider(
    string contentType,
    string displayName,
    int sortOrder) : IAuthoringCatalogSectionProvider
{
    public string ContentType { get; } = contentType;

    public int SortOrder { get; } = sortOrder;

    public Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContentCatalogSection(
            ContentType,
            displayName,
            false,
            []));
    }
}
