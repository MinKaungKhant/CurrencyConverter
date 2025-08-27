using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Entities;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CurrencyConverter.Application.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly ICurrencyProvider _currencyProvider;
    private readonly ICacheService _cacheService;
    private readonly ILogger<ExchangeRateService> _logger;

    // Excluded currencies as per requirements
    private static readonly HashSet<string> ExcludedCurrencies = new()
    {
        "TRY", "PLN", "THB", "MXN"
    };

    public ExchangeRateService(
        ICurrencyProvider currencyProvider,
        ICacheService cacheService,
        ILogger<ExchangeRateService> logger)
    {
        _currencyProvider = currencyProvider;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<Dictionary<string, decimal>> GetLatestExchangeRatesAsync(string baseCurrency, CancellationToken cancellationToken = default)
    {
        ValidateCurrency(baseCurrency);

        var cacheKey = $"latest_rates_{baseCurrency}";
        
        // Try to get from cache first
        var cachedRates = await _cacheService.GetAsync<Dictionary<string, decimal>>(cacheKey, cancellationToken);
        if (cachedRates != null)
        {
            _logger.LogInformation("Retrieved latest exchange rates for {BaseCurrency} from cache", baseCurrency);
            return FilterExcludedCurrencies(cachedRates);
        }

        try
        {
            var rates = await _currencyProvider.GetLatestRatesAsync(baseCurrency, cancellationToken);
            
            // Cache for 15 minutes
            await _cacheService.SetAsync(cacheKey, rates, TimeSpan.FromMinutes(15), cancellationToken);
            
            _logger.LogInformation("Retrieved latest exchange rates for {BaseCurrency} from provider", baseCurrency);
            return FilterExcludedCurrencies(rates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve latest exchange rates for {BaseCurrency}", baseCurrency);
            throw new ExternalApiException("Failed to retrieve exchange rates", ex);
        }
    }

    public async Task<ExchangeRate> GetExchangeRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default)
    {
        ValidateCurrency(baseCurrency);
        ValidateCurrency(targetCurrency);

        var rates = await GetLatestExchangeRatesAsync(baseCurrency, cancellationToken);
        
        if (!rates.TryGetValue(targetCurrency, out var rate))
        {
            throw new ExchangeRateNotFoundException(baseCurrency, targetCurrency);
        }

        return new ExchangeRate
        {
            BaseCurrency = baseCurrency,
            TargetCurrency = targetCurrency,
            Rate = rate,
            Date = DateTime.UtcNow.Date,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<IEnumerable<ExchangeRate>> GetHistoricalExchangeRatesAsync(
        string baseCurrency, 
        DateTime startDate, 
        DateTime endDate, 
        int page, 
        int pageSize, 
        CancellationToken cancellationToken = default)
    {
        ValidateCurrency(baseCurrency);
        
        if (startDate > endDate)
        {
            throw new ArgumentException("Start date cannot be after end date");
        }

        if (page < 1 || pageSize < 1)
        {
            throw new ArgumentException("Page and page size must be greater than 0");
        }

        var cacheKey = $"historical_rates_{baseCurrency}_{startDate:yyyy-MM-dd}_{endDate:yyyy-MM-dd}";
        
        var cachedRates = await _cacheService.GetAsync<List<ExchangeRate>>(cacheKey, cancellationToken);
        if (cachedRates != null)
        {
            _logger.LogInformation("Retrieved historical exchange rates for {BaseCurrency} from cache", baseCurrency);
            return ApplyPagination(FilterExcludedCurrenciesFromRates(cachedRates), page, pageSize);
        }

        try
        {
            var rates = await _currencyProvider.GetHistoricalRatesAsync(baseCurrency, startDate, endDate, cancellationToken);
            var ratesList = rates.ToList();
            
            // Cache for 1 hour
            await _cacheService.SetAsync(cacheKey, ratesList, TimeSpan.FromHours(1), cancellationToken);
            
            _logger.LogInformation("Retrieved historical exchange rates for {BaseCurrency} from provider", baseCurrency);
            return ApplyPagination(FilterExcludedCurrenciesFromRates(ratesList), page, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve historical exchange rates for {BaseCurrency}", baseCurrency);
            throw new ExternalApiException("Failed to retrieve historical exchange rates", ex);
        }
    }

    private static void ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency cannot be null or empty");
        }

        if (ExcludedCurrencies.Contains(currency.ToUpper()))
        {
            throw new UnsupportedCurrencyException(currency);
        }
    }

    private static Dictionary<string, decimal> FilterExcludedCurrencies(Dictionary<string, decimal> rates)
    {
        return rates.Where(kvp => !ExcludedCurrencies.Contains(kvp.Key.ToUpper()))
                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static IEnumerable<ExchangeRate> FilterExcludedCurrenciesFromRates(IEnumerable<ExchangeRate> rates)
    {
        return rates.Where(rate => !ExcludedCurrencies.Contains(rate.TargetCurrency.ToUpper()) &&
                                  !ExcludedCurrencies.Contains(rate.BaseCurrency.ToUpper()));
    }

    private static IEnumerable<ExchangeRate> ApplyPagination(IEnumerable<ExchangeRate> rates, int page, int pageSize)
    {
        return rates.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
