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
