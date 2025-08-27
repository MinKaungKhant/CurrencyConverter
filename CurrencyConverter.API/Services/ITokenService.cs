using CurrencyConverter.API.DTOs;

namespace CurrencyConverter.API.Services;

public interface ITokenService
{
    TokenResponse GenerateToken(TokenRequest request);
    Task<bool> ValidateTokenAsync(string clientId, string token);
    Task RevokeTokenAsync(string clientId);
    Task RevokeAllTokensAsync(string clientId);
    Task<bool> IsTokenBlacklistedAsync(string jti);
    Task BlacklistTokenAsync(string jti, TimeSpan? expiration = null);
}