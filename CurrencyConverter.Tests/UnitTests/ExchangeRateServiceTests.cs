using Xunit;
using Moq;
using FluentAssertions;
using CurrencyConverter.Application.Services;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Entities;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CurrencyConverter.Tests.UnitTests;

public class ExchangeRateServiceTests
{
    private readonly Mock<ICurrencyProvider> _mockCurrencyProvider;
    private readonly Mock<ICacheService> _mockCacheService;
    private readonly Mock<ILogger<ExchangeRateService>> _mockLogger;
    private readonly ExchangeRateService _service;

    public ExchangeRateServiceTests()
    {
        _mockCurrencyProvider = new Mock<ICurrencyProvider>();
        _mockCacheService = new Mock<ICacheService>();
        _mockLogger = new Mock<ILogger<ExchangeRateService>>();
        _service = new ExchangeRateService(_mockCurrencyProvider.Object, _mockCacheService.Object, _mockLogger.Object);
    }

    #region GetLatestExchangeRatesAsync Tests

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    public async Task GetLatestExchangeRatesAsync_WithValidCurrency_ReturnsRatesFromProvider(string baseCurrency)
    {
        // Arrange
        var expectedRates = new Dictionary<string, decimal>
        {
            { "USD", 1.1234m },
            { "GBP", 0.8567m },
            { "JPY", 123.45m },
            { "TRY", 15.67m }, // This should be filtered out
            { "PLN", 4.32m }   // This should be filtered out
        };

        _mockCacheService.Setup(x => x.GetAsync<Dictionary<string, decimal>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, decimal>?)null);

