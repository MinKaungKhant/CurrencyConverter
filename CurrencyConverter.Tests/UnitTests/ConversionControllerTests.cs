using Xunit;
using Moq;
using FluentAssertions;
using CurrencyConverter.API.Controllers;
using CurrencyConverter.API.DTOs;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace CurrencyConverter.Tests.UnitTests;

public class ConversionControllerTests
{
    private readonly Mock<ICurrencyConversionService> _mockConversionService;
    private readonly Mock<ILogger<ConversionController>> _mockLogger;
    private readonly ConversionController _controller;

    public ConversionControllerTests()
    {
        _mockConversionService = new Mock<ICurrencyConversionService>();
        _mockLogger = new Mock<ILogger<ConversionController>>();
        _controller = new ConversionController(_mockConversionService.Object, _mockLogger.Object);
        
        // Setup HttpContext
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "test-trace-id"
            }
        };
    }

    #region ConvertCurrency Tests

    [Fact]
    public async Task ConvertCurrency_WithValidRequest_ReturnsOkResult()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        var expectedConvertedAmount = 112.34m;
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            request.Amount, 
            request.FromCurrency.ToUpper(), 
            request.ToCurrency.ToUpper(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConvertedAmount);

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConversionResponse>().Subject;
        
        response.OriginalAmount.Should().Be(request.Amount);
        response.ConvertedAmount.Should().Be(expectedConvertedAmount);
        response.FromCurrency.Should().Be(request.FromCurrency.ToUpper());
        response.ToCurrency.Should().Be(request.ToCurrency.ToUpper());
        response.ExchangeRate.Should().Be(expectedConvertedAmount / request.Amount);
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("USD", "EUR", 50.25, 44.72)]
    [InlineData("GBP", "JPY", 1000, 150000)]
    [InlineData("CHF", "CAD", 250.75, 340.50)]
    public async Task ConvertCurrency_WithDifferentValidCurrencies_ReturnsCorrectConversion(
        string fromCurrency, string toCurrency, decimal amount, decimal expectedConverted)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = amount,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            amount, 
            fromCurrency.ToUpper(), 
            toCurrency.ToUpper(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConverted);

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConversionResponse>().Subject;
        
        response.OriginalAmount.Should().Be(amount);
        response.ConvertedAmount.Should().Be(expectedConverted);
        response.FromCurrency.Should().Be(fromCurrency.ToUpper());
        response.ToCurrency.Should().Be(toCurrency.ToUpper());
    }

    [Fact]
    public async Task ConvertCurrency_WithUnsupportedCurrencyException_ReturnsBadRequest()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "TRY"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnsupportedCurrencyException("TRY"));

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var errorResponse = badRequestResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        
        errorResponse.Error.Should().Be("UnsupportedCurrency");
        errorResponse.Message.Should().NotBeEmpty();
        errorResponse.TraceId.Should().Be("test-trace-id");
    }

    [Fact]
    public async Task ConvertCurrency_WithExchangeRateNotFoundException_ReturnsNotFound()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "XYZ"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExchangeRateNotFoundException("EUR", "XYZ"));

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var errorResponse = notFoundResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        
        errorResponse.Error.Should().Be("ExchangeRateNotFound");
        errorResponse.Message.Should().NotBeEmpty();
        errorResponse.TraceId.Should().Be("test-trace-id");
    }

    [Fact]
    public async Task ConvertCurrency_WithExternalApiException_ReturnsServiceUnavailable()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalApiException("External API is down"));

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var statusCodeResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(503);
        
        var errorResponse = statusCodeResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        errorResponse.Error.Should().Be("ServiceUnavailable");
        errorResponse.Message.Should().Be("Exchange rate service is temporarily unavailable");
        errorResponse.TraceId.Should().Be("test-trace-id");
    }

    [Fact]
    public async Task ConvertCurrency_WithUnexpectedException_ReturnsInternalServerError()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var statusCodeResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        
        var errorResponse = statusCodeResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        errorResponse.Error.Should().Be("InternalServerError");
        errorResponse.Message.Should().Be("An unexpected error occurred");
        errorResponse.TraceId.Should().Be("test-trace-id");
    }

    [Fact]
    public async Task ConvertCurrency_WithInvalidModelState_ReturnsBadRequest()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 0, // Invalid amount
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        _controller.ModelState.AddModelError("Amount", "Amount must be greater than 0");

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var errorResponse = badRequestResult.Value.Should().BeOfType<ErrorResponse>().Subject;
        
        errorResponse.Error.Should().Be("InvalidRequest");
        errorResponse.Message.Should().Be("Invalid request data");
        errorResponse.TraceId.Should().Be("test-trace-id");
        
        _mockConversionService.Verify(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConvertCurrency_WithZeroAmount_CalculatesZeroExchangeRate()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 0.01m, // Very small amount to test division
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        var expectedConvertedAmount = 0.011m;
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            request.Amount, 
            request.FromCurrency.ToUpper(), 
            request.ToCurrency.ToUpper(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConvertedAmount);

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConversionResponse>().Subject;
        
        response.ExchangeRate.Should().Be(expectedConvertedAmount / request.Amount);
    }

    [Theory]
    [InlineData("eur", "usd")]
    [InlineData("Eur", "Usd")]
    [InlineData("EUR", "USD")]
    public async Task ConvertCurrency_WithDifferentCasing_ConvertsToUppercase(string fromCurrency, string toCurrency)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };
        
        var expectedConvertedAmount = 112.34m;
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            request.Amount, 
            fromCurrency.ToUpper(), 
            toCurrency.ToUpper(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConvertedAmount);

        // Act
        var result = await _controller.ConvertCurrency(request);

        // Assert
        result.Should().NotBeNull();
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ConversionResponse>().Subject;
        
        response.FromCurrency.Should().Be(fromCurrency.ToUpper());
        response.ToCurrency.Should().Be(toCurrency.ToUpper());
        
        _mockConversionService.Verify(x => x.ConvertCurrencyAsync(
            request.Amount, 
            fromCurrency.ToUpper(), 
            toCurrency.ToUpper(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertCurrency_LogsWarningOnUnsupportedCurrency()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "TRY"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnsupportedCurrencyException("TRY"));

        // Act
        await _controller.ConvertCurrency(request);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unsupported currency")),
                It.IsAny<UnsupportedCurrencyException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ConvertCurrency_LogsErrorOnExternalApiException()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };
        
        _mockConversionService.Setup(x => x.ConvertCurrencyAsync(
            It.IsAny<decimal>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalApiException("External API is down"));

        // Act
        await _controller.ConvertCurrency(request);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("External API error")),
                It.IsAny<ExternalApiException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}