// HTTP transport only; FoliageAuthoringService owns the wind edit and save policy.
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Browser;
public static class BrowserWind
{
    public static void MapBrowserWind(this WebApplication app)
    {
        var group = app.MapGroup("/studio/api/wind");
        group.MapGet("", (FoliageAuthoringService s) => Read(() => s.Catalog()));
        group.MapGet("/family", (string id, FoliageAuthoringService s) => Read(() => s.Load(id)));
        group.MapGet("/source", (string id, string file, int tile, int columns, int rows, FoliageAuthoringService s) => Read(() => s.New(id,new(file,tile,columns,rows))));
        group.MapGet("/shader", (FoliageAuthoringService s) => Read(() => new { source = s.Shader() }));
        group.MapPost("/preview", (HttpRequest r, FoliageAuthoringService s, CancellationToken ct) => Edit(r,s,false,ct));
        group.MapPut("/save", (HttpRequest r, FoliageAuthoringService s, CancellationToken ct) => Edit(r,s,true,ct));
    }
    private static IResult Read(Func<object> action)
    {
        try { return BrowserJson.Json(action()); }
        catch (TileEditException ex) { return BrowserJson.Error("wind_error",ex.Message,ex.Status); }
    }
    private static async Task<IResult> Edit(HttpRequest request,FoliageAuthoringService service,bool save,CancellationToken ct)
    {
        try
        {
            var value = await BrowserJson.Read<WindRequest>(request,ct);
            return value is null ? BrowserJson.Error("invalid_request","Wind settings are required.") : BrowserJson.Json(await service.Edit(value,save,ct));
        }
        catch (TileEditException ex) { return BrowserJson.Error("wind_error",ex.Message,ex.Status); }
    }
}
