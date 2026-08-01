using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ItemAssetService
{
    private const string AssetsResourcePrefix = "res://assets/";
    private const string ItemResourcePrefix = "res://assets/items/";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly AssetRootsOptions _options;

    public ItemAssetService(IOptions<AssetRootsOptions> options)
    {
        _options = options.Value;
    }

    public string? GetGameAssetsRoot() =>
        TryGetGameAssetsRoot(out var assetsRoot) ? assetsRoot : null;

    public ItemAssetCatalogResponse LoadCatalog()
    {
        if (!TryGetGameAssetsRoot(out var assetsRoot))
        {
            return new ItemAssetCatalogResponse(DateTimeOffset.UtcNow, []);
        }

        var itemDirectory = Path.Combine(assetsRoot, "items");
        if (!Directory.Exists(itemDirectory))
        {
            return new ItemAssetCatalogResponse(DateTimeOffset.UtcNow, []);
        }

        var assets = Directory
            .EnumerateFiles(itemDirectory, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(assetsRoot, path).Replace('\\', '/');
                return new ItemAssetEntry(
                    $"{AssetsResourcePrefix}{relative}",
                    Path.GetFileNameWithoutExtension(path).Replace('_', ' '),
                    Path.GetFullPath(path));
            })
            .ToArray();

        return new ItemAssetCatalogResponse(DateTimeOffset.UtcNow, assets);
    }

    public ItemAssetResolution Resolve(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return new ItemAssetResolution(false, null, "An item icon is required.");
        }

        if (!resourcePath.StartsWith(ItemResourcePrefix, StringComparison.Ordinal))
        {
            return new ItemAssetResolution(
                false,
                null,
                $"Item icons must use the canonical '{ItemResourcePrefix}' resource prefix.");
        }

        if (!string.Equals(Path.GetExtension(resourcePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return new ItemAssetResolution(false, null, "Item icons must be PNG files.");
        }

        if (!TryGetGameAssetsRoot(out var assetsRoot))
        {
            return new ItemAssetResolution(false, null, "The game_client_assets root is not configured.");
        }

        var relative = resourcePath[AssetsResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is ".." or "."))
        {
            return new ItemAssetResolution(false, null, "Item icon paths may not contain traversal segments.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(assetsRoot, relative));
        var normalizedRoot = Path.GetFullPath(assetsRoot).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedRoot, PathComparison))
        {
            return new ItemAssetResolution(false, null, "The item icon resolves outside the configured asset root.");
        }

        return File.Exists(fullPath)
            ? new ItemAssetResolution(true, fullPath, null)
            : new ItemAssetResolution(false, fullPath, "The selected item icon does not exist on disk.");
    }

    private bool TryGetGameAssetsRoot(out string assetsRoot)
    {
        assetsRoot = string.Empty;
        if (!_options.Roots.TryGetValue("game_client_assets", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        assetsRoot = Path.GetFullPath(configured);
        return true;
    }
}

public sealed record ItemAssetResolution(bool Exists, string? FilePath, string? Message);
