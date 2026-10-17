// Owns one wind authoring decision: load family/source, validate mask, preview,
// then publish derived PNGs by atomically replacing the existing family manifest.
// Native art, TSX, collisions, harvest rules and live publication are never written.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed record WindSource(string File, int Tile, int Columns, int Rows);
public sealed record WindRequest(string Id, string ExpectedHash, bool Enabled, string Profile,
    byte[] Mask, WindSource? Source, string? PreviewSignature);
public sealed record WindDraft(string Id, string Hash, bool Enabled, string Profile, int Width, int Height,
    byte[] Rgba, byte[] Mask, WindSource? Source, JsonNode Family);

public sealed class FoliageAuthoringService(ItemAssetService assets, TileMetadataAuthoringService tiles)
{
    private const string Prefix = "res://assets/maps/animations/foliage/";
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly byte[] _previewKey = RandomNumberGenerator.GetBytes(32);
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public object Catalog()
    {
        var manifest = Manifest();
        return new { profiles = manifest["profiles"]!.DeepClone(), families = Families(manifest).Select(f => new {
            id = f["id"]!.GetValue<string>(), profile = f["profile"]!.GetValue<string>(), enabled = f["enabled"]?.GetValue<bool>() ?? true
        }).ToArray() };
    }
    public string Shader()
    {
        var root = assets.GetGameAssetsRoot() ?? throw new TileEditException("Configure game_client_assets first.", 503);
        var path = Path.GetFullPath(Path.Combine(root, "..", "screens", "game", "shaders", "foliage_wind.gdshader"));
        NoLinks(path);
        return File.ReadAllText(path); // Fixed game resource, not a browser-provided path.
    }
    public WindDraft Load(string id)
    {
        var manifest = Manifest();
        var family = Families(manifest).SingleOrDefault(f => f["id"]!.GetValue<string>() == id)
            ?? throw new TileEditException("Wind family not found.", 404);
        var size = family["size"]!.AsArray();
        int width = size[0]!.GetValue<int>(), height = size[1]!.GetValue<int>();
        var artBytes = ReadImage(family["art"]!.GetValue<string>());
        var maskBytes = ReadImage(family["mask"]!.GetValue<string>());
        var art = FoliagePng.Crop(FoliagePng.Decode(artBytes), 3, 3, width, height);
        var encoded = FoliagePng.Crop(FoliagePng.Decode(maskBytes), 3, 3, width, height);
        var flags = Enumerable.Range(0, width * height).Select(i => encoded.Rgba[i * 4]).ToArray();
        return new(id, Hash(ManifestBytes().Concat(artBytes).Concat(maskBytes).ToArray()),
            family["enabled"]?.GetValue<bool>() ?? true, family["profile"]!.GetValue<string>(), width, height,
            art.Rgba, flags, null, family.DeepClone());
    }
    public WindDraft New(string id, WindSource source)
    {
        CheckId(id);
        if (source.Columns is < 1 or > 7 || source.Rows is < 1 or > 7)
            throw new TileEditException("Choose a rectangle between 1 and 7 tiles per side.");
        var manifest = Manifest();
        if (Families(manifest).Any(f => f["id"]!.GetValue<string>() == id)) throw new TileEditException("That family ID already exists. Open it instead.", 409);
        var first = tiles.Pixels(source.File, source.Tile);
        if ((first.Columns == 0 && (source.Columns != 1 || source.Rows != 1)) ||
            (first.Columns > 0 && source.Tile % first.Columns + source.Columns > first.Columns))
            throw new TileEditException("Choose a rectangle inside one atlas row range; collection images support one tile.");
        int width = source.Columns * 32, height = source.Rows * 32;
        var rgba = new byte[width * height * 4]; var cells = new JsonArray();
        var existing = Families(manifest).SelectMany(f => f["cells"]!.AsArray()).Select(c => c!["identity"]!.GetValue<string>()).ToHashSet();
        for (int y = 0; y < source.Rows; y++) for (int x = 0; x < source.Columns; x++)
        {
            var tile = tiles.Pixels(source.File, source.Tile + y * first.Columns + x);
            if (existing.Contains(tile.Identity)) throw new TileEditException($"{tile.Identity} already belongs to a wind family. Edit that family instead.");
            for (int row = 0; row < 32; row++) tile.Pixels.Rgba.AsSpan(row * 128, 128).CopyTo(rgba.AsSpan(((y * 32 + row) * width + x * 32) * 4));
            cells.Add(new JsonObject { ["identity"] = tile.Identity, ["offset"] = new JsonArray(x, y), ["source"] = new JsonArray(x, y) });
        }
        // A new family starts fixed, so enabling it cannot accidentally sway its roots.
        var flags = Enumerable.Range(0, width * height).Select(i => rgba[i * 4 + 3] == 0 ? (byte)0 : (byte)255).ToArray();
        var family = new JsonObject { ["id"] = id, ["profile"] = "stem", ["size"] = new JsonArray(width,height),
            ["plant_size"] = new JsonArray(width,height), ["frame_columns"] = 1, ["layer"] = "tile_foliage",
            ["cells"] = cells, ["guards"] = new JsonArray() };
        return new(id, Hash(ManifestBytes().Concat(rgba).Concat(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source) + family.ToJsonString())).ToArray()),
            true, "stem", width, height, rgba, flags, source, family);
    }
    public async Task<object> Edit(WindRequest request, bool save, CancellationToken cancellation)
    {
        await _saveGate.WaitAsync(cancellation);
        try
        {
            CheckId(request.Id);
            var current = request.Source is null ? Load(request.Id) : New(request.Id, request.Source);
            if (current.Hash != request.ExpectedHash) throw new TileEditException("Source changed. Reload and compare before saving.", 409);
            var manifest = Manifest();
            if (manifest["profiles"]?[request.Profile] is not JsonObject profile) throw new TileEditException("Choose an existing wind profile.");
            if (request.Mask is null || request.Mask.Length != current.Width * current.Height) throw new TileEditException("Mask dimensions must match the family.");
            bool fixedPixel = false;
            for (int i = 0; i < request.Mask.Length; i++)
            {
                bool opaque = current.Rgba[i * 4 + 3] > 0;
                if (opaque ? request.Mask[i] is not (127 or 255) : request.Mask[i] != 0)
                    throw new TileEditException("Paint only opaque art: moving or fixed. Transparent pixels stay empty.");
                fixedPixel |= request.Mask[i] == 255;
            }
            if (!fixedPixel) throw new TileEditException("Keep at least one fixed root or trunk pixel.");
            var unsigned = request with { PreviewSignature = null };
            var signature = Convert.ToHexString(HMACSHA256.HashData(_previewKey, JsonSerializer.SerializeToUtf8Bytes(unsigned))).ToLowerInvariant();
            if (!save) return new { preview_signature = signature, draft = current with { Mask = request.Mask, Enabled = request.Enabled, Profile = request.Profile }, profile = profile.DeepClone(), scope = "Every matching placement in the supported world layers; collision and harvest behavior stay unchanged." };
            if (request.PreviewSignature != signature) throw new TileEditException("Preview these exact settings before saving.", 409);
            cancellation.ThrowIfCancellationRequested();
            var family = current.Family.DeepClone().AsObject();
            family["profile"] = request.Profile; family["enabled"] = request.Enabled;
            // Content-addressed images are installed first. A crash cannot make the
            // old manifest point at a half-updated mask/art pair. Old files remain usable.
            var images = Images(current, request.Mask);
            string revision = Hash(images.Art.Concat(images.Encoded).ToArray());
            foreach (var (field, suffix, bytes) in new[] { ("art", "art", images.Art), ("mask", "coordinates", images.Encoded), ("authored_mask", "mask", images.Authored) })
            {
                string name = $"{request.Id}-{revision}-{suffix}.png";
                var path = PathInRoot(name);
                if (File.Exists(path)) { if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new TileEditException("Derived image conflict.",409); }
                else AtomicWrite(path, bytes);
                family[field] = Prefix + name;
            }
            var latest = request.Source is null ? Load(request.Id) : New(request.Id, request.Source);
            if (latest.Hash != current.Hash) throw new TileEditException("Source changed while saving. Reload and compare.",409);
            var families = manifest["families"]!.AsArray();
            int index = families.ToList().FindIndex(f => f!["id"]!.GetValue<string>() == request.Id);
            if (index < 0) families.Add(family); else families[index] = family;
            AtomicWrite(PathInRoot("families.json"), Encoding.UTF8.GetBytes(manifest.ToJsonString(Pretty) + "\n"));
            return new { saved = true, draft = Load(request.Id), message = "Saved source assets. Use the existing client export/publication workflow; no live package changed." };
        }
        finally { _saveGate.Release(); }
    }
    private static (byte[] Art, byte[] Encoded, byte[] Authored) Images(WindDraft draft, byte[] flags)
    {
        int w = draft.Width, h = draft.Height, paddedWidth = w + 6;
        var art = new byte[(w+6)*(h+6)*4]; var encoded = new byte[art.Length]; var authored = new byte[w*h*4];
        for (int y = 0; y < h+6; y++) for (int x = 0; x < w+6; x++)
        {
            int pixel = (y*paddedWidth+x)*4; byte flag = 0;
            if (x >= 3 && x < w+3 && y >= 3 && y < h+3)
            {
                int original = (y-3)*w+x-3; flag = flags[original];
                draft.Rgba.AsSpan(original*4,4).CopyTo(art.AsSpan(pixel));
                authored[original*4] = flag == 127 ? (byte)255 : (byte)0;
                authored[original*4+1] = flag == 255 ? (byte)255 : (byte)0;
                authored[original*4+3] = flag == 0 ? (byte)0 : (byte)255;
            }
            encoded[pixel] = flag; encoded[pixel+1] = (byte)x; encoded[pixel+2] = (byte)y; encoded[pixel+3] = 255;
        }
        return (FoliagePng.Encode(new(w+6,h+6,art)), FoliagePng.Encode(new(w+6,h+6,encoded)), FoliagePng.Encode(new(w,h,authored)));
    }
    private JsonObject Manifest() => JsonNode.Parse(ManifestBytes())?.AsObject() ?? throw new TileEditException("Missing foliage manifest.",503);
    private byte[] ManifestBytes()
    {
        var path = PathInRoot("families.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024*1024) throw new TileEditException("Foliage manifest is unavailable or too large.",503);
        return File.ReadAllBytes(path);
    }
    private static IEnumerable<JsonObject> Families(JsonObject manifest) => manifest["families"]!.AsArray().Select(n => n!.AsObject());
    private byte[] ReadImage(string resource)
    {
        if (!resource.StartsWith(Prefix, StringComparison.Ordinal)) throw new TileEditException("Wind images must stay in the foliage asset folder.");
        var path = PathInRoot(resource[Prefix.Length..]);
        if (!File.Exists(path) || new FileInfo(path).Length > 16*1024*1024) throw new TileEditException("Foliage image unavailable.");
        return File.ReadAllBytes(path);
    }
    private string PathInRoot(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name || name.Contains('\\') || name.Contains(':')) throw new TileEditException("Invalid foliage filename.");
        var root = assets.GetGameAssetsRoot() ?? throw new TileEditException("Configure game_client_assets first.",503);
        var path = Path.GetFullPath(Path.Combine(root,"maps","animations","foliage",name)); NoLinks(path); return path;
    }
    private static void NoLinks(string path)
    {
        for (FileSystemInfo? current = new FileInfo(path); current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))) throw new TileEditException("Linked foliage paths are not supported.");
    }
    private static void CheckId(string id)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9_]{0,63}$")) throw new TileEditException("Use a lowercase family ID with letters, digits and underscores (up to 64 characters).");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void AtomicWrite(string path, byte[] bytes)
    {
        NoLinks(path);
        var temporary = path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path) && !OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary,File.GetUnixFileMode(path));
            NoLinks(path); File.Move(temporary,path,true);
        }
        finally { File.Delete(temporary); }
    }
}
