using System.Diagnostics;
using System.Security.Claims;

namespace CurrencyConverter.API.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = GetClientIpAddress(context);
        var clientId = GetClientIdFromToken(context);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms. ClientIP: {ClientIP}, ClientId: {ClientId}, TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                clientIp,
                clientId ?? "Anonymous",
                context.TraceIdentifier);
        }
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Try to get IP from X-Forwarded-For header (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Try to get IP from X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private static string? GetClientIdFromToken(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                   context.User.FindFirst("client_id")?.Value ??
                   context.User.FindFirst("sub")?.Value;
        }

        return null;
    }
}
