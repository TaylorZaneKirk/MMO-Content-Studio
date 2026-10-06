// Read-only spell media. Canonical identities, bounded bytes and format checks are
// owned here; neither browser paths nor authored references select a host root.
using System.Buffers.Binary;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public sealed class BrowserSpellMedia(ItemAssetService assets)
{
    private const string Prefix = "res://assets/";
    private const int ImageLimit = 16 * 1024 * 1024;
    private const int AudioLimit = 32 * 1024 * 1024;
    private string? Root()
    {
        var configured = assets.GetGameAssetsRoot();
        if (configured is null) return null;
        var root = Path.GetFullPath(configured);
        for (var part = new DirectoryInfo(root); part is not null; part = part.Parent)
            if (part.LinkTarget is not null) return null;
        return root;
    }
    private string? Resolve(string resource, bool audio)
    {
        var root = Root();
        if (root is null || !resource.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var relative = resource[Prefix.Length..];
        var extension = Path.GetExtension(relative).ToLowerInvariant();
        if (relative.Contains('\\') || (audio ? extension is not (".wav" or ".ogg" or ".mp3") : extension != ".png")) return null;
        var parts = relative.Split('/');
        if (parts.Any(part => part is "" or "." or ".." || part.Contains(':'))) return null;
        var path = root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
            if (new FileInfo(path).LinkTarget is not null) return null;
        }
        return path;
    }
    public object Catalog(bool audio)
    {
        var rows = new List<object>();
        var root = Root();
        if (root is not null && Directory.Exists(root))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var path in Directory.EnumerateFiles(root, "*", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                var resource = Prefix + Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Resolve(resource, audio) is null) continue;
                var size = new FileInfo(path).Length;
                if (size == 0 || size > (audio ? AudioLimit : ImageLimit)) continue;
                rows.Add(new { resource_path = resource, display_name = Path.GetFileName(path),
                    url = "/studio/api/spells/" + (audio ? "audio" : "image") + "?resource=" + Uri.EscapeDataString(resource) });
            }
        }
        return new { assets = rows };
    }
    public IResult Read(string resource, bool audio)
    {
        var path = Resolve(resource, audio);
        if (path is null || !File.Exists(path)) return Results.NotFound();
        try
        {
            // Read the bounded snapshot that is validated and returned, rather than
            // validating a file then asking the response to reopen changed bytes.
            using var stream = File.OpenRead(path);
            if (stream.Length < 12 || stream.Length > (audio ? AudioLimit : ImageLimit)) return Results.NotFound();
            var bytes = new byte[(int)stream.Length];stream.ReadExactly(bytes);
            var type = Path.GetExtension(path).ToLowerInvariant();
            if (audio ? !ValidAudio(bytes, type) : !BrowserPngAssets.ValidPng(bytes)) return Results.NotFound();
            return Results.File(bytes, audio ? type switch { ".wav" => "audio/wav", ".ogg" => "audio/ogg", _ => "audio/mpeg" } : "image/png", enableRangeProcessing: audio);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Results.NotFound(); }
    }
    // Format headers are checked here; the browser reports unsupported/corrupt
    // codec data. This is preview delivery, not an audio transcoding service.
    private static bool ValidAudio(byte[] bytes, string extension)
    {
        var data = bytes.AsSpan();
        if (extension == ".wav") return data[..4].SequenceEqual("RIFF"u8) && data.Slice(8,4).SequenceEqual("WAVE"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4,4)) <= bytes.Length - 8;
        if (extension == ".ogg") return data[..4].SequenceEqual("OggS"u8) && data[4] == 0;
        var offset = 0;
        if (data[..3].SequenceEqual("ID3"u8))
        {
            if (data[3] is < 2 or > 4 || data.Slice(6,4).ContainsAnyInRange((byte)128,(byte)255)) return false;
            offset = 10 + (data[6] << 21 | data[7] << 14 | data[8] << 7 | data[9]);
            if (data[3] == 4 && (data[5] & 0x10) != 0) offset += 10;
        }
        if (offset > bytes.Length - 4) return false;
        // MPEG audio sync, supported version/layer and nonreserved rate indices.
        return data[offset] == 0xff && (data[offset+1] & 0xe0) == 0xe0
            && (data[offset+1] & 0x18) != 0x08 && (data[offset+1] & 0x06) != 0
            && (data[offset+2] & 0xf0) is not (0 or 0xf0) && (data[offset+2] & 0x0c) != 0x0c;
    }
}