        _mockCurrencyProvider.Setup(x => x.GetLatestRatesAsync(baseCurrency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRates);

        _mockCacheService.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, decimal>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetLatestExchangeRatesAsync(baseCurrency);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotContainKey("TRY");
        result.Should().NotContainKey("PLN");
        result.Should().NotContainKey("THB");
        result.Should().NotContainKey("MXN");
        result.Should().ContainKey("USD");
        result.Should().ContainKey("GBP");
        result.Should().ContainKey("JPY");
        
        _mockCurrencyProvider.Verify(x => x.GetLatestRatesAsync(baseCurrency, It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheService.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, decimal>>(), TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("TRY")]
    [InlineData("PLN")]
    [InlineData("THB")]
    [InlineData("MXN")]
    public async Task GetLatestExchangeRatesAsync_WithExcludedCurrency_ThrowsUnsupportedCurrencyException(string excludedCurrency)
    {
        // Act & Assert
        await Assert.ThrowsAsync<UnsupportedCurrencyException>(() => 
            _service.GetLatestExchangeRatesAsync(excludedCurrency));
        
        _mockCurrencyProvider.Verify(x => x.GetLatestRatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetLatestExchangeRatesAsync_WithInvalidCurrency_ThrowsArgumentException(string? invalidCurrency)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GetLatestExchangeRatesAsync(invalidCurrency!));
    }

    [Fact]
    public async Task GetLatestExchangeRatesAsync_WithCachedData_ReturnsCachedRates()
    {
        // Arrange
        var baseCurrency = "EUR";
        var cachedRates = new Dictionary<string, decimal>
        {
            { "USD", 1.1234m },
            { "GBP", 0.8567m }
        };

        _mockCacheService.Setup(x => x.GetAsync<Dictionary<string, decimal>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedRates);

        // Act
        var result = await _service.GetLatestExchangeRatesAsync(baseCurrency);

        // Assert
        result.Should().BeEquivalentTo(cachedRates);
        _mockCurrencyProvider.Verify(x => x.GetLatestRatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetLatestExchangeRatesAsync_ProviderThrowsException_ThrowsExternalApiException()
    {
        // Arrange
        var baseCurrency = "EUR";
        
        _mockCacheService.Setup(x => x.GetAsync<Dictionary<string, decimal>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, decimal>?)null);

        _mockCurrencyProvider.Setup(x => x.GetLatestRatesAsync(baseCurrency, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsAsync<ExternalApiException>(() => 
            _service.GetLatestExchangeRatesAsync(baseCurrency));
    }

    #endregion

    #region GetExchangeRateAsync Tests

    [Fact]
    public async Task GetExchangeRateAsync_WithValidCurrencies_ReturnsExchangeRate()
    {
        // Arrange
        var baseCurrency = "EUR";
        var targetCurrency = "USD";
        var expectedRate = 1.1234m;
        
        var rates = new Dictionary<string, decimal>
        {
            { targetCurrency, expectedRate },
            { "GBP", 0.8567m }
        };

        _mockCacheService.Setup(x => x.GetAsync<Dictionary<string, decimal>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, decimal>?)null);

        _mockCurrencyProvider.Setup(x => x.GetLatestRatesAsync(baseCurrency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        _mockCacheService.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, decimal>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetExchangeRateAsync(baseCurrency, targetCurrency);

        // Assert
        result.Should().NotBeNull();
        result.BaseCurrency.Should().Be(baseCurrency);
        result.TargetCurrency.Should().Be(targetCurrency);
        result.Rate.Should().Be(expectedRate);
        result.Date.Should().Be(DateTime.UtcNow.Date);
    }

    [Fact]
    public async Task GetExchangeRateAsync_WithNonExistentTargetCurrency_ThrowsExchangeRateNotFoundException()
    {
        // Arrange
        var baseCurrency = "EUR";
        var targetCurrency = "XYZ";
        
        var rates = new Dictionary<string, decimal>
        {
            { "USD", 1.1234m },
            { "GBP", 0.8567m }
        };

        _mockCacheService.Setup(x => x.GetAsync<Dictionary<string, decimal>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, decimal>?)null);

        _mockCurrencyProvider.Setup(x => x.GetLatestRatesAsync(baseCurrency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        // Act & Assert
        await Assert.ThrowsAsync<ExchangeRateNotFoundException>(() => 
            _service.GetExchangeRateAsync(baseCurrency, targetCurrency));
    }

    [Theory]
    [InlineData("EUR", "TRY")]
    [InlineData("TRY", "USD")]
    [InlineData("PLN", "EUR")]
    [InlineData("USD", "THB")]
    public async Task GetExchangeRateAsync_WithExcludedCurrencies_ThrowsUnsupportedCurrencyException(string baseCurrency, string targetCurrency)
    {
        // Act & Assert
        await Assert.ThrowsAsync<UnsupportedCurrencyException>(() => 
            _service.GetExchangeRateAsync(baseCurrency, targetCurrency));
    }

    #endregion

    #region GetHistoricalExchangeRatesAsync Tests

    [Fact]
    public async Task GetHistoricalExchangeRatesAsync_WithValidParameters_ReturnsPagedResults()
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 5);
        var page = 1;
        var pageSize = 10;

        var historicalRates = new List<ExchangeRate>
        {
            new() { BaseCurrency = baseCurrency, TargetCurrency = "USD", Rate = 1.1m, Date = startDate },
            new() { BaseCurrency = baseCurrency, TargetCurrency = "GBP", Rate = 0.85m, Date = startDate },
            new() { BaseCurrency = baseCurrency, TargetCurrency = "USD", Rate = 1.12m, Date = startDate.AddDays(1) },
            new() { BaseCurrency = baseCurrency, TargetCurrency = "TRY", Rate = 15.67m, Date = startDate }, // Should be filtered
            new() { BaseCurrency = baseCurrency, TargetCurrency = "PLN", Rate = 4.32m, Date = startDate }   // Should be filtered
        };

        _mockCacheService.Setup(x => x.GetAsync<List<ExchangeRate>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ExchangeRate>?)null);

        _mockCurrencyProvider.Setup(x => x.GetHistoricalRatesAsync(baseCurrency, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(historicalRates);

        _mockCacheService.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<List<ExchangeRate>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotContain(r => r.TargetCurrency == "TRY" || r.TargetCurrency == "PLN" || r.TargetCurrency == "THB" || r.TargetCurrency == "MXN");
        result.Should().HaveCount(c => c <= pageSize);
        
        _mockCurrencyProvider.Verify(x => x.GetHistoricalRatesAsync(baseCurrency, startDate, endDate, It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheService.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<List<ExchangeRate>>(), TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetHistoricalExchangeRatesAsync_WithStartDateAfterEndDate_ThrowsArgumentException()
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = new DateTime(2024, 1, 10);
        var endDate = new DateTime(2024, 1, 5);
        var page = 1;
        var pageSize = 10;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 10)]
    [InlineData(1, -5)]
    public async Task GetHistoricalExchangeRatesAsync_WithInvalidPaginationParameters_ThrowsArgumentException(int page, int pageSize)
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 5);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize));
    }

    [Fact]
    public async Task GetHistoricalExchangeRatesAsync_WithExcludedBaseCurrency_ThrowsUnsupportedCurrencyException()
    {
        // Arrange
        var baseCurrency = "TRY";
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 5);
        var page = 1;
        var pageSize = 10;

        // Act & Assert
        await Assert.ThrowsAsync<UnsupportedCurrencyException>(() => 
            _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize));
    }

    [Fact]
    public async Task GetHistoricalExchangeRatesAsync_WithCachedData_ReturnsCachedResults()
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 5);
        var page = 1;
        var pageSize = 10;

        var cachedRates = new List<ExchangeRate>
        {
            new() { BaseCurrency = baseCurrency, TargetCurrency = "USD", Rate = 1.1m, Date = startDate },
            new() { BaseCurrency = baseCurrency, TargetCurrency = "GBP", Rate = 0.85m, Date = startDate }
        };

        _mockCacheService.Setup(x => x.GetAsync<List<ExchangeRate>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedRates);

        // Act
        var result = await _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize);

        // Assert
        result.Should().BeEquivalentTo(cachedRates.Take(pageSize));
        _mockCurrencyProvider.Verify(x => x.GetHistoricalRatesAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetHistoricalExchangeRatesAsync_ProviderThrowsException_ThrowsExternalApiException()
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 5);
        var page = 1;
        var pageSize = 10;

        _mockCacheService.Setup(x => x.GetAsync<List<ExchangeRate>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ExchangeRate>?)null);

        _mockCurrencyProvider.Setup(x => x.GetHistoricalRatesAsync(baseCurrency, startDate, endDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsAsync<ExternalApiException>(() => 
            _service.GetHistoricalExchangeRatesAsync(baseCurrency, startDate, endDate, page, pageSize));
    }

    #endregion
}