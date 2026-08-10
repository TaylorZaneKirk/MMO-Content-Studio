using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ActorCalibrationFrameResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"actor-calibration-frames-{Guid.NewGuid():N}");

    [Fact]
    public void NpcCharsFramesResolveExactRequestedPosesWithoutFallback()
    {
        var assets = CreateAssetsRoot();
        foreach (var fileName in new[]
                 {
                     "Chars_139_200-F2-S.png",
                     "Chars_135_200-F1-N.png",
                     "Chars_130_200-F2-E.png",
                     "Chars_140_200-F3-S.png",
                     "Chars_144_200-F4-W.png"
                 })
        {
            WritePng(Path.Combine(assets, "actors", "npcs", fileName), 64, 96);
        }

        var result = CreateResolver(assets).Resolve(new CalibrationFrameRequest(
            "npc",
            "res://assets/actors/npcs/Chars_139_200-F2-S.png"));

        Assert.True(result.Succeeded);
        Assert.Equal("Chars_135_200-F1-N.png", Path.GetFileName(Frame(result, "N", 1).FilePath));
        Assert.Equal("Chars_130_200-F2-E.png", Path.GetFileName(Frame(result, "E", 2).FilePath));
        Assert.Equal("Chars_140_200-F3-S.png", Path.GetFileName(Frame(result, "S", 3).FilePath));
        Assert.Equal("Chars_144_200-F4-W.png", Path.GetFileName(Frame(result, "W", 4).FilePath));
        Assert.False(Frame(result, "N", 4).Available);
    }

    [Fact]
    public void OrcUsesAllSixteenNormalizedActorFramesInsteadOfItsStaticFallback()
    {
        var assets = CreateAssetsRoot();
        WritePng(Path.Combine(assets, "maps", "objects", "mobs", "orc.png"), 160, 192);
        foreach (var direction in new[] { "N", "E", "S", "W" })
        {
            foreach (var frame in new[] { 1, 2, 3, 4 })
            {
                WritePng(Path.Combine(assets, "actors", "mobs", $"orc-F{frame}-{direction}.png"), 160, 200);
            }
        }

        var result = CreateResolver(assets).Resolve(new CalibrationFrameRequest(
            "mob",
            "res://assets/maps/objects/mobs/orc.png"));

        Assert.True(result.Succeeded);
        Assert.Equal(16, result.Value!.Frames.Count);
        Assert.All(result.Value.Frames, frame =>
        {
            Assert.True(frame.Available);
            Assert.NotNull(frame.FilePath);
            Assert.EndsWith($"orc-F{frame.Frame}-{frame.Direction}.png", frame.FilePath, StringComparison.Ordinal);
            Assert.DoesNotContain("maps/objects/mobs/orc.png", frame.FilePath, StringComparison.Ordinal);
            Assert.True(frame.SourceWidth > 0);
            Assert.True(frame.SourceHeight > 0);
        });
    }

    [Fact]
    public void IncompleteStaticMobReportsUnavailableFramesWithoutCompatibilityFallback()
    {
        var assets = CreateAssetsRoot();
        WritePng(Path.Combine(assets, "maps", "objects", "mobs", "static_mob.png"), 32, 32);

        var result = CreateResolver(assets).Resolve(new CalibrationFrameRequest(
            "mob",
            "res://assets/maps/objects/mobs/static_mob.png"));

        Assert.True(result.Succeeded);
        Assert.All(result.Value!.Frames, frame => Assert.False(frame.Available));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private ActorCalibrationFrameResolver CreateResolver(string assetsRoot) =>
        new(Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = assetsRoot
            }
        }));

    private string CreateAssetsRoot()
    {
        var assets = Path.Combine(_root, "assets");
        Directory.CreateDirectory(assets);
        return assets;
    }

    private static ActorCalibrationFrameDefinition Frame(
        AuthoringOperationResult<ActorCalibrationFramesResponse> result,
        string direction,
        int frame) =>
        Assert.Single(result.Value!.Frames, candidate => candidate.Direction == direction && candidate.Frame == frame);

    private static void WritePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[24];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4e;
        bytes[3] = 0x47;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        File.WriteAllBytes(path, bytes);
    }

}
