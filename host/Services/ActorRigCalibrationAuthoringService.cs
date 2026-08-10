using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ActorRigCalibrationAuthoringService
{
    private const int SupportedSchemaVersion = 1;
    private const int CoordinateLimit = 4096;
    private static readonly Regex CalibrationIdPattern = new(
        "^[a-z0-9][a-z0-9_]{0,63}$",
        RegexOptions.CultureInvariant);
    private static readonly string[] Directions = ["N", "E", "S", "W"];
    private static readonly string[] Frames = ["1", "2", "3", "4"];

    private readonly ActorAppearanceCatalogService _catalogService;
    private readonly ActorCalibrationFrameResolver? _frameResolver;
    private readonly Action<string>? _beforeReplace;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ActorRigCalibrationAuthoringService(
        ActorAppearanceCatalogService catalogService,
        ActorCalibrationFrameResolver? frameResolver = null,
        Action<string>? beforeReplace = null)
    {
        _catalogService = catalogService;
        _frameResolver = frameResolver;
        _beforeReplace = beforeReplace;
    }

    public async Task<AuthoringOperationResult<ActorCalibrationLoadResponse>> LoadAsync(
        string calibrationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedId = calibrationId?.Trim();
        if (!IsValidCalibrationId(normalizedId))
        {
            return Failure("invalid_actor_calibration_id", "Calibration ID must use lowercase letters, digits, and underscores.", "calibration_id");
        }

        var catalog = ReadCatalog();
        if (!catalog.Succeeded || catalog.Value is null)
        {
            return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(catalog.Errors);
        }

        var entry = FindCalibration(catalog.Value.Calibrations, normalizedId);
        return await Task.FromResult(AuthoringOperationResult<ActorCalibrationLoadResponse>.Success(
            new ActorCalibrationLoadResponse(
                entry is not null,
                catalog.Value.Hash,
                entry is null ? null : ToJsonElement(entry))));
    }

    public async Task<AuthoringOperationResult<ActorCalibrationLoadResponse>> SaveAsync(
        string calibrationId,
        SaveActorCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = calibrationId?.Trim();
        if (!IsValidCalibrationId(normalizedId))
        {
            return Failure("invalid_actor_calibration_id", "Calibration ID must use lowercase letters, digits, and underscores.", "calibration_id");
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var catalog = ReadCatalog();
            if (!catalog.Succeeded || catalog.Value is null)
            {
                return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(catalog.Errors);
            }

            if (!string.Equals(request.ExpectedCatalogHash?.Trim(), catalog.Value.Hash, StringComparison.Ordinal))
            {
                return Failure(
                    "actor_calibration_catalog_conflict",
                    "The actor calibration catalog changed before this save could be applied.",
                    "expected_catalog_hash");
            }

            var rigCatalog = _catalogService.LoadRigCatalog();
            if (!rigCatalog.Available)
            {
                return Failure(
                    "actor_rig_catalog_unavailable",
                    rigCatalog.Message ?? "The canonical actor rig catalog is unavailable.");
            }

            var existing = FindCalibration(catalog.Value.Calibrations, normalizedId);
            var requestedRigId = request.RigId?.Trim();
            string? existingRigId = null;
            if (existing is not null && !TryReadRequiredString(existing, "rig_id", out existingRigId))
            {
                return Failure("invalid_actor_calibration_catalog", "The existing calibration has no valid rig ID.");
            }

            if (existing is not null && !string.Equals(existingRigId, requestedRigId, StringComparison.Ordinal))
            {
                return Failure(
                    "actor_calibration_rig_immutable",
                    "A calibration cannot change its rig ID through socket editing.",
                    "rig_id");
            }

            var rigId = existing is null ? requestedRigId : existingRigId;
            var rig = rigCatalog.Rigs.SingleOrDefault(candidate => candidate.RigId == rigId);
            if (rig is null)
            {
                return Failure("invalid_actor_rig_id", "The requested rig ID is not in the canonical actor rig catalog.", "rig_id");
            }

            if (!TryParseSocketOverrides(request.SocketOverrides, rig, out var socketOverrides, out var socketError))
            {
                return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(socketError!);
            }

            JsonObject? foregroundOverlays = null;
            if (request.ForegroundOverlayOverrides is JsonElement requestedOverlays)
            {
                if (!TryParseForegroundOverlayOverrides(
                        requestedOverlays,
                        rig,
                        request.ActorKind,
                        request.VisualTexturePath,
                        out var overlayOverrides,
                        out var overlayError))
                {
                    return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(overlayError!);
                }

                foregroundOverlays = MergeForegroundOverlayOverrides(existing?["foreground_overlays"] as JsonObject, rig, overlayOverrides);
            }

            if (existing is not null
                && JsonNode.DeepEquals(existing["sockets"], socketOverrides)
                && (foregroundOverlays is null || JsonNode.DeepEquals(existing["foreground_overlays"], foregroundOverlays)))
            {
                return AuthoringOperationResult<ActorCalibrationLoadResponse>.Success(
                    new ActorCalibrationLoadResponse(true, catalog.Value.Hash, ToJsonElement(existing)));
            }

            var updatedEntry = existing is null
                ? new JsonObject
                {
                    ["schema_version"] = SupportedSchemaVersion,
                    ["calibration_id"] = normalizedId,
                    ["rig_id"] = rigId
                }
                : (JsonObject)existing.DeepClone();
            updatedEntry["sockets"] = socketOverrides;
            if (foregroundOverlays is not null)
            {
                if (foregroundOverlays.Count == 0)
                {
                    updatedEntry.Remove("foreground_overlays");
                }
                else
                {
                    updatedEntry["foreground_overlays"] = foregroundOverlays;
                }
            }

            var updatedEntries = catalog.Value.Calibrations.OfType<JsonObject>()
                .Where(entry => !string.Equals(ReadCalibrationId(entry), normalizedId, StringComparison.Ordinal))
                .Select(entry => (JsonObject)entry.DeepClone())
                .Append(updatedEntry)
                .OrderBy(ReadCalibrationId, StringComparer.Ordinal)
                .ToArray();
            var updatedRoot = CreateCanonicalCatalog(catalog.Value.Root, updatedEntries);
            if (!TryValidateCatalog(updatedRoot, rigCatalog, out var validationError))
            {
                return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(validationError!);
            }

            var updatedBytes = SerializeCanonical(updatedRoot);
            var temporaryPath = WriteTemporaryFile(catalog.Value.Path, updatedBytes);
            try
            {
                _beforeReplace?.Invoke(catalog.Value.Path);
                var finalBytes = ReadCurrentCatalogBytes(catalog.Value.Path);
                if (!finalBytes.Succeeded || finalBytes.Value is null)
                {
                    return AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(finalBytes.Errors);
                }

                if (!string.Equals(ComputeHash(finalBytes.Value), catalog.Value.Hash, StringComparison.Ordinal))
                {
                    return Failure(
                        "actor_calibration_catalog_conflict",
                        "The actor calibration catalog changed before this save could be applied.",
                        "expected_catalog_hash");
                }

                File.Move(temporaryPath, catalog.Value.Path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var newHash = ComputeHash(updatedBytes);
            var savedEntry = FindCalibration((JsonArray)updatedRoot["calibrations"]!, normalizedId)!;
            return AuthoringOperationResult<ActorCalibrationLoadResponse>.Success(
                new ActorCalibrationLoadResponse(true, newHash, ToJsonElement(savedEntry)));
        }
        catch (IOException)
        {
            return Failure("actor_calibration_catalog_unavailable", "The actor calibration catalog could not be written.");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private AuthoringOperationResult<CalibrationCatalogFile> ReadCatalog()
    {
        var path = _catalogService.ResolveRigCalibrationCatalogPath();
        if (path is null || !File.Exists(path))
        {
            return FailureCatalog("actor_calibration_catalog_unavailable", "The canonical actor calibration catalog is unavailable.");
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var root = JsonNode.Parse(bytes) as JsonObject;
            if (root is null || !TryReadSchemaVersion(root, out var schemaVersion) || schemaVersion != SupportedSchemaVersion
                || root["calibrations"] is not JsonArray calibrations)
            {
                return FailureCatalog("invalid_actor_calibration_catalog", "The actor calibration catalog must use schema_version 1 and a calibrations array.");
            }

            var rigCatalog = _catalogService.LoadRigCatalog();
            ApiError? validationError = null;
            if (!rigCatalog.Available || !TryValidateCatalog(root, rigCatalog, out validationError))
            {
                return AuthoringOperationResult<CalibrationCatalogFile>.Failure(validationError
                    ?? new ApiError("actor_rig_catalog_unavailable", rigCatalog.Message ?? "The canonical actor rig catalog is unavailable.", ValidationSeverity.Error));
            }

            return AuthoringOperationResult<CalibrationCatalogFile>.Success(
                new CalibrationCatalogFile(path, bytes, ComputeHash(bytes), root, calibrations));
        }
        catch (JsonException)
        {
            return FailureCatalog("invalid_actor_calibration_catalog", "The actor calibration catalog JSON could not be parsed.");
        }
        catch (IOException)
        {
            return FailureCatalog("actor_calibration_catalog_unavailable", "The actor calibration catalog could not be read.");
        }
    }

    private static bool TryValidateCatalog(
        JsonObject root,
        ActorRigCatalogDefinition rigCatalog,
        out ApiError? error)
    {
        error = null;
        if (!TryReadSchemaVersion(root, out var schemaVersion) || schemaVersion != SupportedSchemaVersion
            || root["calibrations"] is not JsonArray calibrations)
        {
            error = new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog must use schema_version 1 and a calibrations array.", ValidationSeverity.Error);
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in calibrations)
        {
            if (node is not JsonObject entry
                || !TryReadSchemaVersion(entry, out var entrySchemaVersion)
                || entrySchemaVersion != SupportedSchemaVersion
                || !TryReadRequiredString(entry, "calibration_id", out var calibrationId)
                || !IsValidCalibrationId(calibrationId)
                || !TryReadRequiredString(entry, "rig_id", out var rigId))
            {
                error = new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog contains an invalid calibration entry.", ValidationSeverity.Error);
                return false;
            }

            if (!ids.Add(calibrationId))
            {
                error = new ApiError("invalid_actor_calibration_catalog", "Actor calibration IDs must be unique.", ValidationSeverity.Error);
                return false;
            }

            var rig = rigCatalog.Rigs.SingleOrDefault(candidate => candidate.RigId == rigId);
            if (rig is null
                || !TryValidateExistingSocketOverrides(entry["sockets"], rig, out error)
                || !TryValidateExistingForegroundOverlayOverrides(entry["foreground_overlays"], rig, out error))
            {
                error ??= new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog references an unavailable rig.", ValidationSeverity.Error);
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateExistingSocketOverrides(JsonNode? node, ActorRigDefinition rig, out ApiError? error)
    {
        error = null;
        if (node is null)
        {
            return true;
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        if (TryParseSocketOverrides(document.RootElement, rig, out _, out error))
        {
            return true;
        }

        error = new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog contains invalid socket overrides.", ValidationSeverity.Error);
        return false;
    }

    private static bool TryValidateExistingForegroundOverlayOverrides(JsonNode? node, ActorRigDefinition rig, out ApiError? error)
    {
        error = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonObject overlays)
        {
            error = new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog contains invalid foreground overlay overrides.", ValidationSeverity.Error);
            return false;
        }

        var knownOverlayIds = rig.ForegroundOverlays.Select(overlay => overlay.OverlayId).ToHashSet(StringComparer.Ordinal);
        foreach (var overlay in overlays)
        {
            if (!knownOverlayIds.Contains(overlay.Key) || overlay.Value is not JsonObject overlayObject)
            {
                continue;
            }

            if (overlayObject["source_rect_by_direction"] is not JsonObject directions)
            {
                continue;
            }

            using var document = JsonDocument.Parse($"{{\"{overlay.Key}\":{directions.ToJsonString()}}}");
            if (!TryParseForegroundOverlayShape(document.RootElement, rig, out _, out _))
            {
                error = new ApiError("invalid_actor_calibration_catalog", "The actor calibration catalog contains invalid foreground overlay overrides.", ValidationSeverity.Error);
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSocketOverrides(
        JsonElement value,
        ActorRigDefinition rig,
        out JsonObject socketOverrides,
        out ApiError? error)
    {
        socketOverrides = new JsonObject();
        error = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            error = new ApiError("invalid_socket_overrides", "Socket overrides must be an object.", ValidationSeverity.Error, "socket_overrides");
            return false;
        }

        var rigSocketIds = rig.Sockets.Select(socket => socket.SocketId).ToHashSet(StringComparer.Ordinal);
        foreach (var socket in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (!rigSocketIds.Contains(socket.Name))
            {
                error = new ApiError("invalid_actor_socket_id", "Socket overrides must reference a socket in the selected rig.", ValidationSeverity.Error, "socket_overrides");
                return false;
            }

            if (socket.Value.ValueKind != JsonValueKind.Object)
            {
                error = new ApiError("invalid_socket_overrides", "Each socket override must be an object.", ValidationSeverity.Error, "socket_overrides");
                return false;
            }

            var directions = new JsonObject();
            foreach (var direction in Directions)
            {
                if (!socket.Value.TryGetProperty(direction, out var directionValue))
                {
                    continue;
                }

                if (directionValue.ValueKind != JsonValueKind.Object)
                {
                    error = new ApiError("invalid_socket_direction", "Socket override directions must contain frame objects.", ValidationSeverity.Error, "socket_overrides");
                    return false;
                }

                var frames = new JsonObject();
                foreach (var frame in Frames)
                {
                    if (!directionValue.TryGetProperty(frame, out var pointValue))
                    {
                        continue;
                    }

                    if (!TryReadCoordinatePoint(pointValue, out var point, out error))
                    {
                        return false;
                    }

                    frames[frame] = point;
                }

                foreach (var property in directionValue.EnumerateObject())
                {
                    if (!Frames.Contains(property.Name, StringComparer.Ordinal))
                    {
                        error = new ApiError("invalid_socket_frame", "Socket overrides may only use frames 1 through 4.", ValidationSeverity.Error, "socket_overrides");
                        return false;
                    }
                }

                directions[direction] = frames;
            }

            foreach (var property in socket.Value.EnumerateObject())
            {
                if (!Directions.Contains(property.Name, StringComparer.Ordinal))
                {
                    error = new ApiError("invalid_socket_direction", "Socket overrides may only use N, E, S, or W directions.", ValidationSeverity.Error, "socket_overrides");
                    return false;
                }
            }

            socketOverrides[socket.Name] = directions;
        }

        return true;
    }

    private bool TryParseForegroundOverlayOverrides(
        JsonElement value,
        ActorRigDefinition rig,
        string? actorKind,
        string? visualTexturePath,
        out JsonObject overlays,
        out ApiError? error)
    {
        if (!TryParseForegroundOverlayShape(value, rig, out overlays, out error))
        {
            return false;
        }

        if (_frameResolver is null)
        {
            error = new ApiError("actor_calibration_frame_validation_unavailable", "Exact actor frames are unavailable for foreground overlay validation.", ValidationSeverity.Error, "foreground_overlay_overrides");
            return false;
        }

        var frames = _frameResolver.Resolve(new CalibrationFrameRequest(actorKind ?? string.Empty, visualTexturePath ?? string.Empty));
        if (!frames.Succeeded || frames.Value is null)
        {
            error = frames.Errors.FirstOrDefault() ?? new ApiError("actor_calibration_frame_validation_unavailable", "Exact actor frames are unavailable for foreground overlay validation.", ValidationSeverity.Error, "foreground_overlay_overrides");
            return false;
        }

        foreach (var overlay in overlays)
        {
            var directions = overlay.Value as JsonObject ?? new JsonObject();
            foreach (var direction in directions)
            {
                var poses = direction.Value as JsonObject ?? new JsonObject();
                foreach (var pose in poses)
                {
                    var frame = frames.Value.Frames.Single(candidate => candidate.Direction == direction.Key && candidate.Frame.ToString() == pose.Key);
                    if (!frame.Available || frame.SourceWidth is null || frame.SourceHeight is null)
                    {
                        error = new ApiError("unavailable_actor_calibration_frame", "Foreground overlay overrides require an available exact actor frame.", ValidationSeverity.Error, "foreground_overlay_overrides");
                        return false;
                    }

                    var rectangle = pose.Value as JsonObject ?? new JsonObject();
                    var x = rectangle["x"]!.GetValue<int>();
                    var y = rectangle["y"]!.GetValue<int>();
                    var width = rectangle["width"]!.GetValue<int>();
                    var height = rectangle["height"]!.GetValue<int>();
                    if (x + width > frame.SourceWidth || y + height > frame.SourceHeight)
                    {
                        error = new ApiError("foreground_overlay_rectangle_out_of_bounds", "Foreground overlay rectangles must fit within the exact actor source frame.", ValidationSeverity.Error, "foreground_overlay_overrides");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool TryParseForegroundOverlayShape(
        JsonElement value,
        ActorRigDefinition rig,
        out JsonObject overlays,
        out ApiError? error)
    {
        overlays = new JsonObject();
        error = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            error = new ApiError("invalid_foreground_overlay_overrides", "Foreground overlay overrides must be an object.", ValidationSeverity.Error, "foreground_overlay_overrides");
            return false;
        }

        var rigOverlayIds = rig.ForegroundOverlays.Select(overlay => overlay.OverlayId).ToHashSet(StringComparer.Ordinal);
        foreach (var overlay in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (!rigOverlayIds.Contains(overlay.Name))
            {
                error = new ApiError("invalid_foreground_overlay_id", "Foreground overlay overrides must reference an overlay in the selected rig.", ValidationSeverity.Error, "foreground_overlay_overrides");
                return false;
            }

            if (overlay.Value.ValueKind != JsonValueKind.Object)
            {
                error = new ApiError("invalid_foreground_overlay_overrides", "Each foreground overlay override must be an object.", ValidationSeverity.Error, "foreground_overlay_overrides");
                return false;
            }

            var directions = new JsonObject();
            foreach (var direction in Directions)
            {
                if (!overlay.Value.TryGetProperty(direction, out var directionValue))
                {
                    continue;
                }

                if (directionValue.ValueKind != JsonValueKind.Object)
                {
                    error = new ApiError("invalid_foreground_overlay_direction", "Foreground overlay directions must contain frame objects.", ValidationSeverity.Error, "foreground_overlay_overrides");
                    return false;
                }

                var frames = new JsonObject();
                foreach (var frame in Frames)
                {
                    if (!directionValue.TryGetProperty(frame, out var rectangleValue))
                    {
                        continue;
                    }

                    if (!TryReadSourceRectangle(rectangleValue, out var rectangle, out error))
                    {
                        return false;
                    }

                    frames[frame] = rectangle;
                }

                if (directionValue.EnumerateObject().Any(property => !Frames.Contains(property.Name, StringComparer.Ordinal)))
                {
                    error = new ApiError("invalid_foreground_overlay_frame", "Foreground overlay overrides may only use frames 1 through 4.", ValidationSeverity.Error, "foreground_overlay_overrides");
                    return false;
                }

                directions[direction] = frames;
            }

            if (overlay.Value.EnumerateObject().Any(property => !Directions.Contains(property.Name, StringComparer.Ordinal)))
            {
                error = new ApiError("invalid_foreground_overlay_direction", "Foreground overlay overrides may only use N, E, S, or W directions.", ValidationSeverity.Error, "foreground_overlay_overrides");
                return false;
            }

            overlays[overlay.Name] = directions;
        }

        return true;
    }

    private static bool TryReadSourceRectangle(JsonElement value, out JsonObject rectangle, out ApiError? error)
    {
        rectangle = new JsonObject();
        error = null;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("x", out var xValue)
            || !value.TryGetProperty("y", out var yValue)
            || !value.TryGetProperty("width", out var widthValue)
            || !value.TryGetProperty("height", out var heightValue)
            || !TryReadCoordinate(xValue, out var x)
            || !TryReadCoordinate(yValue, out var y)
            || !TryReadCoordinate(widthValue, out var width)
            || !TryReadCoordinate(heightValue, out var height)
            || value.EnumerateObject().Any(property => property.Name is not ("x" or "y" or "width" or "height")))
        {
            error = new ApiError("invalid_foreground_overlay_rectangle", "Foreground overlay rectangles must define integer x, y, width, and height source pixels.", ValidationSeverity.Error, "foreground_overlay_overrides");
            return false;
        }

        if (x is < 0 or > CoordinateLimit || y is < 0 or > CoordinateLimit || width is < 1 or > CoordinateLimit || height is < 1 or > CoordinateLimit)
        {
            error = new ApiError("foreground_overlay_rectangle_out_of_range", "Foreground overlay rectangles must use nonnegative positions and positive dimensions within supported limits.", ValidationSeverity.Error, "foreground_overlay_overrides");
            return false;
        }

        rectangle["x"] = x;
        rectangle["y"] = y;
        rectangle["width"] = width;
        rectangle["height"] = height;
        return true;
    }

    private static JsonObject MergeForegroundOverlayOverrides(JsonObject? existing, ActorRigDefinition rig, JsonObject overrides)
    {
        var merged = existing is null ? new JsonObject() : (JsonObject)existing.DeepClone();
        var rigOverlayIds = rig.ForegroundOverlays.Select(overlay => overlay.OverlayId).ToHashSet(StringComparer.Ordinal);
        foreach (var overlayId in rigOverlayIds)
        {
            var overrideDirections = overrides[overlayId] as JsonObject;
            if (overrideDirections is null || overrideDirections.Count == 0)
            {
                if (merged[overlayId] is JsonObject current)
                {
                    current.Remove("source_rect_by_direction");
                    if (current.Count == 0)
                    {
                        merged.Remove(overlayId);
                    }
                }
                continue;
            }

            var overlay = merged[overlayId] as JsonObject ?? new JsonObject();
            overlay["source_rect_by_direction"] = overrideDirections.DeepClone();
            merged[overlayId] = overlay;
        }

        return merged;
    }

    private static bool TryReadCoordinatePoint(JsonElement value, out JsonObject point, out ApiError? error)
    {
        point = new JsonObject();
        error = null;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("x", out var xValue)
            || !value.TryGetProperty("y", out var yValue)
            || !TryReadCoordinate(xValue, out var x)
            || !TryReadCoordinate(yValue, out var y))
        {
            error = new ApiError("invalid_socket_coordinate", "Socket coordinates must be signed integer source pixels.", ValidationSeverity.Error, "socket_overrides");
            return false;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("x" or "y"))
            {
                error = new ApiError("invalid_socket_coordinate", "Socket coordinate points may only define x and y.", ValidationSeverity.Error, "socket_overrides");
                return false;
            }
        }

        if (x is < -CoordinateLimit or > CoordinateLimit || y is < -CoordinateLimit or > CoordinateLimit)
        {
            error = new ApiError("socket_coordinate_out_of_range", "Socket coordinates must be between -4096 and 4096.", ValidationSeverity.Error, "socket_overrides");
            return false;
        }

        point["x"] = x;
        point["y"] = y;
        return true;
    }

    private static bool TryReadCoordinate(JsonElement value, out int coordinate)
    {
        coordinate = 0;
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var numericValue)
            || decimal.Truncate(numericValue) != numericValue
            || numericValue < int.MinValue
            || numericValue > int.MaxValue)
        {
            return false;
        }

        coordinate = decimal.ToInt32(numericValue);
        return true;
    }

    private static JsonObject CreateCanonicalCatalog(JsonObject source, IEnumerable<JsonObject> entries)
    {
        var calibrations = new JsonArray();
        foreach (var entry in entries.OrderBy(ReadCalibrationId, StringComparer.Ordinal))
        {
            var canonicalEntry = new JsonObject
            {
                ["schema_version"] = entry["schema_version"]?.DeepClone(),
                ["calibration_id"] = entry["calibration_id"]?.DeepClone(),
                ["rig_id"] = entry["rig_id"]?.DeepClone(),
                ["sockets"] = CanonicalizeSockets(entry["sockets"] as JsonObject)
            };

            if (entry["foreground_overlays"] is JsonObject foregroundOverlays)
            {
                canonicalEntry["foreground_overlays"] = CanonicalizeForegroundOverlays(foregroundOverlays);
            }

            foreach (var property in entry.Where(property => property.Key is not ("schema_version" or "calibration_id" or "rig_id" or "sockets" or "foreground_overlays")).OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                canonicalEntry[property.Key] = CanonicalizeNode(property.Value);
            }

            calibrations.Add(canonicalEntry);
        }

        var canonicalRoot = new JsonObject
        {
            ["schema_version"] = SupportedSchemaVersion,
            ["calibrations"] = calibrations
        };

        foreach (var property in source.Where(property => property.Key is not ("schema_version" or "calibrations")).OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            canonicalRoot[property.Key] = CanonicalizeNode(property.Value);
        }

        return canonicalRoot;
    }

    private static JsonObject CanonicalizeSockets(JsonObject? sockets)
    {
        var canonicalSockets = new JsonObject();
        if (sockets is null)
        {
            return canonicalSockets;
        }

        foreach (var socket in sockets.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            var canonicalDirections = new JsonObject();
            if (socket.Value is JsonObject directions)
            {
                foreach (var direction in Directions)
                {
                    if (directions[direction] is not JsonObject frames)
                    {
                        continue;
                    }

                    var canonicalFrames = new JsonObject();
                    foreach (var frame in Frames)
                    {
                        if (frames[frame] is JsonObject point)
                        {
                            canonicalFrames[frame] = new JsonObject
                            {
                                ["x"] = point["x"]?.DeepClone(),
                                ["y"] = point["y"]?.DeepClone()
                            };
                        }
                    }

                    canonicalDirections[direction] = canonicalFrames;
                }
            }

            canonicalSockets[socket.Key] = canonicalDirections;
        }

        return canonicalSockets;
    }

    private static JsonObject CanonicalizeForegroundOverlays(JsonObject overlays)
    {
        var canonicalOverlays = new JsonObject();
        foreach (var overlay in overlays.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            if (overlay.Value is not JsonObject overlayObject)
            {
                canonicalOverlays[overlay.Key] = CanonicalizeNode(overlay.Value);
                continue;
            }

            var canonicalOverlay = new JsonObject();
            if (overlayObject["source_rect_by_direction"] is JsonObject directions)
            {
                var canonicalDirections = new JsonObject();
                foreach (var direction in Directions)
                {
                    if (directions[direction] is not JsonObject frames)
                    {
                        continue;
                    }

                    var canonicalFrames = new JsonObject();
                    foreach (var frame in Frames)
                    {
                        if (frames[frame] is JsonObject rectangle)
                        {
                            canonicalFrames[frame] = new JsonObject
                            {
                                ["x"] = rectangle["x"]?.DeepClone(),
                                ["y"] = rectangle["y"]?.DeepClone(),
                                ["width"] = rectangle["width"]?.DeepClone(),
                                ["height"] = rectangle["height"]?.DeepClone()
                            };
                        }
                    }

                    canonicalDirections[direction] = canonicalFrames;
                }

                canonicalOverlay["source_rect_by_direction"] = canonicalDirections;
            }

            foreach (var property in overlayObject.Where(property => property.Key != "source_rect_by_direction").OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                canonicalOverlay[property.Key] = CanonicalizeNode(property.Value);
            }

            canonicalOverlays[overlay.Key] = canonicalOverlay;
        }

        return canonicalOverlays;
    }

    private static JsonNode? CanonicalizeNode(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, CanonicalizeNode(property.Value)))),
        JsonArray value => new JsonArray(value.Select(CanonicalizeNode).ToArray()),
        _ => node?.DeepClone()
    };

    private static byte[] SerializeCanonical(JsonObject root) =>
        Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

    private static string WriteTemporaryFile(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("Calibration catalog directory is unavailable.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        return temporaryPath;
    }

    private static AuthoringOperationResult<byte[]> ReadCurrentCatalogBytes(string path)
    {
        try
        {
            return AuthoringOperationResult<byte[]>.Success(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return AuthoringOperationResult<byte[]>.Failure(
                new ApiError("actor_calibration_catalog_unavailable", "The actor calibration catalog could not be read.", ValidationSeverity.Error));
        }
    }

    private static string ComputeHash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonObject? FindCalibration(JsonArray calibrations, string? calibrationId) =>
        calibrations.OfType<JsonObject>().SingleOrDefault(entry =>
            string.Equals(ReadCalibrationId(entry), calibrationId, StringComparison.Ordinal));

    private static string? ReadCalibrationId(JsonObject entry) =>
        TryReadRequiredString(entry, "calibration_id", out var calibrationId) ? calibrationId : null;

    private static bool TryReadSchemaVersion(JsonObject entry, out int schemaVersion)
    {
        schemaVersion = 0;
        return entry["schema_version"] is JsonValue value && value.TryGetValue<int>(out schemaVersion);
    }

    private static bool TryReadRequiredString(JsonObject entry, string name, out string value)
    {
        value = string.Empty;
        return entry[name] is JsonValue node
            && node.TryGetValue<string>(out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && (value = raw.Trim()) is not null;
    }

    private static JsonElement ToJsonElement(JsonObject entry)
    {
        using var document = JsonDocument.Parse(entry.ToJsonString());
        return document.RootElement.Clone();
    }

    private static bool IsValidCalibrationId(string? calibrationId) =>
        !string.IsNullOrWhiteSpace(calibrationId) && CalibrationIdPattern.IsMatch(calibrationId);

    private static AuthoringOperationResult<ActorCalibrationLoadResponse> Failure(string code, string message, string? field = null) =>
        AuthoringOperationResult<ActorCalibrationLoadResponse>.Failure(
            new ApiError(code, message, ValidationSeverity.Error, field));

    private static AuthoringOperationResult<CalibrationCatalogFile> FailureCatalog(string code, string message) =>
        AuthoringOperationResult<CalibrationCatalogFile>.Failure(
            new ApiError(code, message, ValidationSeverity.Error));

    private sealed record CalibrationCatalogFile(
        string Path,
        byte[] Bytes,
        string Hash,
        JsonObject Root,
        JsonArray Calibrations);
}
