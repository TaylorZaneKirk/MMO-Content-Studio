// Thin browser transport. TileMetadataAuthoringService owns the file-edit decision.
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserTiles
{
    public static void MapBrowserTiles(this WebApplication app)
    {
        var group = app.MapGroup("/studio/api/tiles");
        group.MapGet("", (TileMetadataAuthoringService service) => Run(() => service.Catalog()));
        group.MapGet("/tileset", (string file, TileMetadataAuthoringService service) => Run(() => service.Load(file)));
        group.MapGet("/references", async (TileMetadataAuthoringService service, CancellationToken ct) => BrowserJson.Json(await service.References(ct)));
        group.MapGet("/image", (string file, int id, TileMetadataAuthoringService service) =>
        {
            try { return Results.File(service.Image(file, id), "image/png"); }
            catch (TileEditException ex) { return BrowserJson.Error("tile_image_unavailable", ex.Message, ex.Status); }
        });
        group.MapPost("/preview", (string file, int id, HttpRequest request, TileMetadataAuthoringService service, CancellationToken ct) => Edit(file, id, request, service, false, ct));
        group.MapPut("/save", (string file, int id, HttpRequest request, TileMetadataAuthoringService service, CancellationToken ct) => Edit(file, id, request, service, true, ct));
    }
    private static IResult Run(Func<object> action)
    {
        try { return BrowserJson.Json(action()); }
        catch (TileEditException ex) { return BrowserJson.Error("tile_metadata_error", ex.Message, ex.Status); }
    }
    private static async Task<IResult> Edit(string file, int id, HttpRequest request, TileMetadataAuthoringService service, bool save, CancellationToken ct)
    {
        try
        {
            var value = await BrowserJson.Read<TileMetadataRequest>(request, ct);
            return value is null ? BrowserJson.Error("invalid_request", "Tile metadata is required.") : BrowserJson.Json(await service.Edit(file, id, value, save, ct));
        }
        catch (TileEditException ex) { return BrowserJson.Error("tile_metadata_error", ex.Message, ex.Status); }
    }
}
