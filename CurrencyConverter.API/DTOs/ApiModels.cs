using System.ComponentModel.DataAnnotations;

namespace CurrencyConverter.API.DTOs;

// Core API DTOs
public class ConversionRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string FromCurrency { get; set; } = string.Empty;

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string ToCurrency { get; set; } = string.Empty;
}

public class ConversionResponse
{
    public decimal OriginalAmount { get; set; }
    public decimal ConvertedAmount { get; set; }
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public DateTime Timestamp { get; set; }
}

public class LatestRatesResponse
{
    public string BaseCurrency { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Dictionary<string, decimal> Rates { get; set; } = new();
}

public class RangeRatesResponse
{
    public string BaseCurrency { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public Dictionary<string, Dictionary<string, decimal>> Rates { get; set; } = new();
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? TraceId { get; set; }
}

// JWT Token DTOs
public class TokenRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ClientId { get; set; } = string.Empty;

    [StringLength(200)]
    public string? ClientName { get; set; }

    [StringLength(50)]
    public string Role { get; set; } = "ApiUser";

    [Range(1, 1440)] // 1 minute to 24 hours
    public int ExpirationMinutes { get; set; } = 60;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
