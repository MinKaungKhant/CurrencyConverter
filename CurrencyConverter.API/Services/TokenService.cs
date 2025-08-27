using CurrencyConverter.API.DTOs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CurrencyConverter.API.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly IDistributedCache _distributedCache;

    public TokenService(
        IConfiguration configuration, 
        ILogger<TokenService> logger,
        IDistributedCache distributedCache)
    {
        _configuration = configuration;
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();
        _distributedCache = distributedCache;
    }

    public TokenResponse GenerateToken(TokenRequest request)
    {
        try
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var secretKey = jwtSettings["SecretKey"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            if (string.IsNullOrEmpty(secretKey))
            {
                throw new InvalidOperationException("JWT SecretKey is not configured");
            }

            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expirationTime = DateTime.UtcNow.AddMinutes(request.ExpirationMinutes);
            var jti = Guid.NewGuid().ToString();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, request.ClientId),
                new Claim("client_id", request.ClientId),
                new Claim("client_name", request.ClientName ?? request.ClientId),
                new Claim(ClaimTypes.Role, request.Role),
                new Claim("sub", request.ClientId),
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim(JwtRegisteredClaimNames.Iat, 
                    new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), 
                    ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expirationTime,
                signingCredentials: credentials
            );

            var tokenString = _tokenHandler.WriteToken(token);

            // Store token in cache for tracking - SYNCHRONOUSLY to ensure immediate availability
            try
            {
                var cacheKey = $"active_token:{request.ClientId}";
                var tokenInfo = new
                {
                    Token = tokenString,
                    Jti = jti,
                    ExpiresAt = expirationTime,
                    Role = request.Role,
                    GeneratedAt = DateTime.UtcNow
                };

                var cacheExpiration = TimeSpan.FromMinutes(request.ExpirationMinutes + 5); // Add buffer
                _distributedCache.SetString(cacheKey, 
                    System.Text.Json.JsonSerializer.Serialize(tokenInfo),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = cacheExpiration
                    });

                _logger.LogDebug("Token cached synchronously for client: {ClientId}, JTI: {Jti}", request.ClientId, jti);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache token for client: {ClientId}", request.ClientId);
                // Don't throw here - token generation should still succeed even if caching fails
            }

            _logger.LogInformation("JWT token generated for client: {ClientId}, Role: {Role}, Expires: {ExpiresAt}", 
                request.ClientId, request.Role, expirationTime);

            return new TokenResponse
            {
                AccessToken = tokenString,
                TokenType = "Bearer",
                ExpiresIn = request.ExpirationMinutes * 60, // Convert to seconds
                ExpiresAt = expirationTime,
                ClientId = request.ClientId,
                Role = request.Role
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating JWT token for client: {ClientId}", request.ClientId);
            throw;
        }
    }

    public async Task<bool> ValidateTokenAsync(string clientId, string token)
    {
        try
        {
            var cacheKey = $"active_token:{clientId}";
            var cachedTokenInfo = await _distributedCache.GetStringAsync(cacheKey);
            
            if (string.IsNullOrEmpty(cachedTokenInfo))
            {
                _logger.LogDebug("No cached token found for client: {ClientId}", clientId);
                return false;
            }

            var tokenInfo = System.Text.Json.JsonSerializer.Deserialize<TokenInfo>(cachedTokenInfo);
            var isValid = tokenInfo?.Token == token && tokenInfo.ExpiresAt > DateTime.UtcNow;

            if (isValid && tokenInfo != null)
            {
                // Check if token is blacklisted
                var isBlacklisted = await IsTokenBlacklistedAsync(tokenInfo.Jti);
                if (isBlacklisted)
                {
                    _logger.LogDebug("Token is blacklisted for client: {ClientId}, JTI: {Jti}", clientId, tokenInfo.Jti);
                    return false;
                }
            }

            _logger.LogDebug("Token validation result for client {ClientId}: {IsValid}", clientId, isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token for client: {ClientId}", clientId);
            return false;
        }
    }

    public async Task RevokeTokenAsync(string clientId)
    {
        try
        {
            var cacheKey = $"active_token:{clientId}";
            await _distributedCache.RemoveAsync(cacheKey);
            _logger.LogInformation("Token revoked for client: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token for client: {ClientId}", clientId);
        }
    }

    public async Task RevokeAllTokensAsync(string clientId)
    {
        try
        {
            // Remove active token
            await RevokeTokenAsync(clientId);
            
            // Could extend this to handle multiple tokens per client if needed
            _logger.LogInformation("All tokens revoked for client: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking all tokens for client: {ClientId}", clientId);
        }
    }

    public async Task<bool> IsTokenBlacklistedAsync(string jti)
    {
        try
        {
            var blacklistKey = $"blacklisted_token:{jti}";
            var blacklisted = await _distributedCache.GetStringAsync(blacklistKey);
            return !string.IsNullOrEmpty(blacklisted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token blacklist for JTI: {Jti}", jti);
            return false; // Assume not blacklisted if we can't check
        }
    }

    public async Task BlacklistTokenAsync(string jti, TimeSpan? expiration = null)
    {
        try
        {
            var blacklistKey = $"blacklisted_token:{jti}";
            var cacheOptions = new DistributedCacheEntryOptions();
            
            if (expiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = expiration.Value;
            }
            else
            {
                // Default to 24 hours if no expiration provided
                cacheOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            }

            await _distributedCache.SetStringAsync(blacklistKey, 
                DateTime.UtcNow.ToString(), cacheOptions);
            
            _logger.LogInformation("Token blacklisted with JTI: {Jti}", jti);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blacklisting token with JTI: {Jti}", jti);
        }
    }

    private class TokenInfo
    {
        public string Token { get; set; } = string.Empty;
        public string Jti { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Role { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
    }
}