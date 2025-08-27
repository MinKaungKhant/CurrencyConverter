using System.Collections.Concurrent;
using System.Net;

namespace CurrencyConverter.API.Middleware;

public class ApiThrottlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiThrottlingMiddleware> _logger;
    private readonly ThrottlingOptions _options;
    private static readonly ConcurrentDictionary<string, ClientRequestInfo> _clients = new();

    public ApiThrottlingMiddleware(RequestDelegate next, ILogger<ApiThrottlingMiddleware> logger, ThrottlingOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var now = DateTime.UtcNow;

        var clientInfo = _clients.AddOrUpdate(clientId,
            new ClientRequestInfo { LastRequestTime = now, RequestCount = 1 },
            (key, existing) =>
            {
                if (now - existing.LastRequestTime > _options.TimeWindow)
                {
                    existing.RequestCount = 1;
                    existing.LastRequestTime = now;
                }
                else
                {
                    existing.RequestCount++;
                }
                return existing;
            });

        if (clientInfo.RequestCount > _options.MaxRequests)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}. Requests: {RequestCount}, Limit: {MaxRequests}",
                clientId, clientInfo.RequestCount, _options.MaxRequests);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers.Append("Retry-After", _options.TimeWindow.TotalSeconds.ToString());
            
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        await _next(context);
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // Try to get client ID from JWT token
        var clientIdClaim = context.User.FindFirst("client_id")?.Value;
        if (!string.IsNullOrEmpty(clientIdClaim))
        {
            return clientIdClaim;
        }

        // Fall back to IP address
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}

public class ThrottlingOptions
{
    public int MaxRequests { get; set; } = 100;
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromMinutes(1);
}

public class ClientRequestInfo
{
    public DateTime LastRequestTime { get; set; }
    public int RequestCount { get; set; }
}
