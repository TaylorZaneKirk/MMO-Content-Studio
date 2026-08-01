using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class ItemAssetAuthoringService
{
    private const long MaximumPngBytes = 16 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<ItemAssetAuthoringService> _logger;

    public ItemAssetAuthoringService(
        ItemAssetService assetService,
        ILogger<ItemAssetAuthoringService> logger)
    {
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<ImportItemAssetResponse>> ImportAsync(
        ImportItemAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var suppliedSourcePath = (request.SourceFilePath ?? string.Empty).Trim();
            if (suppliedSourcePath.Length == 0)
            {
                return Failure("asset_source_missing", "Select a PNG to import.", "source_file_path");
            }

            var sourcePath = Path.GetFullPath(suppliedSourcePath);
            if (!File.Exists(sourcePath))
            {
                return Failure("asset_source_missing", "The selected PNG does not exist.", "source_file_path");
            }

            var sourceInfo = new FileInfo(sourcePath);
            if (!string.Equals(sourceInfo.Extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("asset_not_png", "Only PNG item assets can be imported.", "source_file_path");
            }

            if (sourceInfo.Length is <= 8 or > MaximumPngBytes)
            {
                return Failure(
                    "asset_size_invalid",
                    $"PNG size must be between 9 bytes and {MaximumPngBytes / 1024 / 1024} MB.",
                    "source_file_path");
            }

            if (!await HasPngSignatureAsync(sourcePath, cancellationToken))
            {
                return Failure("asset_signature_invalid", "The selected file is not a valid PNG stream.", "source_file_path");
            }

            var requestedName = string.IsNullOrWhiteSpace(request.TargetFileName)
                ? sourceInfo.Name
                : request.TargetFileName.Trim();
            var safeName = SanitizeFileName(requestedName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return Failure("asset_name_invalid", "The target file name has no usable characters.", "target_file_name");
            }

            if (!safeName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                safeName += ".png";
            }

            var assetsRoot = _assetService.GetGameAssetsRoot();
            if (assetsRoot is null)
            {
                return Failure(
                    "asset_root_unconfigured",
                    "The game_client_assets root is not configured.",
                    "target_file_name");
            }

            var itemDirectory = Path.Combine(assetsRoot, "items");
            Directory.CreateDirectory(itemDirectory);
            var targetPath = Path.GetFullPath(Path.Combine(itemDirectory, safeName));
            var normalizedItemDirectory = Path.GetFullPath(itemDirectory).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(normalizedItemDirectory, PathComparison))
            {
                return Failure("asset_name_invalid", "The target asset path escapes the item directory.", "target_file_name");
            }

            if (string.Equals(sourcePath, targetPath, PathComparison))
            {
                return Success(targetPath, false, "The selected PNG is already in the canonical item directory.");
            }

            if (File.Exists(targetPath))
            {
                if (await FilesMatchAsync(sourcePath, targetPath, cancellationToken))
                {
                    return Success(targetPath, false, "An identical canonical item asset already exists.");
                }

                return Failure(
                    "asset_name_conflict",
                    $"A different item asset named '{safeName}' already exists. Choose another target name.",
                    "target_file_name");
            }

            var temporaryPath = $"{targetPath}.content-studio-{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var target = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(target, cancellationToken);
                    await target.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, targetPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return Success(targetPath, true, "The PNG was imported into the canonical item asset directory.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            _logger.LogWarning(exception, "Item asset import failed");
            return Failure(
                "asset_import_failed",
                "The PNG could not be imported into the canonical item asset directory.",
                "source_file_path");
        }
    }

    private AuthoringOperationResult<ImportItemAssetResponse> Success(
        string targetPath,
        bool created,
        string message)
    {
        var assetsRoot = _assetService.GetGameAssetsRoot()
            ?? throw new InvalidOperationException("The game asset root became unavailable during import.");
        var relative = Path.GetRelativePath(assetsRoot, targetPath).Replace('\\', '/');
        var asset = new ItemAssetEntry(
            $"res://assets/{relative}",
            Path.GetFileNameWithoutExtension(targetPath).Replace('_', ' '),
            targetPath);
        return AuthoringOperationResult<ImportItemAssetResponse>.Success(
            new ImportItemAssetResponse(asset, created, message));
    }

    private static AuthoringOperationResult<ImportItemAssetResponse> Failure(
        string code,
        string message,
        string field) =>
        AuthoringOperationResult<ImportItemAssetResponse>.Failure(new ApiError(
            code,
            message,
            ValidationSeverity.Error,
            field));

    private static async Task<bool> HasPngSignatureAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[PngSignature.Length];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        return bytesRead == buffer.Length && buffer.SequenceEqual(PngSignature);
    }

    private static async Task<bool> FilesMatchAsync(
        string firstPath,
        string secondPath,
        CancellationToken cancellationToken)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        await using var first = new FileStream(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var second = new FileStream(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var firstHash = await SHA256.HashDataAsync(first, cancellationToken);
        var secondHash = await SHA256.HashDataAsync(second, cancellationToken);
        return firstHash.SequenceEqual(secondHash);
    }

    private static string SanitizeFileName(string requestedName)
    {
        var fileName = Path.GetFileName(requestedName);
        return UnsafeFileNameCharactersRegex().Replace(fileName, "_").Trim(' ', '.', '_');
    }

    [GeneratedRegex("[^A-Za-z0-9._ -]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileNameCharactersRegex();
}
