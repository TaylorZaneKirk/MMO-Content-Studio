using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ContentCatalogServiceTests
{
    [Fact]
    public async Task LoadAsyncOrdersProvidersBySortOrderThenContentType()
    {
        var service = new ContentCatalogService(
        [
            new StubProvider("zeta", 300),
            new StubProvider("beta", 100),
            new StubProvider("alpha", 100)
        ]);

        var response = await service.LoadAsync();

        string[] expected = ["alpha", "beta", "zeta"];
        Assert.Equal(
            expected,
            response.Sections.Select(section => section.ContentType));
    }

    [Fact]
    public async Task LoadAsyncRejectsDuplicateContentTypes()
    {
        var service = new ContentCatalogService(
        [
            new StubProvider("items", 100),
            new StubProvider("items", 200)
        ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LoadAsync());

        Assert.Contains("items", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlannedProviderReturnsUnimplementedEmptySection()
    {
        var provider = new PlannedCatalogSectionProvider("mobs", "Mobs", 400);

        var section = await provider.LoadAsync(CancellationToken.None);

        Assert.Equal("mobs", section.ContentType);
        Assert.Equal("Mobs", section.DisplayName);
        Assert.False(section.Implemented);
        Assert.Empty(section.Entries);
    }

    [Fact]
    public async Task PlannedProviderObservesCancellation()
    {
        var provider = new PlannedCatalogSectionProvider("mobs", "Mobs", 400);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.LoadAsync(cancellation.Token));
    }

    private sealed class StubProvider(
        string contentType,
        int sortOrder) : IAuthoringCatalogSectionProvider
    {
        public string ContentType { get; } = contentType;

        public int SortOrder { get; } = sortOrder;

        public Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ContentCatalogSection(
                ContentType,
                ContentType,
                true,
                []));
        }
    }
}
