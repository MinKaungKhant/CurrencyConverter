namespace CurrencyConverter.API.Configuration;

public class ApiSettings
{
    public const string SectionName = "ApiSettings";
    
    public RateLimitingSettings RateLimiting { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public ExternalApiSettings ExternalApis { get; set; } = new();
}

public class RateLimitingSettings
{
    public int RequestsPerMinute { get; set; } = 100;
    public int RequestsPerHour { get; set; } = 1000;
    public int RequestsPerDay { get; set; } = 10000;
    public bool EnableThrottling { get; set; } = true;
}

public class CacheSettings
{
    public int DefaultExpirationMinutes { get; set; } = 15;
    public int ExchangeRatesCacheMinutes { get; set; } = 5;
    public int HistoricalRatesCacheHours { get; set; } = 24;
    public bool EnableDistributedCache { get; set; } = true;
    public bool FallbackToMemoryCache { get; set; } = true;
}

public class ExternalApiSettings
{
    public FrankfurterApiSettings FrankfurterApi { get; set; } = new();
}

public class FrankfurterApiSettings
{
    public string BaseUrl { get; set; } = "https://api.frankfurter.app";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerTimeoutSeconds { get; set; } = 30;
    public bool EnableCircuitBreaker { get; set; } = true;
    public bool EnableRetryPolicy { get; set; } = true;
}
