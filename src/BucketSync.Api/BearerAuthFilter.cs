using BucketSync.Core;

namespace BucketSync.Api;

public sealed class BearerAuthFilter : IEndpointFilter
{
    public const string BucketIdItem = "bucketId";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var token = ExtractBearer(http.Request);
        if (token is null) return Results.Unauthorized();

        var sessions = http.RequestServices.GetRequiredService<ISessionStore>();
        var session = await sessions.ValidateAsync(token, http.RequestAborted);
        if (session is null) return Results.Unauthorized();

        http.Items[BucketIdItem] = session.BucketId;
        return await next(ctx);
    }

    public static string? ExtractBearer(HttpRequest req)
    {
        var h = req.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? h[prefix.Length..].Trim() : null;
    }
}
