using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CurrencyConverter.Application.Services;

public class CurrencyConversionService : ICurrencyConversionService
{
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<CurrencyConversionService> _logger;

    public CurrencyConversionService(
        IExchangeRateService exchangeRateService,
        ILogger<CurrencyConversionService> logger)
    {
        _exchangeRateService = exchangeRateService;
        _logger = logger;
    }

    public async Task<decimal> ConvertCurrencyAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken = default)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount cannot be negative");
        }

        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
        {
            throw new ArgumentException("Currency codes cannot be null or empty");
        }

        // Same currency conversion
        if (fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        try
        {
            var exchangeRate = await _exchangeRateService.GetExchangeRateAsync(fromCurrency, toCurrency, cancellationToken);
            var convertedAmount = amount * exchangeRate.Rate;

            _logger.LogInformation("Converted {Amount} {FromCurrency} to {ConvertedAmount} {ToCurrency} using rate {Rate}",
                amount, fromCurrency, convertedAmount, toCurrency, exchangeRate.Rate);

            return Math.Round(convertedAmount, 4); // Round to 4 decimal places
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert {Amount} from {FromCurrency} to {ToCurrency}",
                amount, fromCurrency, toCurrency);
            throw;
        }
    }

    public async Task<bool> IsCurrencySupportedAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return false;
        }

        try
        {
            // Try to get rates for this currency - if it works, it's supported
            await _exchangeRateService.GetLatestExchangeRatesAsync(currencyCode, cancellationToken);
            return true;
        }
        catch (UnsupportedCurrencyException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if currency {CurrencyCode} is supported", currencyCode);
            return false;
        }
    }
}
