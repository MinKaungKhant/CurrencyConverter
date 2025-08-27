using Xunit;
using Moq;
using FluentAssertions;
using CurrencyConverter.Application.Services;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Entities;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CurrencyConverter.Tests.UnitTests;

public class CurrencyConversionServiceTests
{
    private readonly Mock<IExchangeRateService> _mockExchangeRateService;
    private readonly Mock<ILogger<CurrencyConversionService>> _mockLogger;
    private readonly CurrencyConversionService _service;

    public CurrencyConversionServiceTests()
    {
        _mockExchangeRateService = new Mock<IExchangeRateService>();
        _mockLogger = new Mock<ILogger<CurrencyConversionService>>();
        _service = new CurrencyConversionService(_mockExchangeRateService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ConvertCurrencyAsync_SameCurrency_ReturnsOriginalAmount()
    {
        // Arrange
        var amount = 100m;
        var currency = "USD";

        // Act
        var result = await _service.ConvertCurrencyAsync(amount, currency, currency);

        // Assert
        result.Should().Be(amount);
    }

    [Fact]
    public async Task ConvertCurrencyAsync_ValidConversion_ReturnsConvertedAmount()
    {
        // Arrange
        var amount = 100m;
        var fromCurrency = "USD";
        var toCurrency = "EUR";
        var exchangeRate = new ExchangeRate
        {
            BaseCurrency = fromCurrency,
            TargetCurrency = toCurrency,
            Rate = 0.85m,
            Date = DateTime.UtcNow.Date,
            LastUpdated = DateTime.UtcNow
        };

        _mockExchangeRateService
            .Setup(x => x.GetExchangeRateAsync(fromCurrency, toCurrency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchangeRate);

        // Act
        var result = await _service.ConvertCurrencyAsync(amount, fromCurrency, toCurrency);

        // Assert
        result.Should().Be(85m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task ConvertCurrencyAsync_NegativeAmount_ThrowsArgumentException(decimal amount)
    {
        // Arrange
        var fromCurrency = "USD";
        var toCurrency = "EUR";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ConvertCurrencyAsync(amount, fromCurrency, toCurrency));
    }

    [Theory]
    [InlineData("", "EUR")]
    [InlineData("USD", "")]
    [InlineData(null, "EUR")]
    [InlineData("USD", null)]
    public async Task ConvertCurrencyAsync_InvalidCurrencies_ThrowsArgumentException(string fromCurrency, string toCurrency)
    {
        // Arrange
        var amount = 100m;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.ConvertCurrencyAsync(amount, fromCurrency, toCurrency));
    }

    [Fact]
    public async Task IsCurrencySupportedAsync_SupportedCurrency_ReturnsTrue()
    {
        // Arrange
        var currency = "USD";
        _mockExchangeRateService
            .Setup(x => x.GetLatestExchangeRatesAsync(currency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { { "EUR", 0.85m } });

        // Act
        var result = await _service.IsCurrencySupportedAsync(currency);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsCurrencySupportedAsync_UnsupportedCurrency_ReturnsFalse()
    {
        // Arrange
        var currency = "TRY";
        _mockExchangeRateService
            .Setup(x => x.GetLatestExchangeRatesAsync(currency, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnsupportedCurrencyException(currency));

        // Act
        var result = await _service.IsCurrencySupportedAsync(currency);

        // Assert
        result.Should().BeFalse();
    }
}
