using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class TileArtMetadataTests
{
    [Fact]
    public async Task ArtMetadataSavesReloadsAndSearchesAcrossTilesetsWithoutChangingOtherProperties()
    {
        var root = Path.Combine(Path.GetTempPath(), "tile-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string untouched = "<property name=\"custom_note\" value=\"keep &amp; preserve\"/>";
            const string source = "<tileset name=\"doors\"><tile id=\"4\"><properties>" + untouched + "<property name=\"slick\" type=\"bool\" value=\"false\"/></properties><image source=\"door.png\" width=\"32\" height=\"32\"/></tile></tileset>";
            File.WriteAllText(Path.Combine(root, "doors.tsx"), source);
            File.WriteAllText(Path.Combine(root, "roof.tsx"), "<tileset name=\"roof\" tilewidth=\"32\" tileheight=\"32\" columns=\"1\" tilecount=\"1\"><image source=\"roof.png\" width=\"32\" height=\"32\"/></tileset>");
            var options = Options.Create(new AssetRootsOptions { Roots = new() { ["tiled_tilesets"] = root, ["game_client_assets"] = root } });
            // Art-only edits never consult gameplay reference catalogs.
            var service = new TileMetadataAuthoringService(options, new ItemAssetService(options), null!, null!, null!);
            foreach (var (file, id, label) in new[] { ("doors.tsx", 4, "Timber door"), ("roof.tsx", 0, "Timber roof corner") })
            {
                var loaded = JsonSerializer.SerializeToElement(service.Load(file));
                var request = new TileMetadataRequest(loaded.GetProperty("hash").GetString()!, new() { ["art_label"] = label, ["art_tags"] = "rustic, northwest" }, null);
                var preview = JsonSerializer.SerializeToElement(await service.Edit(file, id, request, false, TestContext.Current.CancellationToken));
                await service.Edit(file, id, request with { PreviewSignature = preview.GetProperty("preview_signature").GetString() }, true, TestContext.Current.CancellationToken);
                var reloaded = JsonSerializer.SerializeToElement(service.Load(file));
                Assert.Contains(reloaded.GetProperty("tiles")[0].GetProperty("Properties").EnumerateArray(), p => p.GetProperty("Name").GetString() == "art_label" && p.GetProperty("Value").GetString() == label && p.GetProperty("Editable").GetBoolean());
                var conflict = await Assert.ThrowsAsync<TileEditException>(() => service.Edit(file, id, request, false, TestContext.Current.CancellationToken));
                Assert.Equal(409, conflict.Status);
            }
            var saved = File.ReadAllText(Path.Combine(root, "doors.tsx"));
            Assert.Contains(untouched, saved);
            Assert.Contains("<property name=\"slick\" type=\"bool\" value=\"false\"/>", saved);
            var result = service.Search("TIMBER", TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "doors.tsx", "roof.tsx" }, result.Matches.Select(m => m.File));
            Assert.Equal(new[] { 4, 0 }, result.Matches.Select(m => m.Id));
            Assert.Equal(2, service.Search("northwest", TestContext.Current.CancellationToken).Matches.Count);
            Assert.False(result.Truncated);
            Assert.Empty(result.Warnings);
        }
        finally { Directory.Delete(root, true); }
    }
}
