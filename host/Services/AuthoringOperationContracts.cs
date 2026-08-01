using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

/// <summary>
/// Application boundary for one complete logical content mutation.
/// Implementations added in T1+ must validate the aggregate, calculate all
/// dependent persistence changes, execute them in one transaction, then reload
/// and verify the persisted aggregate before returning success.
/// </summary>
public interface IAuthoringOperation<in TRequest, TResult>
{
    Task<AuthoringOperationResult<TResult>> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AuthoringOperationResult<T>(
    bool Succeeded,
    T? Value,
    IReadOnlyList<ApiError> Errors)
{
    public static AuthoringOperationResult<T> Success(T value) =>
        new(true, value, []);

    public static AuthoringOperationResult<T> Failure(params ApiError[] errors) =>
        new(false, default, errors);
}

public interface IContentAggregateReader<TResult>
{
    Task<TResult?> LoadAsync(string stableId, CancellationToken cancellationToken = default);
}
