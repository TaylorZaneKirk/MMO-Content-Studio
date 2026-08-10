using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ActorAppearanceCatalogServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"actor-appearance-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadRigCatalogDiscoversHumanoidFromSupportedClientRootShapes(bool assetsRoot)
    {
        var projectRoot = FindProjectRoot();
        var configuredRoot = assetsRoot
            ? Path.Combine(projectRoot, "prototype", "client", "assets")
            : Path.Combine(projectRoot, "prototype", "client");

        var catalog = CreateService(configuredRoot).LoadRigCatalog();

        Assert.True(catalog.Available, catalog.Message);
        Assert.Contains(catalog.Rigs, rig => rig.RigId == "humanoid_v1");
    }

    [Fact]
    public void LoadOptionsKeepsRigsAvailableWhenOptionalCatalogsAreMissing()
    {
        var projectRoot = FindProjectRoot();
        var source = Path.Combine(projectRoot, "prototype", "client", "actors", "appearance", "data", "rigs", "catalog_v1.json");
        var target = Path.Combine(_root, "assets", "actors", "appearance", "data", "rigs", "catalog_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);

        var options = CreateService(Path.Combine(_root, "assets")).LoadOptions();

        Assert.True(options.Available);
        Assert.True(options.RigsAvailable);
        Assert.Contains(options.VisualModes, mode => mode.Id == "composite_rig");
        Assert.Contains(options.Rigs, rig => rig.RigId == "humanoid_v1");
        Assert.False(options.CalibrationsAvailable);
        Assert.False(options.EquippedVisualsAvailable);
        Assert.Empty(options.Calibrations);
        Assert.Empty(options.EquippedVisuals);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static ActorAppearanceCatalogService CreateService(string gameClientAssets) =>
        new(Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = gameClientAssets
            }
        }));

    private static string FindProjectRoot()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "prototype", "client")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MMO Project client directory for actor appearance integration tests.");
    }
}
