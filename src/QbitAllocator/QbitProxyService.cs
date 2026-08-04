namespace QbitAllocator;

public sealed class QbitProxyService
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Content-Length", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host"
    };

    private readonly HttpClient _client;
    private readonly AddRequestRewriter _rewriter;
    private readonly AllocatorStatusStore _status;

    public QbitProxyService(HttpClient client, AddRequestRewriter rewriter, AllocatorStatusStore status)
    {
        _client = client;
        _rewriter = rewriter;
        _status = status;
    }

    public async Task ForwardAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var built = await BuildRequestAsync(context.Request, cancellationToken);
        if (built.RejectedReason is not null)
        {
            context.Response.StatusCode = StatusCodes.Status507InsufficientStorage;
            await context.Response.WriteAsync(built.RejectedReason, cancellationToken);
            return;
        }

        using var upstream = built.Request!;
        using var response = await _client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");

        await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    public async Task<ProxyBuildResult> BuildRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var pathAndQuery = RewriteCompatibilityPath(request.PathBase + request.Path + request.QueryString);
        var message = new HttpRequestMessage(new HttpMethod(request.Method), pathAndQuery);

        if (IsInterceptedAdd(request))
        {
            var rewritten = await _rewriter.RewriteAsync(request, cancellationToken);
            if (!rewritten.Assigned)
            {
                _status.AddSkipped("", null, rewritten.Reason!);
                return ProxyBuildResult.Rejected(rewritten.Reason!);
            }
            else
            {
                message.Content = rewritten.Content;
            }
        }
        else if (request.ContentLength.GetValueOrDefault() > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, cancellationToken);
            message.Content = new ByteArrayContent(buffer.ToArray());
        }

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            if (IsInterceptedAdd(request) && string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return ProxyBuildResult.Success(message);
    }

    private static bool IsInterceptedAdd(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method) &&
            string.Equals(request.Path.Value, "/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase);
    }

    private static string RewriteCompatibilityPath(string pathAndQuery)
    {
        if (pathAndQuery.StartsWith("/api/v2/torrents/pause", StringComparison.OrdinalIgnoreCase))
        {
            return "/api/v2/torrents/stop" + pathAndQuery["/api/v2/torrents/pause".Length..];
        }

        if (pathAndQuery.StartsWith("/api/v2/torrents/resume", StringComparison.OrdinalIgnoreCase))
        {
            return "/api/v2/torrents/start" + pathAndQuery["/api/v2/torrents/resume".Length..];
        }

        return pathAndQuery;
    }
}

public sealed record ProxyBuildResult(HttpRequestMessage? Request, string? RejectedReason)
{
    public static ProxyBuildResult Success(HttpRequestMessage request) => new(request, null);
    public static ProxyBuildResult Rejected(string reason) => new(null, reason);
}
