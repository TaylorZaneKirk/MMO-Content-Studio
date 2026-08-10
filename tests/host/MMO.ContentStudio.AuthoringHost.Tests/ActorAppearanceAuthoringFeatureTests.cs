using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.ActorAppearance;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ActorAppearanceAuthoringFeatureTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"actor-appearance-endpoints-{Guid.NewGuid():N}");
    private WebApplication? _app;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var assets = Path.Combine(_root, "assets");
        WriteRigCatalog(assets);
        WriteCalibrationCatalog(assets);
        WritePng(Path.Combine(assets, "maps", "objects", "mobs", "static_mob.png"));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<AssetRootsOptions>(options => options.Roots["game_client_assets"] = assets);
        builder.Services.AddActorAppearanceAuthoring();
        _app = builder.Build();
        _app.MapActorAppearanceAuthoring();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync(TestContext.Current.CancellationToken);
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task RoutesUseTheV1EnvelopeForLoadSaveConflictValidationAndFrameAvailability()
    {
        var missing = await GetEnvelope("/api/v1/actor-appearance/calibrations/new_actor");
        Assert.True(missing.GetProperty("success").GetBoolean());
        Assert.False(missing.GetProperty("data").GetProperty("exists").GetBoolean());
        var hash = missing.GetProperty("data").GetProperty("catalog_hash").GetString();
        Assert.False(string.IsNullOrWhiteSpace(hash));

        using var invalidLoadResponse = await _client!.GetAsync(
            "/api/v1/actor-appearance/calibrations/ORC",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLoadResponse.StatusCode);
        using var invalidLoadDocument = JsonDocument.Parse(await invalidLoadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("invalid_actor_calibration_id", invalidLoadDocument.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());

        var saveResponse = await _client!.PutAsJsonAsync(
            "/api/v1/actor-appearance/calibrations/new_actor",
            new
            {
                expected_catalog_hash = hash,
                rig_id = "humanoid_v1",
                socket_overrides = new Dictionary<string, object>
                {
                    ["right_hand_primary"] = new Dictionary<string, object>
                    {
                        ["S"] = new Dictionary<string, object> { ["1"] = new { x = 4, y = 5 } }
                    }
                }
            },
            TestContext.Current.CancellationToken);
        var savePayload = await saveResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(saveResponse.StatusCode == HttpStatusCode.OK, savePayload);
        using var saveDocument = JsonDocument.Parse(savePayload);
        Assert.True(saveDocument.RootElement.GetProperty("success").GetBoolean());

        var staleResponse = await _client!.PutAsJsonAsync(
            "/api/v1/actor-appearance/calibrations/new_actor",
            new { expected_catalog_hash = "stale", rig_id = "humanoid_v1", socket_overrides = new { } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var staleDocument = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("actor_calibration_catalog_conflict", staleDocument.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());

        var invalidResponse = await _client!.PutAsJsonAsync(
            "/api/v1/actor-appearance/calibrations/new_actor",
            new
            {
                expected_catalog_hash = saveDocument.RootElement.GetProperty("data").GetProperty("catalog_hash").GetString(),
                rig_id = "humanoid_v1",
                socket_overrides = new { right_hand_primary = new { south = new Dictionary<string, object> { ["1"] = new { x = 4, y = 5 } } } }
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var frameResponse = await _client!.PostAsJsonAsync(
            "/api/v1/actor-appearance/calibration-frames",
            new { actor_kind = "mob", visual_texture_path = "res://assets/maps/objects/mobs/static_mob.png" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
        using var frameDocument = JsonDocument.Parse(await frameResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(16, frameDocument.RootElement.GetProperty("data").GetProperty("frames").GetArrayLength());
    }

    private async Task<JsonElement> GetEnvelope(string path)
    {
        using var response = await _client!.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static void WriteRigCatalog(string assets)
    {
        var path = Path.Combine(assets, "actors", "appearance", "data", "rigs", "catalog_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"schema_version":1,"rigs":[{"schema_version":1,"rig_id":"humanoid_v1","layers":{"body":{"binding_type":"rig_layer","default_render_plane":"base","z_index_by_direction":{"N":0,"E":0,"S":0,"W":0}}},"sockets":{"right_hand_primary":{"N":{"1":{"x":0,"y":0},"2":{"x":0,"y":0},"3":{"x":0,"y":0},"4":{"x":0,"y":0}},"E":{"1":{"x":0,"y":0},"2":{"x":0,"y":0},"3":{"x":0,"y":0},"4":{"x":0,"y":0}},"S":{"1":{"x":0,"y":0},"2":{"x":0,"y":0},"3":{"x":0,"y":0},"4":{"x":0,"y":0}},"W":{"1":{"x":0,"y":0},"2":{"x":0,"y":0},"3":{"x":0,"y":0},"4":{"x":0,"y":0}}}}}]}
            """);
    }

    private static void WriteCalibrationCatalog(string assets)
    {
        var path = Path.Combine(assets, "actors", "appearance", "data", "rig_calibrations", "catalog_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schema_version\":1,\"calibrations\":[]}");
    }

    private static void WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[24];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4e;
        bytes[3] = 0x47;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 32);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 32);
        File.WriteAllBytes(path, bytes);
    }
}
