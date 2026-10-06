using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Browser;
// Existing Items scope is unchanged by sharing PNG validation and upload handling.
public sealed class BrowserItemAssets(ItemAssetService assets)
    : BrowserPngAssets(assets, "items", "items", "/studio/api/asset");
// World Object contracts allow any canonical game PNG. Uploads have one fixed destination.
public sealed class BrowserWorldObjectAssets(ItemAssetService assets)
    : BrowserPngAssets(assets, "", "maps/objects/world_objects", "/studio/api/world-objects/asset");
