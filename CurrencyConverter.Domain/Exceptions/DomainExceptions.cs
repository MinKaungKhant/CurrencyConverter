namespace CurrencyConverter.Domain.Exceptions;

public class UnsupportedCurrencyException : Exception
{
    public UnsupportedCurrencyException(string currency)
        : base($"Currency '{currency}' is not supported.")
    {
    }

    public UnsupportedCurrencyException(string currency, Exception innerException)
        : base($"Currency '{currency}' is not supported.", innerException)
    {
    }
}

public class ExchangeRateNotFoundException : Exception
{
    public ExchangeRateNotFoundException(string baseCurrency, string targetCurrency)
        : base($"Exchange rate from '{baseCurrency}' to '{targetCurrency}' not found.")
    {
    }

    public ExchangeRateNotFoundException(string baseCurrency, string targetCurrency, Exception innerException)
        : base($"Exchange rate from '{baseCurrency}' to '{targetCurrency}' not found.", innerException)
    {
    }
}

public class ExternalApiException : Exception
{
    public ExternalApiException(string message) : base(message)
    {
    }

    public ExternalApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
