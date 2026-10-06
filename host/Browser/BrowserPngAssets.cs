// Shared confined PNG boundary. Read/upload scopes are fixed by host code, never by browser paths.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Browser;

public partial class BrowserPngAssets(ItemAssetService assets, string readFolder, string uploadFolder, string imageEndpoint)
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private string Prefix => "res://assets/" + (readFolder.Length == 0 ? "" : readFolder + "/");
    private string UploadPrefix => "res://assets/" + uploadFolder + "/";

    private string? Root()
    {
        var gameRoot = assets.GetGameAssetsRoot();
        if (gameRoot is null) return null;
        var root = Path.GetFullPath(Path.Combine(gameRoot, readFolder));
        // Refuse links anywhere in the configured ancestry, including the root itself.
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) return null;
        return root;
    }
    private string? Resolve(string resource)
    {
        var root = Root();
        if (root is null || !resource.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var relative = resource[Prefix.Length..];
        if (relative.Contains('\\') || !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
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
    // Convert only already-confined PNGs to browser identities; never expose host paths.
    public bool CanRead(string resource) => Resolve(resource) is { } path && File.Exists(path) && new FileInfo(path).Length <= MaximumBytes;
    public string? UrlForFile(string? file)
    {
        var root = Root();
        if (root is null || string.IsNullOrWhiteSpace(file)) return null;
        var full = Path.GetFullPath(file);
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        var resource = Prefix + relative;
        return Resolve(resource) == full && CanRead(resource) ? imageEndpoint + "?resource=" + Uri.EscapeDataString(resource) : null;
    }
    public object Catalog()
    {
        var root = Root();
        var entries = new List<object>();
        if (root is not null && Directory.Exists(root))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var path in Directory.EnumerateFiles(root, "*.png", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                var resource = Prefix + Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Resolve(resource) is not null) entries.Add(Entry(resource));
            }
        }
        return new { assets = entries };
    }
    private object Entry(string resource) => new
    {
        resource_path = resource,
        display_name = Path.GetFileNameWithoutExtension(resource).Replace('_', ' '),
        url = imageEndpoint + "?resource=" + Uri.EscapeDataString(resource)
    };
    public IResult Image(string resource)
    {
        var path = Resolve(resource);
        if (path is null || !File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return Results.NotFound();
        return Results.File(path, "image/png", enableRangeProcessing: false);
    }
    public async Task<IResult> Upload(HttpRequest request, string name, CancellationToken cancellation)
    {
        if (request.ContentType != "image/png" || request.ContentLength is > MaximumBytes)
            return BrowserJson.Error("invalid_png", "Choose a PNG no larger than 16 MiB.");
        if (!SafeName().IsMatch(name) || name.StartsWith('.') || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return BrowserJson.Error("invalid_name", "Use a PNG filename with letters, digits, spaces, dots, hyphens or underscores; no folders.");
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int count;
        while ((count = await request.Body.ReadAsync(bytes, cancellation)) > 0)
        {
            if (buffer.Length + count > MaximumBytes) return BrowserJson.Error("png_too_large", "PNG exceeds 16 MiB.", 413);
            buffer.Write(bytes, 0, count);
        }
        var data = buffer.ToArray();
        if (!ValidPng(data)) return BrowserJson.Error("invalid_png", "PNG is invalid or exceeds 4096 pixels per side / 16 million pixels.");
        var resource = UploadPrefix + name;
        var target = Resolve(resource);
        var root = target is null ? null : Path.GetDirectoryName(target);
        if (target is null || root is null) return BrowserJson.Error("asset_root_unavailable", "The host asset folder is unavailable.");
        Directory.CreateDirectory(root);
        if (File.Exists(target)) return await Existing(target, resource, data, cancellation);
        var temporary = Path.Combine(root, $".studio-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.WriteAsync(data, cancellation);
            // Recheck confinement before the atomic rename. Existing assets are never replaced.
            if (Resolve(resource) != target) return BrowserJson.Error("asset_root_changed", "The host asset folder changed; retry after checking it.");
            try { File.Move(temporary, target, false); }
            catch (IOException) when (File.Exists(target)) { return await Existing(target, resource, data, cancellation); }
            return BrowserJson.Json(new { asset = Entry(resource), created = true, message = "PNG imported. Save the definition to assign it." });
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<IResult> Existing(string target, string resource, byte[] data, CancellationToken cancellation)
    {
        if (new FileInfo(target).LinkTarget is not null) return Results.NotFound();
        await using var file = File.OpenRead(target);
        if (file.Length != data.Length || !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(file, cancellation), SHA256.HashData(data)))
            return BrowserJson.Error("asset_name_conflict", "A different PNG already has that name. Choose another name.", 409);
        return BrowserJson.Json(new { asset = Entry(resource), created = false, message = "Identical PNG already exists; selected it." });
    }

    // Validate bounded dimensions, chunk CRCs and the exact decompressed scanline size.
    // This accepts ordinary and Adam7 PNGs without loading an unbounded image decoder.
    private static bool ValidPng(byte[] data)
    {
        try
        {
            if (data.Length < 45 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
            int width = 0, height = 0, bits = 0, interlace = 0, position = 8;
            bool header = false, ended = false, palette = false;
            int color = -1;
            using var compressed = new MemoryStream();
            while (position + 12 <= data.Length)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4)));
                if (length < 0 || length > data.Length - position - 12) return false;
                var chunk = data.AsSpan(position + 4, length + 4);
                var type = System.Text.Encoding.ASCII.GetString(chunk[..4]);
                if (Crc(chunk) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8 + length, 4))) return false;
                if (!header && type != "IHDR") return false;
                if (type == "IHDR")
                {
                    if (header || length != 13) return false;
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4)));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(8, 4)));
                    int depth = chunk[12]; color = chunk[13]; interlace = chunk[16];
                    var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                    if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > 16_000_000
                        || channels == 0 || chunk[14] != 0 || chunk[15] != 0 || interlace > 1
                        || !(color == 0 ? depth is 1 or 2 or 4 or 8 or 16 : color == 3 ? depth is 1 or 2 or 4 or 8 : depth is 8 or 16)) return false;
                    bits = channels * depth; header = true;
                }
                else if (type == "PLTE") { if (length == 0 || length > 768 || length % 3 != 0) return false; palette = true; }
                else if (type == "IDAT") compressed.Write(chunk[4..]);
                else if (type == "IEND") { ended = length == 0 && position + 12 == data.Length; break; }
                else if (char.IsUpper(type[0])) return false;
                position += length + 12;
            }
            if (!ended || compressed.Length == 0 || color == 3 && !palette) return false;
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            int[] startX = interlace == 0 ? [0] : [0, 4, 0, 2, 0, 1, 0];
            int[] startY = interlace == 0 ? [0] : [0, 0, 4, 0, 2, 0, 1];
            int[] stepX = interlace == 0 ? [1] : [8, 8, 4, 4, 2, 2, 1];
            int[] stepY = interlace == 0 ? [1] : [8, 8, 8, 4, 4, 2, 2];
            for (var pass = 0; pass < startX.Length; pass++)
            {
                var columns = Math.Max(0, (width - startX[pass] + stepX[pass] - 1) / stepX[pass]);
                var rows = Math.Max(0, (height - startY[pass] + stepY[pass] - 1) / stepY[pass]);
                if (columns == 0) continue;
                var scanline = new byte[(columns * bits + 7) / 8];
                for (var row = 0; row < rows; row++)
                {
                    var filter = zlib.ReadByte();
                    if (filter is < 0 or > 4) return false;
                    zlib.ReadExactly(scanline);
                }
            }
            return zlib.ReadByte() == -1;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException) { return false; }
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
        }
        return crc ^ 0xffffffff;
    }
    [GeneratedRegex("^[A-Za-z0-9._ -]{1,120}$")]
    private static partial Regex SafeName();
}
