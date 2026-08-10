using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ActorRigCalibrationAuthoringServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"actor-calibration-{Guid.NewGuid():N}");
    private readonly string _assetsRoot;
    private readonly string _catalogPath;
    private readonly ActorAppearanceCatalogService _catalogService;
    private readonly ActorCalibrationFrameResolver _frameResolver;
    private readonly ActorRigCalibrationAuthoringService _service;

    public ActorRigCalibrationAuthoringServiceTests()
    {
        _assetsRoot = Path.Combine(_root, "assets");
        _catalogPath = Path.Combine(_assetsRoot, "actors", "appearance", "data", "rig_calibrations", "catalog_v1.json");
        WriteRigCatalog();
        WriteExactMobFrames();
        WriteCatalog(ExistingCatalog());
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = _assetsRoot
            }
        });
        _catalogService = new ActorAppearanceCatalogService(options);
        _frameResolver = new ActorCalibrationFrameResolver(options);
        _service = new ActorRigCalibrationAuthoringService(_catalogService, _frameResolver);
    }

    [Fact]
    public async Task LoadReturnsExistingCalibrationAndCurrentHashWhileMissingIsNotAnError()
    {
        var existing = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var missing = await _service.LoadAsync("new_actor", TestContext.Current.CancellationToken);

        Assert.True(existing.Succeeded);
        Assert.True(existing.Value!.Exists);
        Assert.Equal("orc_v1", existing.Value.Calibration!.Value.GetProperty("calibration_id").GetString());
        Assert.True(missing.Succeeded);
        Assert.False(missing.Value!.Exists);
        Assert.Equal(existing.Value.CatalogHash, missing.Value.CatalogHash);
        Assert.Null(missing.Value.Calibration);
    }

    [Theory]
    [InlineData("ORC")]
    [InlineData("orc-v1")]
    [InlineData("../orc")]
    [InlineData(" ")]
    public async Task LoadRejectsInvalidCalibrationIds(string calibrationId)
    {
        var result = await _service.LoadAsync(calibrationId, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "invalid_actor_calibration_id");
    }

    [Fact]
    public async Task CreateUpdateMultiplePoseRoundTripAndOmissionReplaceTheCompleteSocketSet()
    {
        var initial = await _service.LoadAsync("new_actor", TestContext.Current.CancellationToken);
        var created = await _service.SaveAsync("new_actor", Request(initial.Value!.CatalogHash, "humanoid_v1", """
            {
              "left_hand_primary": {"N": {"1": {"x": -4, "y": 7}}, "W": {"4": {"x": 8, "y": -9}}},
              "right_hand_primary": {"S": {"2": {"x": 12, "y": 13}}}
            }
            """), TestContext.Current.CancellationToken);

        Assert.True(created.Succeeded);
        Assert.Equal(-4, created.Value!.Calibration!.Value.GetProperty("sockets").GetProperty("left_hand_primary").GetProperty("N").GetProperty("1").GetProperty("x").GetInt32());

        var updated = await _service.SaveAsync("new_actor", Request(created.Value.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": {"E": {"3": {"x": 20, "y": 21}}} }
            """), TestContext.Current.CancellationToken);

        Assert.True(updated.Succeeded);
        var sockets = updated.Value!.Calibration!.Value.GetProperty("sockets");
        Assert.False(sockets.TryGetProperty("left_hand_primary", out _));
        Assert.Equal(20, sockets.GetProperty("right_hand_primary").GetProperty("E").GetProperty("3").GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task SocketSavePreservesForegroundOverlaysAndForwardCompatibleFields()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var saved = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": {"S": {"1": {"x": 24, "y": 136}}} }
            """), TestContext.Current.CancellationToken);

        Assert.True(saved.Succeeded);
        var calibration = saved.Value!.Calibration!.Value;
        Assert.Equal(24, calibration.GetProperty("sockets").GetProperty("right_hand_primary").GetProperty("S").GetProperty("1").GetProperty("x").GetInt32());
        Assert.Equal(24, calibration.GetProperty("foreground_overlays").GetProperty("right_hand_primary_grip").GetProperty("source_rect_by_direction").GetProperty("S").GetProperty("1").GetProperty("x").GetInt32());
        Assert.Equal("preserve", calibration.GetProperty("future_field").GetProperty("mode").GetString());
        using var catalogDocument = JsonDocument.Parse(File.ReadAllText(_catalogPath));
        Assert.Equal("preserve", catalogDocument.RootElement.GetProperty("catalog_future").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task IntegralJsonCoordinatesAreAcceptedAndCanonicalizedAsIntegerTokens()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var saved = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": { "S": { "1": { "x": 25.0, "y": 135.0 } } } }
            """), TestContext.Current.CancellationToken);

        Assert.True(saved.Succeeded);
        Assert.Equal(25, saved.Value!.Calibration!.Value.GetProperty("sockets").GetProperty("right_hand_primary").GetProperty("S").GetProperty("1").GetProperty("x").GetInt32());
        var text = File.ReadAllText(_catalogPath);
        Assert.DoesNotContain("25.0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("135.0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleHashAndRigChangesAreRejected()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var stale = await _service.SaveAsync("orc_v1", Request("stale", "humanoid_v1", "{}"), TestContext.Current.CancellationToken);
        var rigChange = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "other_rig", "{}"), TestContext.Current.CancellationToken);

        Assert.False(stale.Succeeded);
        Assert.Contains(stale.Errors, error => error.Code == "actor_calibration_catalog_conflict");
        Assert.False(rigChange.Succeeded);
        Assert.Contains(rigChange.Errors, error => error.Code == "actor_calibration_rig_immutable");
    }

    [Theory]
    [InlineData("unknown socket", "{\"unknown\": {\"S\": {\"1\": {\"x\": 1, \"y\": 2}}}}", "invalid_actor_socket_id")]
    [InlineData("invalid direction", "{\"right_hand_primary\": {\"south\": {\"1\": {\"x\": 1, \"y\": 2}}}}", "invalid_socket_direction")]
    [InlineData("invalid frame", "{\"right_hand_primary\": {\"S\": {\"F1\": {\"x\": 1, \"y\": 2}}}}", "invalid_socket_frame")]
    [InlineData("fractional x coordinate", "{\"right_hand_primary\": {\"S\": {\"1\": {\"x\": 1.5, \"y\": 2}}}}", "invalid_socket_coordinate")]
    [InlineData("fractional y coordinate", "{\"right_hand_primary\": {\"S\": {\"1\": {\"x\": 1, \"y\": 2.25}}}}", "invalid_socket_coordinate")]
    [InlineData("string coordinate", "{\"right_hand_primary\": {\"S\": {\"1\": {\"x\": \"1\", \"y\": 2}}}}", "invalid_socket_coordinate")]
    [InlineData("out of range coordinate", "{\"right_hand_primary\": {\"S\": {\"1\": {\"x\": 4097, \"y\": 2}}}}", "socket_coordinate_out_of_range")]
    public async Task InvalidSocketMutationsAreRejectedWithoutChangingTheOriginalFile(string _, string socketOverrides, string errorCode)
    {
        var before = File.ReadAllBytes(_catalogPath);
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var result = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", socketOverrides), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == errorCode);
        Assert.Equal(before, File.ReadAllBytes(_catalogPath));
    }

    [Fact]
    public async Task DeterministicSaveUsesNoBomAndNoOpRetainsExactBytes()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var noOp = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": { "S": { "1": { "x": 25, "y": 135 } } } }
            """, """
            { "right_hand_primary_grip": { "S": { "1": { "x": 24, "y": 104, "width": 24, "height": 20 } } } }
            """), TestContext.Current.CancellationToken);

        Assert.True(noOp.Succeeded);
        Assert.Equal(ExistingCatalog(), File.ReadAllText(_catalogPath));

        var changed = await _service.SaveAsync("orc_v1", Request(noOp.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": { "W": { "4": { "x": -4, "y": 3 } }, "N": { "2": { "x": 7, "y": 8 } } } }
            """), TestContext.Current.CancellationToken);

        Assert.True(changed.Succeeded);
        var bytes = File.ReadAllBytes(_catalogPath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.True(text.IndexOf("\"N\"", StringComparison.Ordinal) < text.IndexOf("\"W\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForegroundOverlaySavePreservesSocketsAndForwardFieldsAndUsesExactFrameBounds()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var saved = await _service.SaveAsync("orc_v1", Request(
            loaded.Value!.CatalogHash,
            "humanoid_v1",
            """ { "right_hand_primary": { "S": { "1": { "x": 25, "y": 135 } }, "E": { "2": { "x": 12, "y": 13 } } } } """,
            """ { "right_hand_primary_grip": { "S": { "1": { "x": 24.0, "y": 104.0, "width": 24.0, "height": 20.0 } }, "E": { "2": { "x": 0, "y": 0, "width": 1, "height": 1 } } } } """), TestContext.Current.CancellationToken);

        Assert.True(saved.Succeeded);
        var calibration = saved.Value!.Calibration!.Value;
        Assert.Equal(12, calibration.GetProperty("sockets").GetProperty("right_hand_primary").GetProperty("E").GetProperty("2").GetProperty("x").GetInt32());
        var overlays = calibration.GetProperty("foreground_overlays");
        Assert.Equal(24, overlays.GetProperty("right_hand_primary_grip").GetProperty("source_rect_by_direction").GetProperty("S").GetProperty("1").GetProperty("x").GetInt32());
        Assert.Equal("preserve", calibration.GetProperty("foreground_overlays").GetProperty("right_hand_primary_grip").GetProperty("future_overlay_field").GetString());
        Assert.Equal("preserve", calibration.GetProperty("future_field").GetProperty("mode").GetString());
    }

    [Theory]
    [InlineData("{\"right_hand_primary_grip\":{\"S\":{\"1\":{\"x\":-1,\"y\":0,\"width\":1,\"height\":1}}}}", "foreground_overlay_rectangle_out_of_range")]
    [InlineData("{\"right_hand_primary_grip\":{\"S\":{\"1\":{\"x\":0,\"y\":0,\"width\":0,\"height\":1}}}}", "foreground_overlay_rectangle_out_of_range")]
    [InlineData("{\"right_hand_primary_grip\":{\"S\":{\"1\":{\"x\":0.5,\"y\":0,\"width\":1,\"height\":1}}}}", "invalid_foreground_overlay_rectangle")]
    [InlineData("{\"unknown\":{\"S\":{\"1\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}}}", "invalid_foreground_overlay_id")]
    [InlineData("{\"right_hand_primary_grip\":{\"south\":{\"1\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}}}", "invalid_foreground_overlay_direction")]
    [InlineData("{\"right_hand_primary_grip\":{\"S\":{\"5\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}}}", "invalid_foreground_overlay_frame")]
    [InlineData("{\"right_hand_primary_grip\":{\"S\":{\"1\":{\"x\":160,\"y\":0,\"width\":1,\"height\":1}}}}", "foreground_overlay_rectangle_out_of_bounds")]
    public async Task InvalidForegroundOverlayMutationsDoNotRewriteCatalog(string overlays, string errorCode)
    {
        var before = File.ReadAllBytes(_catalogPath);
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var result = await _service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", "{}", overlays), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == errorCode);
        Assert.Equal(before, File.ReadAllBytes(_catalogPath));
    }

    [Fact]
    public async Task ExternalEditBeforeReplacementReturnsConflictAndPreservesTheExternalBytes()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var externalBytes = Encoding.UTF8.GetBytes(ExternalCatalog());
        var service = CreateService(_ => File.WriteAllBytes(_catalogPath, externalBytes));

        var result = await service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": { "S": { "1": { "x": 23, "y": 136 } } } }
            """), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "actor_calibration_catalog_conflict");
        Assert.Equal(externalBytes, File.ReadAllBytes(_catalogPath));
        Assert.Empty(TemporaryCatalogFiles());

        var reloaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        Assert.True(reloaded.Succeeded);
        Assert.Equal(Hash(externalBytes), reloaded.Value!.CatalogHash);
    }

    [Fact]
    public async Task FileDisappearanceBeforeReplacementFailsWithoutLeavingTheCandidateTempFile()
    {
        var loaded = await _service.LoadAsync("orc_v1", TestContext.Current.CancellationToken);
        var service = CreateService(File.Delete);

        var result = await service.SaveAsync("orc_v1", Request(loaded.Value!.CatalogHash, "humanoid_v1", """
            { "right_hand_primary": { "S": { "1": { "x": 23, "y": 136 } } } }
            """), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "actor_calibration_catalog_unavailable");
        Assert.False(File.Exists(_catalogPath));
        Assert.Empty(TemporaryCatalogFiles());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private SaveActorCalibrationRequest Request(string expectedHash, string rigId, string socketOverrides, string? foregroundOverlayOverrides = null)
    {
        using var document = JsonDocument.Parse(socketOverrides);
        JsonDocument? overlays = foregroundOverlayOverrides is null ? null : JsonDocument.Parse(foregroundOverlayOverrides);
        try
        {
            return new SaveActorCalibrationRequest(
                expectedHash,
                rigId,
                document.RootElement.Clone(),
                overlays?.RootElement.Clone(),
                "mob",
                "res://assets/maps/objects/mobs/orc.png");
        }
        finally
        {
            overlays?.Dispose();
        }
    }

    private ActorRigCalibrationAuthoringService CreateService(Action<string> beforeReplace) =>
        new(_catalogService, _frameResolver, beforeReplace);

    private IReadOnlyList<string> TemporaryCatalogFiles() =>
        Directory.GetFiles(Path.GetDirectoryName(_catalogPath)!, $".{Path.GetFileName(_catalogPath)}.*.tmp");

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private void WriteRigCatalog()
    {
        var path = Path.Combine(_assetsRoot, "actors", "appearance", "data", "rigs", "catalog_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "schema_version": 1,
              "rigs": [
                {
                  "schema_version": 1,
                  "rig_id": "humanoid_v1",
                  "layers": {"body": {"binding_type": "rig_layer", "default_render_plane": "base", "z_index_by_direction": {"N": 0, "E": 0, "S": 0, "W": 0}}},
                  "sockets": {
                    "left_hand_primary": {"N": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "E": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "S": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "W": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}},
                    "right_hand_primary": {"N": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "E": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "S": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}, "W": {"1": {"x": 0, "y": 0}, "2": {"x": 0, "y": 0}, "3": {"x": 0, "y": 0}, "4": {"x": 0, "y": 0}}}
                  },
                  "foreground_overlays": {
                    "right_hand_primary_grip": {"socket_id": "right_hand_primary", "source_layer_id": "body", "z_index_by_direction": {"N": 40, "E": 40, "S": 40, "W": 40}, "source_rect_by_direction": {"S": {"1": {"x": 0, "y": 0, "width": 1, "height": 1}}}},
                    "left_hand_primary_grip": {"socket_id": "left_hand_primary", "source_layer_id": "body", "z_index_by_direction": {"N": 40, "E": 40, "S": 40, "W": 40}, "source_rect_by_direction": {"S": {"4": null}}}
                  }
                }
              ]
            }
            """);
    }

    private void WriteCatalog(string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        File.WriteAllText(_catalogPath, contents, new UTF8Encoding(false));
    }

    private void WriteExactMobFrames()
    {
        WritePng(Path.Combine(_assetsRoot, "maps", "objects", "mobs", "orc.png"), 160, 200);
        foreach (var direction in new[] { "N", "E", "S", "W" })
        {
            foreach (var frame in new[] { 1, 2, 3, 4 })
            {
                WritePng(Path.Combine(_assetsRoot, "actors", "mobs", $"orc-F{frame}-{direction}.png"), 160, 200);
            }
        }
    }

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

    private static string ExistingCatalog() => """
        {
          "schema_version": 1,
          "catalog_future": { "mode": "preserve" },
          "calibrations": [
            {
              "schema_version": 1,
              "calibration_id": "orc_v1",
              "rig_id": "humanoid_v1",
              "sockets": {
                "right_hand_primary": {
                  "S": {
                    "1": { "x": 25, "y": 135 }
                  }
                }
              },
              "foreground_overlays": {
                "right_hand_primary_grip": {
                  "future_overlay_field": "preserve",
                  "source_rect_by_direction": {
                    "S": {
                      "1": { "x": 24, "y": 104, "width": 24, "height": 20 }
                    }
                  }
                }
              },
              "future_field": { "mode": "preserve" }
            }
          ]
        }
        """ + "\n";

    private static string ExternalCatalog() => """
        {
          "schema_version": 1,
          "calibrations": [
            {
              "schema_version": 1,
              "calibration_id": "orc_v1",
              "rig_id": "humanoid_v1",
              "sockets": {
                "right_hand_primary": {
                  "S": {
                    "1": { "x": 91, "y": 92 }
                  }
                }
              }
            }
          ]
        }
        """ + "\n";
}
