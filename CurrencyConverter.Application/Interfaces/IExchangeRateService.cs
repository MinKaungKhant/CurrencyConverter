using CurrencyConverter.Domain.Entities;

namespace CurrencyConverter.Application.Interfaces;

public interface IExchangeRateService
{
    Task<Dictionary<string, decimal>> GetLatestExchangeRatesAsync(string baseCurrency, CancellationToken cancellationToken = default);
    Task<ExchangeRate> GetExchangeRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);
    Task<IEnumerable<ExchangeRate>> GetHistoricalExchangeRatesAsync(string baseCurrency, DateTime startDate, DateTime endDate, int page, int pageSize, CancellationToken cancellationToken = default);
}
