using CurrencyConverter.Domain.Entities;

namespace CurrencyConverter.Application.Interfaces;

public interface ICurrencyProvider
{
    Task<Dictionary<string, decimal>> GetLatestRatesAsync(string baseCurrency, CancellationToken cancellationToken = default);
    Task<IEnumerable<ExchangeRate>> GetHistoricalRatesAsync(string baseCurrency, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<Currency>> GetSupportedCurrenciesAsync(CancellationToken cancellationToken = default);
    string ProviderName { get; }
}
