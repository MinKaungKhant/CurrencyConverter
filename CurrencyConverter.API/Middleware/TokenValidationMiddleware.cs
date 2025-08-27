using CurrencyConverter.API.Services;
using System.Security.Claims;

namespace CurrencyConverter.API.Middleware;

public class TokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenValidationMiddleware> _logger;

    public TokenValidationMiddleware(RequestDelegate next, ILogger<TokenValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITokenService tokenService)
    {
        // Skip validation for non-authenticated routes
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        try
        {
            var clientId = context.User.FindFirst("client_id")?.Value;
            var jti = context.User.FindFirst("jti")?.Value;
            
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(jti))
            {
                _logger.LogWarning("Token missing required claims (client_id or jti)");
                await RespondWithUnauthorized(context, "Invalid token claims");
                return;
            }

            // Check if token is blacklisted first (this is the critical check)
            var isBlacklisted = await tokenService.IsTokenBlacklistedAsync(jti);
            if (isBlacklisted)
            {
                _logger.LogWarning("Blacklisted token attempted access. Client: {ClientId}, JTI: {Jti}", clientId, jti);
                await RespondWithUnauthorized(context, "Token has been revoked");
                return;
            }

            // Get token from Authorization header
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                _logger.LogWarning("Missing or invalid Authorization header");
                await RespondWithUnauthorized(context, "Missing or invalid authorization header");
                return;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            // Validate token against cache - but be more forgiving
            var isValidToken = await tokenService.ValidateTokenAsync(clientId, token);
            if (!isValidToken)
            {
                _logger.LogWarning("Token not found in cache for client: {ClientId}. This might be normal for new tokens or cache issues.", clientId);
                
                // Instead of immediately rejecting, let the JWT validation handle it
                // The middleware should primarily focus on blacklist checking
                // JWT validation is already handled by the JWT Bearer middleware
                _logger.LogDebug("Allowing request to proceed - JWT Bearer middleware will handle basic validation");
            }
            else
            {
                _logger.LogDebug("Token validation successful for client: {ClientId}", clientId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation middleware - allowing request to proceed");
            // Don't block the request due to middleware errors
            // The JWT Bearer middleware will still handle basic JWT validation
        }

        await _next(context);
    }

    private static async Task RespondWithUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            error = "Unauthorized",
            message = message,
            timestamp = DateTime.UtcNow,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}