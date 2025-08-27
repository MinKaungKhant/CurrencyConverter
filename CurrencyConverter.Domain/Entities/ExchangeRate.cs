namespace CurrencyConverter.Domain.Entities;

public class ExchangeRate
{
    public string BaseCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime Date { get; set; }
    public DateTime LastUpdated { get; set; }
}
