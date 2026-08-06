using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public interface IRuntimeCatalogPublisher
{
    Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(CancellationToken cancellationToken);
}
