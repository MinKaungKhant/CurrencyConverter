using CurrencyConverter.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CurrencyConverter.Application.Factories;

public interface ICurrencyProviderFactory
{
    ICurrencyProvider GetProvider(string? providerName = null);
    IEnumerable<string> GetAvailableProviders();
}

public class CurrencyProviderFactory : ICurrencyProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _providers;

    public CurrencyProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _providers = new Dictionary<string, Type>();
    }

    public void RegisterProvider<T>(string name) where T : class, ICurrencyProvider
    {
        _providers[name] = typeof(T);
    }

    public ICurrencyProvider GetProvider(string? providerName = null)
    {
        // Default to first available provider if none specified
        if (string.IsNullOrEmpty(providerName))
        {
            providerName = _providers.Keys.FirstOrDefault();
        }

        if (providerName == null || !_providers.ContainsKey(providerName))
        {
            throw new ArgumentException($"Provider '{providerName}' not found.");
        }

        var providerType = _providers[providerName];
        return (ICurrencyProvider)_serviceProvider.GetService(providerType)!;
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _providers.Keys;
    }
}
