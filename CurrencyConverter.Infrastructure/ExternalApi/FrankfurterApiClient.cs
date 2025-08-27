using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Entities;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CurrencyConverter.Infrastructure.ExternalApi;

public class FrankfurterApiClient : ICurrencyProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FrankfurterApiClient> _logger;
    private const string BaseUrl = "https://api.frankfurter.app";

    public string ProviderName => "Frankfurter";

    public FrankfurterApiClient(HttpClient httpClient, ILogger<FrankfurterApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<Dictionary<string, decimal>> GetLatestRatesAsync(string baseCurrency, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/latest?from={baseCurrency}", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Frankfurter API returned {StatusCode} for latest rates request", response.StatusCode);
                throw new ExternalApiException($"Frankfurter API returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<FrankfurterLatestResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Rates == null)
            {
                throw new ExternalApiException("Invalid response from Frankfurter API");
            }

            _logger.LogInformation("Successfully retrieved latest rates for {BaseCurrency} from Frankfurter API", baseCurrency);
            return apiResponse.Rates;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error when calling Frankfurter API for latest rates");
            throw new ExternalApiException("Network error when calling external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse response from Frankfurter API");
            throw new ExternalApiException("Failed to parse external API response", ex);
        }
    }

    public async Task<IEnumerable<ExchangeRate>> GetHistoricalRatesAsync(string baseCurrency, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            var startDateStr = startDate.ToString("yyyy-MM-dd");
            var endDateStr = endDate.ToString("yyyy-MM-dd");
            
            var response = await _httpClient.GetAsync($"/{startDateStr}..{endDateStr}?from={baseCurrency}", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Frankfurter API returned {StatusCode} for historical rates request", response.StatusCode);
                throw new ExternalApiException($"Frankfurter API returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<FrankfurterHistoricalResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Rates == null)
            {
                throw new ExternalApiException("Invalid response from Frankfurter API");
            }

            var exchangeRates = new List<ExchangeRate>();

            foreach (var dateRates in apiResponse.Rates)
            {
                if (DateTime.TryParse(dateRates.Key, out var date))
                {
                    foreach (var rate in dateRates.Value)
                    {
                        exchangeRates.Add(new ExchangeRate
                        {
                            BaseCurrency = baseCurrency,
                            TargetCurrency = rate.Key,
                            Rate = rate.Value,
                            Date = date,
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }
            }

            _logger.LogInformation("Successfully retrieved historical rates for {BaseCurrency} from {StartDate} to {EndDate} from Frankfurter API", 
                baseCurrency, startDate, endDate);
            
            return exchangeRates;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error when calling Frankfurter API for historical rates");
            throw new ExternalApiException("Network error when calling external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse response from Frankfurter API");
            throw new ExternalApiException("Failed to parse external API response", ex);
        }
    }

    public async Task<IEnumerable<Currency>> GetSupportedCurrenciesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/currencies", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Frankfurter API returned {StatusCode} for currencies request", response.StatusCode);
                throw new ExternalApiException($"Frankfurter API returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var currencies = JsonSerializer.Deserialize<Dictionary<string, string>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (currencies == null)
            {
                throw new ExternalApiException("Invalid response from Frankfurter API");
            }

            _logger.LogInformation("Successfully retrieved supported currencies from Frankfurter API");
            
            return currencies.Select(c => new Currency
            {
                Code = c.Key,
                Name = c.Value,
                IsActive = true
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error when calling Frankfurter API for currencies");
            throw new ExternalApiException("Network error when calling external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse response from Frankfurter API");
            throw new ExternalApiException("Failed to parse external API response", ex);
        }
    }
}

// Response DTOs for Frankfurter API
internal class FrankfurterLatestResponse
{
    public decimal Amount { get; set; }
    public string Base { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public Dictionary<string, decimal> Rates { get; set; } = new();
}

internal class FrankfurterHistoricalResponse
{
    public decimal Amount { get; set; }
    public string Base { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, decimal>> Rates { get; set; } = new();
}
