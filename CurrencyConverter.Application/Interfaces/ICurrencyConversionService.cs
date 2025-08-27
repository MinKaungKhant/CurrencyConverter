namespace CurrencyConverter.Application.Interfaces;

public interface ICurrencyConversionService
{
    Task<decimal> ConvertCurrencyAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
    Task<bool> IsCurrencySupportedAsync(string currencyCode, CancellationToken cancellationToken = default);
}
