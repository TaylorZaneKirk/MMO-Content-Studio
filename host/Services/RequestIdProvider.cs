namespace MMO.ContentStudio.AuthoringHost.Services;

public static class RequestIdProvider
{
    public static string Resolve(HttpContext context)
    {
        const string headerName = "X-Request-Id";
        var supplied = context.Request.Headers[headerName].FirstOrDefault();
        return string.IsNullOrWhiteSpace(supplied)
            ? Guid.NewGuid().ToString("N")
            : supplied.Trim();
    }
}
