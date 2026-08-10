using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class RiggedSpritePreviewResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rigged-preview-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("N", 1, "Chars_135_200-F1-N.png")]
    [InlineData("E", 2, "Chars_130_200-F2-E.png")]
    [InlineData("S", 1, "Chars_138_200-F1-S.png")]
    [InlineData("W", 3, "Chars_134_200-F3-W.png")]
    public void CharsFamilyResolvesCurrentRuntimePose(string direction, int frame, string expectedFileName)
    {
        var seed = Write("Chars_139_200-F2-S.png");
        var expected = Write(expectedFileName);

        var resolved = RiggedSpritePreviewResolver.ResolveBaseFrame(seed, direction, frame);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void CharsFamilyFallsBackToDirectionalFirstFrameThenSeed()
    {
        var seed = Write("Chars_139_200-F2-S.png");
        var directionalFirst = Write("Chars_129_200-F1-E.png");

        Assert.Equal(directionalFirst, RiggedSpritePreviewResolver.ResolveBaseFrame(seed, "E", 3));
        Assert.Equal(seed, RiggedSpritePreviewResolver.ResolveBaseFrame(seed, "W", 2));
    }

    [Theory]
    [InlineData("N", "Chars_142_200-F4-N.png")]
    [InlineData("E", "Chars_143_200-F4-E.png")]
    [InlineData("S", "Chars_141_200-F4-S.png")]
    [InlineData("W", "Chars_144_200-F4-W.png")]
    public void ExactCharsFrameResolutionUsesTheDistinctFourthFrameFiles(string direction, string expectedFileName)
    {
        var seed = Write("Chars_139_200-F2-S.png");
        var expected = Write(expectedFileName);

        Assert.Equal(expected, RiggedSpritePreviewResolver.ResolveExactCharsFrame(seed, direction, 4));
        Assert.Equal(expected, RiggedSpritePreviewResolver.ResolveBaseFrame(seed, direction, 4));
    }

    [Fact]
    public void NormalizedFamilyAndSingleImageKeepTheirExistingFallbacks()
    {
        var normalizedSeed = Write("Guard-F2-S.png");
        var normalizedTarget = Write("Guard-F1-N.png");
        var singleImage = Write("orc.png");

        Assert.Equal(normalizedTarget, RiggedSpritePreviewResolver.ResolveBaseFrame(normalizedSeed, "N", 1));
        Assert.Equal(singleImage, RiggedSpritePreviewResolver.ResolveBaseFrame(singleImage, "W", 3));
    }

    [Fact]
    public void CanonicalFixedOrcPreviewUsesItsStaticBaseAndAxeAttachment()
    {
        var assetsRoot = FindCanonicalAssetsRoot();
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = assetsRoot
            }
        });
        var catalogService = new ActorAppearanceCatalogService(options);
        var resolver = new RiggedSpritePreviewResolver(catalogService, new ItemAssetService(options));
        var basePath = Path.Combine(assetsRoot, "maps", "objects", "mobs", "orc.png");

        var result = resolver.Resolve(
            basePath,
            160,
            192,
            new RiggedSpriteVisualDescriptor(
                1,
                "humanoid_v1",
                "orc_v1",
                "fixed",
                "S",
                1,
                new Dictionary<string, string> { ["right_hand"] = "inventory_154_axe" }),
            "N",
            2);

        Assert.NotNull(result);
        Assert.EndsWith("maps/objects/mobs/orc.png", result!.BaseFilePath.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Equal(160, result.SourceWidth);
        Assert.Equal(192, result.SourceHeight);
        var cosmetic = Assert.Single(result.Cosmetics);
        Assert.Equal("inventory_154_axe", cosmetic.ItemId);
        Assert.EndsWith("actors/player/right_hand/axe-F1-S.png", cosmetic.FilePath.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Equal(-26, cosmetic.X);
        Assert.Equal(45, cosmetic.Y);
        var overlay = Assert.Single(result.ForegroundOverlays);
        Assert.Equal("right_hand_primary_grip", overlay.OverlayId);
        Assert.Equal(24, overlay.SourceRect.X);
        Assert.Equal(104, overlay.SourceRect.Y);
    }

    [Fact]
    public void ActorPoseOrcPreviewUsesNormalizedFamilyAndResolvedDimensions()
    {
        var assetsRoot = FindCanonicalAssetsRoot();
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = assetsRoot
            }
        });
        var resolver = new RiggedSpritePreviewResolver(
            new ActorAppearanceCatalogService(options),
            new ItemAssetService(options));
        var basePath = Path.Combine(assetsRoot, "maps", "objects", "mobs", "orc.png");

        var result = resolver.Resolve(
            basePath,
            160,
            192,
            new RiggedSpriteVisualDescriptor(
                1,
                "humanoid_v1",
                "orc_v1",
                "actor_pose",
                null,
                null,
                new Dictionary<string, string>()),
            "N",
            2);

        Assert.NotNull(result);
        Assert.EndsWith("actors/mobs/orc-F2-N.png", result!.BaseFilePath.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Equal(160, result.SourceWidth);
        Assert.Equal(204, result.SourceHeight);
    }

    [Theory]
    [InlineData("N", -1)]
    [InlineData("S", 10)]
    [InlineData("E", 10)]
    [InlineData("W", -20)]
    public void SolidActorHeldItemDepthUsesDirectionalPresentation(string direction, int expectedZ)
    {
        var assetsRoot = FindCanonicalAssetsRoot();
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = assetsRoot
            }
        });
        var resolver = new RiggedSpritePreviewResolver(
            new ActorAppearanceCatalogService(options),
            new ItemAssetService(options));

        var result = resolver.Resolve(
            Path.Combine(assetsRoot, "maps", "objects", "mobs", "orc.png"),
            160,
            192,
            new RiggedSpriteVisualDescriptor(
                1,
                "humanoid_v1",
                "orc_v1",
                "fixed",
                direction,
                1,
                new Dictionary<string, string> { ["right_hand"] = "inventory_154_axe" }),
            "S",
            1);

        Assert.NotNull(result);
        Assert.Equal(expectedZ, Assert.Single(result!.Cosmetics).ZIndex);
    }

    private static string FindCanonicalAssetsRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "prototype", "client", "assets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The canonical MMO Project client assets are unavailable for this integration test.");
    }

    private string Write(string fileName)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
