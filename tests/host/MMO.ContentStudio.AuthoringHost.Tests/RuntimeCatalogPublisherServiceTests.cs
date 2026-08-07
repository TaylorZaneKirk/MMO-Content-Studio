using Microsoft.Extensions.Logging.Abstractions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class RuntimeCatalogPublisherServiceTests : IDisposable
{
    private readonly string _prototypeRoot;

    public RuntimeCatalogPublisherServiceTests()
    {
        _prototypeRoot = Path.Combine(
            Path.GetTempPath(),
            "mmo-content-studio-runtime-publisher-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_prototypeRoot, "tools", "MapPublisher"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_prototypeRoot))
        {
            Directory.Delete(_prototypeRoot, true);
        }
    }

    [Theory]
    [InlineData(RuntimeCatalogPublicationScope.EquipmentVisual, "export-equipment-visual-catalog", "client/actors/appearance/data/equipped_visuals/published_catalog_v1.json")]
    [InlineData(RuntimeCatalogPublicationScope.Npc, "export-npc-catalog", "shared/maps/npcs/catalog.json")]
    [InlineData(RuntimeCatalogPublicationScope.Mob, "export-mob-catalog", "shared/maps/mobs/catalog.json")]
    [InlineData(RuntimeCatalogPublicationScope.Dialogue, "export-dialogue-catalog", "shared/dialogues/catalog.json")]
    public async Task ScopedPublicationRunsOnlyTheRequestedCatalog(
        RuntimeCatalogPublicationScope scope,
        string expectedCommand,
        string expectedOutputPath)
    {
        var calls = new List<(string Command, string OutputPath)>();
        var service = new RuntimeCatalogPublisherService(
            "local",
            "Host=test;Database=test;",
            _prototypeRoot,
            NullLogger<RuntimeCatalogPublisherService>.Instance,
            (command, outputPath, cancellationToken) =>
            {
                calls.Add((command, outputPath));
                return Task.FromResult(new RuntimeCatalogPublisherService.RuntimeCatalogCommandResult(0, string.Empty, string.Empty));
            });

        var result = await service.PublishCatalogsAsync(scope, TestContext.Current.CancellationToken);

        Assert.Empty(result);
        var call = Assert.Single(calls);
        Assert.Equal(expectedCommand, call.Command);
        Assert.Equal(expectedOutputPath, call.OutputPath);
    }
}
