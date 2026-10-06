// Owns the Tiles workflow: confined TSX discovery, metadata editing, preview and file save.
// Source files remain authoritative. This service never publishes maps or changes the database.
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed record TileMetadataRequest(string ExpectedHash, Dictionary<string, string?> Changes, string? PreviewSignature);
public sealed record TileReference(string Id, string Name, string PublicationState);
public sealed record TileReferenceCatalog(bool Available, IReadOnlyList<TileReference> Items);
public sealed record TileProperty(string Name, string Type, string? Value, bool Editable);
public sealed record TileImage(string Label, int X, int Y, int Width, int Height, bool Available);
public sealed record TileEntry(int Id, TileImage Image, IReadOnlyList<TileProperty> Properties, string[] EditableFields);

public sealed class TileMetadataAuthoringService(
    IOptions<AssetRootsOptions> roots, ItemAssetService assets,
    WorldObjectAuthoringService worldObjects, UnifiedItemAuthoringService items, MobAuthoringService mobs)
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly byte[] _previewKey = RandomNumberGenerator.GetBytes(32);
    private const int MaximumXmlBytes = 4 * 1024 * 1024;
    private static readonly string[] FlagFields = ["ignitable", "slick"];
    private static readonly string[] ReferenceFields = ["world_object_definition_id", "item_id", "mob_definition_id"];

    public object Catalog()
    {
        var root = Root();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var files = Directory.EnumerateFiles(root, "*.tsx", options).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray();
        return new { tilesets = files, target_label = "Configured Tiled tilesets", formats = new[] { "TSX" } };
    }
    public object Load(string file)
    {
        var document = Read(file);
        var entries = Entries(document, FilePath(file));
        return new { file, name = (string?)document.Root.Attribute("name"), hash = Hash(document.Bytes),
            atlas = document.Root.Element("image") is not null, tiles = entries };
    }
    public byte[] Image(string file, int id)
    {
        var document = Read(file);
        if (id < 0 || (document.Root.Element("image") is not null
            ? id >= TilesetXml.Number(document.Root, "tilecount") : document.Tile(id) is null))
            throw new TileEditException("Tile not found.", 404);
        var image = document.Root.Element("image") ?? document.Tile(id)?.Element("image");
        var path = ImagePath(FilePath(file), (string?)image?.Attribute("source"));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new TileEditException("Tile image exceeds 16 MiB.");
        using var buffer = new MemoryStream();
        var block = new byte[8192]; int count;
        while ((count = stream.Read(block)) > 0)
        {
            if (buffer.Length + count > 16 * 1024 * 1024) throw new TileEditException("Tile image exceeds 16 MiB.");
            buffer.Write(block, 0, count);
        }
        var bytes = buffer.ToArray();
        if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new TileEditException("Tile image must be a PNG no larger than 16 MiB.");
        return bytes;
    }
    public async Task<Dictionary<string, TileReferenceCatalog>> References(CancellationToken ct)
    {
        var result = new Dictionary<string, TileReferenceCatalog>();
        foreach (var name in ReferenceFields) result[name] = await ReferenceCatalog(name, ct);
        return result;
    }
    private async Task<TileReferenceCatalog> ReferenceCatalog(string name, CancellationToken ct)
    {
        switch (name)
        {
            case "world_object_definition_id":
                var world = await worldObjects.ListAsync(null, ct);
                return new(world.Succeeded && world.Value is not null, world.Value?.Items.Select(x => new TileReference(x.DefinitionId, x.DisplayName, x.PublicationState)).ToArray() ?? []);
            case "item_id":
                var item = await items.ListAsync(null, ct);
                return new(item.Succeeded && item.Value is not null, item.Value?.Items.Select(x => new TileReference(x.ItemId, x.DisplayName, x.PublicationState)).ToArray() ?? []);
            case "mob_definition_id":
                var mob = await mobs.ListAsync(null, ct);
                return new(mob.Succeeded && mob.Value is not null, mob.Value?.Items.Select(x => new TileReference(x.MobDefinitionId, x.DisplayName, x.PublicationState)).ToArray() ?? []);
            default: throw new TileEditException("Unsupported reference field.");
        }
    }
    public async Task<object> Edit(string file, int id, TileMetadataRequest request, bool save, CancellationToken ct)
    {
        // One in-process owner serializes writes; Tiled does not participate in this lock.
        await _saveGate.WaitAsync(ct);
        try
        {
            var path = FilePath(file);
            var document = Read(file);
            if (request.ExpectedHash != Hash(document.Bytes)) throw Conflict();
            var tile = Entries(document, path).SingleOrDefault(t => t.Id == id) ?? throw new TileEditException("Tile not found.", 404);
            if (request.Changes is null || request.Changes.Count > 5) throw new TileEditException("Provide only the edited metadata fields.");
            var changes = new List<object>();
            var warnings = new List<string>();
            foreach (var (name, value) in request.Changes)
            {
                if (!tile.EditableFields.Contains(name)) throw new TileEditException("This field is not editable for this tile or uses an unsupported XML structure.");
                if (FlagFields.Contains(name) && value is not null and not "true" and not "false")
                    throw new TileEditException("Flags must be unset, true or false.");
                if (value is not null && (value.Length > 256 || value.Any(char.IsControl))) throw new TileEditException("Reference values must be short single-line IDs.");
                var before = tile.Properties.FirstOrDefault(p => p.Name == name)?.Value;
                if (before == value) continue;
                if (ReferenceFields.Contains(name) && value is not null)
                {
                    var catalog = await ReferenceCatalog(name, ct);
                    if (!catalog.Available) throw new TileEditException("The reference catalog is unavailable. Retry when it is available; existing references have been preserved.", 503);
                    var reference = catalog.Items.FirstOrDefault(x => x.Id == value);
                    if (reference is null) throw new TileEditException("Choose an existing catalog reference. Unknown existing references are preserved until explicitly changed.");
                    if (reference.PublicationState != "Published") warnings.Add($"{name}: the selected definition is {reference.PublicationState}; publish it separately before map import.");
                }
                changes.Add(new { field = name, before, after = value });
            }
            var bytes = document.Patch(id, request.Changes);
            var resultingHash = Hash(bytes);
            var signature = Convert.ToHexString(HMACSHA256.HashData(_previewKey, Encoding.UTF8.GetBytes($"{file}\n{id}\n{request.ExpectedHash}\n{resultingHash}"))).ToLowerInvariant();
            if (!save) return new { changes, warnings, preview_signature = signature, resulting_hash = resultingHash, applicable = changes.Count > 0 };
            if (changes.Count == 0 || request.PreviewSignature != signature) throw new TileEditException("Preview these exact changes before saving.", 409);
            ct.ThrowIfCancellationRequested();
            var temporary = path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                // Revalidate path ancestry and contents immediately before atomic replacement.
                // This is best-effort external concurrency, not an OS compare-and-swap with Tiled.
                if (FilePath(file) != path || Hash(Read(file).Bytes) != request.ExpectedHash) throw Conflict();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new { tileset_saved = true, hash = resultingHash, warnings, map_publication = "not_requested", live_game_restarted = false };
        }
        finally { _saveGate.Release(); }
    }
    private static TileEditException Conflict() => new("The tileset changed on disk. Reload and compare your draft before saving again.", 409);
    private TilesetXml Read(string file)
    {
        var path = FilePath(file);
        if (!File.Exists(path)) throw new TileEditException("Tileset not found.", 404);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumXmlBytes) throw new TileEditException("Tileset exceeds the 4 MiB editing limit.");
        using var buffer = new MemoryStream();
        var block = new byte[8192]; int count;
        while ((count = stream.Read(block)) > 0)
        {
            if (buffer.Length + count > MaximumXmlBytes) throw new TileEditException("Tileset exceeds the 4 MiB editing limit.");
            buffer.Write(block, 0, count);
        }
        try { return new TilesetXml(buffer.ToArray()); }
        catch (Exception ex) when (ex is XmlException or DecoderFallbackException)
        { throw new TileEditException("The tileset is not supported UTF-8 XML, or contains prohibited DTD/entity declarations."); }
    }
    private TileEntry[] Entries(TilesetXml document, string path)
    {
        var root = document.Root;
        var atlas = root.Element("image");
        var explicitTiles = root.Elements("tile").ToDictionary(t => TilesetXml.Number(t, "id"));
        var count = atlas is null ? explicitTiles.Count : TilesetXml.Number(root, "tilecount");
        if (count > 10000) throw new TileEditException("Tilesets over 10,000 tiles are not supported by this editor.");
        var ids = atlas is null ? explicitTiles.Keys.Order().ToArray() : Enumerable.Range(0, count).ToArray();
        if (atlas is not null && explicitTiles.Keys.Any(id => id >= count)) throw new TileEditException("Atlas contains an out-of-range tile ID.");
        return ids.Select(id =>
        {
            explicitTiles.TryGetValue(id, out var tile);
            var image = atlas ?? tile?.Element("image");
            if (image is null || tile?.Elements("image").Count() > 1) throw new TileEditException("A collection tile must have one external image.");
            var width = TilesetXml.Number(atlas is null ? image : root, atlas is null ? "width" : "tilewidth");
            var height = TilesetXml.Number(atlas is null ? image : root, atlas is null ? "height" : "tileheight");
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new TileEditException("Unsupported tile dimensions.");
            var x = 0; var y = 0;
            if (atlas is not null)
            {
                var columns = TilesetXml.Number(root, "columns");
                var margin = TilesetXml.Number(root, "margin", 0); var spacing = TilesetXml.Number(root, "spacing", 0);
                if (columns <= 0 || columns > 10000 || margin > 4096 || spacing > 4096) throw new TileEditException("Invalid atlas layout.");
                x = margin + id % columns * (width + spacing); y = margin + id / columns * (height + spacing);
                if ((long)x + width > TilesetXml.Number(image, "width") || (long)y + height > TilesetXml.Number(image, "height")) throw new TileEditException("Atlas crop exceeds its image.");
            }
            var allowed = FlagFields.Concat(atlas is null ? ReferenceFields.Where(n => n != "item_id" || (string?)root.Attribute("name") == "item_spawns") : []).ToList();
            var properties = (tile?.Element("properties")?.Elements("property") ?? []).Select(p =>
            {
                var name = (string)p.Attribute("name")!;
                var editable = allowed.Contains(name) && TilesetXml.SimpleProperty(p, FlagFields.Contains(name) ? "bool" : "string");
                if (!editable) allowed.Remove(name);
                return new TileProperty(name, (string?)p.Attribute("type") ?? "string", TilesetXml.Value(p), editable);
            }).ToArray();
            var source = (string?)image.Attribute("source");
            var available = false;
            try { var imagePath = ImagePath(path, source); available = File.Exists(imagePath) && new FileInfo(imagePath).Length <= 16 * 1024 * 1024; }
            catch (TileEditException) { /* Missing/outside images never widen the configured asset boundary. */ }
            return new TileEntry(id, new(Path.GetFileName(source ?? "Missing image"), x, y, width, height, available), properties, allowed.ToArray());
        }).ToArray();
    }
    private string Root()
    {
        if (!roots.Value.Roots.TryGetValue("tiled_tilesets", out var configured) || string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
            throw new TileEditException("Tiles are not configured. Set the approved tiled_tilesets asset root on the host.", 503);
        var root = Path.GetFullPath(configured);
        NoLinks(root);
        if (!Directory.Exists(root)) throw new TileEditException("The configured tileset root is unavailable.", 503);
        return root;
    }
    private string FilePath(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !file.EndsWith(".tsx", StringComparison.Ordinal) || file.Contains('\\') || file.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new TileEditException("Choose a TSX file from the configured tileset list.");
        var root = Root(); var path = Path.GetFullPath(Path.Combine(root, file));
        Confine(root, path); NoLinks(path); return path;
    }
    private string ImagePath(string tilesetPath, string? source)
    {
        var root = assets.GetGameAssetsRoot();
        if (root is null || string.IsNullOrWhiteSpace(source) || Path.IsPathRooted(source) || source.Contains('\\') || source.Contains(':') || !source.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new TileEditException("Image is not an external PNG under the configured game assets.");
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tilesetPath)!, source));
        Confine(Path.GetFullPath(root), path); NoLinks(path); return path;
    }
    private static void Confine(string root, string path)
    {
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new TileEditException("Path is outside the configured root.");
    }
    private static void NoLinks(string path)
    {
        for (FileSystemInfo? current = new FileInfo(path); current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new TileEditException("Linked files and directories are not supported.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
