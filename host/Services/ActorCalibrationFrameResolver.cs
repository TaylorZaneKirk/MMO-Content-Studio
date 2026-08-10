using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ActorCalibrationFrameResolver
{
    private const string ResourcePrefix = "res://assets/";
    private readonly AssetRootsOptions _options;

    public ActorCalibrationFrameResolver(IOptions<AssetRootsOptions> options)
    {
        _options = options.Value;
    }

    public AuthoringOperationResult<ActorCalibrationFramesResponse> Resolve(
        CalibrationFrameRequest request)
    {
        var actorKind = request.ActorKind?.Trim();
        if (actorKind is not ("npc" or "mob"))
        {
            return Failure("invalid_actor_kind", "Actor kind must be npc or mob.", "actor_kind");
        }

        var visualTexturePath = request.VisualTexturePath?.Trim();
        if (string.IsNullOrWhiteSpace(visualTexturePath))
        {
            return Failure("invalid_visual_texture_path", "Visual texture path is required.", "visual_texture_path");
        }

        var assetsRoot = ResolveAssetsRoot();
        if (assetsRoot is null)
        {
            return Failure(
                "actor_calibration_assets_unavailable",
                "The configured game_client_assets root is unavailable.");
        }

        var frames = new List<ActorCalibrationFrameDefinition>();
        foreach (var direction in Directions)
        {
            foreach (var frame in Frames)
            {
                var path = actorKind == "npc"
                    ? ResolveExactNpcFrame(assetsRoot, visualTexturePath, direction, frame)
                    : ResolveExactMobFrame(assetsRoot, visualTexturePath, direction, frame);
                if (path is not null && TryReadPngDimensions(path, out var width, out var height))
                {
                    frames.Add(new ActorCalibrationFrameDefinition(direction, frame, true, path, width, height));
                    continue;
                }

                frames.Add(new ActorCalibrationFrameDefinition(direction, frame, false, null, null, null));
            }
        }

        return AuthoringOperationResult<ActorCalibrationFramesResponse>.Success(
            new ActorCalibrationFramesResponse(actorKind, visualTexturePath, frames));
    }

    private string? ResolveAssetsRoot()
    {
        if (!_options.Roots.TryGetValue("game_client_assets", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var configuredRoot = Path.GetFullPath(configured);
        if (Directory.Exists(configuredRoot) && string.Equals(Path.GetFileName(configuredRoot), "assets", StringComparison.OrdinalIgnoreCase))
        {
            return configuredRoot;
        }

        var assetsRoot = Path.Combine(configuredRoot, "assets");
        return Directory.Exists(assetsRoot) ? assetsRoot : null;
    }

    private static string? ResolveExactNpcFrame(string assetsRoot, string visualTexturePath, string direction, int frame)
    {
        var seedPath = ResolveResourcePath(assetsRoot, visualTexturePath);
        if (seedPath is null || !File.Exists(seedPath))
        {
            return null;
        }

        return RiggedSpritePreviewResolver.ResolveExactCharsFrame(seedPath, direction, frame);
    }

    private static string? ResolveExactMobFrame(string assetsRoot, string visualTexturePath, string direction, int frame)
    {
        var seedPath = ResolveResourcePath(assetsRoot, visualTexturePath);
        if (seedPath is null || !File.Exists(seedPath))
        {
            return null;
        }

        var assetKey = Path.GetFileNameWithoutExtension(seedPath);
        if (!Regex.IsMatch(assetKey, "^[a-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        var candidate = Path.Combine(assetsRoot, "actors", "mobs", $"{assetKey}-F{frame}-{direction}.png");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveResourcePath(string assetsRoot, string resourcePath)
    {
        if (!resourcePath.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var relativePath = resourcePath[ResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(assetsRoot, relativePath));
        var rootWithSeparator = assetsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? candidate : null;
    }

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        if (stream.Read(header) != header.Length || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4e || header[3] != 0x47)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return width > 0 && height > 0;
    }

    private static AuthoringOperationResult<ActorCalibrationFramesResponse> Failure(string code, string message, string? field = null) =>
        AuthoringOperationResult<ActorCalibrationFramesResponse>.Failure(
            new ApiError(code, message, ValidationSeverity.Error, field));

    private static readonly string[] Directions = ["N", "E", "S", "W"];
    private static readonly int[] Frames = [1, 2, 3, 4];
}
