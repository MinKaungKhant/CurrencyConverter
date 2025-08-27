using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using Xunit;
using FluentAssertions;
using System.Text.Json;
using CurrencyConverter.API.DTOs;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Security.Claims;
using System.Net;

namespace CurrencyConverter.Tests.IntegrationTests;

public class ConversionApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ConversionApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override authentication for testing
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>(
                        "Test", options => { });
                
                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .AddAuthenticationSchemes("Test")
                        .Build();
                });
            });
        });
        
        _client = _factory.CreateClient();
    }

    #region Convert API Tests

    [Fact]
    public async Task ConvertCurrency_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OriginalAmount.Should().Be(100m);
        result.FromCurrency.Should().Be("EUR");
        result.ToCurrency.Should().Be("USD");
        result.ConvertedAmount.Should().BeGreaterThan(0);
        result.ExchangeRate.Should().BeGreaterThan(0);
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("USD", "EUR", 50.25)]
    [InlineData("GBP", "USD", 1000)]
    [InlineData("JPY", "EUR", 10000)]
    [InlineData("CHF", "GBP", 250.75)]
    public async Task ConvertCurrency_WithDifferentValidCurrencies_ReturnsOk(string fromCurrency, string toCurrency, decimal amount)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = amount,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OriginalAmount.Should().Be(amount);
        result.FromCurrency.Should().Be(fromCurrency.ToUpper());
        result.ToCurrency.Should().Be(toCurrency.ToUpper());
        result.ConvertedAmount.Should().BeGreaterThan(0);
        result.ExchangeRate.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("EUR", "TRY")]
    [InlineData("USD", "PLN")]
    [InlineData("GBP", "THB")]
    [InlineData("CHF", "MXN")]
    [InlineData("TRY", "USD")]
    [InlineData("PLN", "EUR")]
    [InlineData("THB", "GBP")]
    [InlineData("MXN", "CHF")]
    public async Task ConvertCurrency_WithExcludedCurrencies_ReturnsBadRequest(string fromCurrency, string toCurrency)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("UnsupportedCurrency");
        result.Message.Should().NotBeEmpty();
        result.TraceId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public async Task ConvertCurrency_WithInvalidAmount_ReturnsBadRequest(decimal amount)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = amount,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", "USD")]
    [InlineData("EUR", "")]
    [InlineData("E", "USD")]
    [InlineData("EUR", "U")]
    [InlineData("EURO", "USD")]
    [InlineData("EUR", "DOLLAR")]
    public async Task ConvertCurrency_WithInvalidCurrencyFormat_ReturnsBadRequest(string fromCurrency, string toCurrency)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConvertCurrency_WithMissingRequestBody_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConvertCurrency_WithInvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConvertCurrency_SameCurrency_ReturnsOriginalAmount()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = "EUR",
            ToCurrency = "EUR"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OriginalAmount.Should().Be(100m);
        result.ConvertedAmount.Should().Be(100m);
        result.FromCurrency.Should().Be("EUR");
        result.ToCurrency.Should().Be("EUR");
        result.ExchangeRate.Should().Be(1m);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.99)]
    [InlineData(999999.99)]
    public async Task ConvertCurrency_WithEdgeCaseAmounts_ReturnsOk(decimal amount)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = amount,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OriginalAmount.Should().Be(amount);
        result.ConvertedAmount.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("eur", "usd")]
    [InlineData("Eur", "Usd")]
    [InlineData("EUR", "USD")]
    public async Task ConvertCurrency_WithDifferentCasing_ReturnsOkWithUppercase(string fromCurrency, string toCurrency)
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100m,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.FromCurrency.Should().Be("EUR");
        result.ToCurrency.Should().Be("USD");
    }

    #endregion
}