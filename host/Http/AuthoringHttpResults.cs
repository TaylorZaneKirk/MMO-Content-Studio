using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Http;

public static class AuthoringHttpResults
{
    private static readonly HashSet<string> ConflictCodes = new(StringComparer.Ordinal)
    {
        "item_version_conflict",
        "wrong_authoring_workspace",
        "item_has_live_references",
        "item_has_published_consumable_references",
        "consumable_profile_missing",
        "weapon_or_tool_requires_t3b"
    };

    public static IResult Ok<T>(HttpContext context, T value)
    {
        var requestId = RequestIdProvider.Resolve(context);
        return Results.Ok(ApiEnvelope<T>.Ok(requestId, value));
    }

    public static IResult FromOperation<T>(
        HttpContext context,
        AuthoringOperationResult<T> result)
    {
        var requestId = RequestIdProvider.Resolve(context);
        if (result.Succeeded && result.Value is not null)
        {
            return Results.Ok(ApiEnvelope<T>.Ok(requestId, result.Value));
        }

        var envelope = ApiEnvelope<T>.Failure(requestId, result.Errors.ToArray());
        var codes = result.Errors
            .Select(error => error.Code)
            .ToHashSet(StringComparer.Ordinal);

        if (codes.Contains("item_not_found"))
        {
            return Results.NotFound(envelope);
        }

        if (codes.Overlaps(ConflictCodes))
        {
            return Results.Conflict(envelope);
        }

        if (codes.Contains("database_unavailable"))
        {
            return Results.Json(
                envelope,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.BadRequest(envelope);
    }
}
