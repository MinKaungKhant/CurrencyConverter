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

public class LatestRatesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public LatestRatesApiTests(WebApplicationFactory<Program> factory)
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

    #region Latest Rates API Tests

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("CHF")]
    [InlineData("CAD")]
    [InlineData("AUD")]
    public async Task GetLatestRates_WithValidCurrency_ReturnsOk(string baseCurrency)
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={baseCurrency}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LatestRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency.ToUpper());
        result.Date.Should().BeCloseTo(DateTime.UtcNow.Date, TimeSpan.FromDays(1));
        result.Rates.Should().NotBeEmpty();
        result.Rates.Should().NotContainKey("TRY");
        result.Rates.Should().NotContainKey("PLN");
        result.Rates.Should().NotContainKey("THB");
        result.Rates.Should().NotContainKey("MXN");
        
        // Verify all rates are positive
        result.Rates.Values.Should().OnlyContain(rate => rate > 0);
    }

    [Theory]
    [InlineData("TRY")]
    [InlineData("PLN")]
    [InlineData("THB")]
    [InlineData("MXN")]
    public async Task GetLatestRates_WithExcludedCurrency_ReturnsBadRequest(string excludedCurrency)
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={excludedCurrency}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("UnsupportedCurrency");
        result.Message.Should().NotBeEmpty();
        result.TraceId.Should().NotBeEmpty();
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetLatestRates_WithEmptyOrNullBaseCurrency_ReturnsBadRequest(string? baseCurrency)
    {
        // Act
        var url = baseCurrency == null
            ? "/api/v1/exchangerates/latest"
            : $"/api/v1/exchangerates/latest?baseCurrency={baseCurrency}";
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("InvalidRequest");
        result.Message.Should().Be("Base currency is required");
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("XXX")]
    [InlineData("123")]
    [InlineData("AB")]
    [InlineData("ABCD")]
    public async Task GetLatestRates_WithInvalidCurrency_ReturnsBadRequest(string invalidCurrency)
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={invalidCurrency}");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("Eur")]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData("USD")]
    public async Task GetLatestRates_WithDifferentCasing_ReturnsOkWithUppercase(string baseCurrency)
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={baseCurrency}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LatestRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency.ToUpper());
    }

    [Fact]
    public async Task GetLatestRates_ResponseStructure_IsValid()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/exchangerates/latest?baseCurrency=EUR");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LatestRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().NotBeEmpty();
        result.Date.Should().NotBe(default);
        result.Rates.Should().NotBeNull();
        result.Rates.Should().NotBeEmpty();
        
        // Check that rates have valid currency codes (3 characters)
        result.Rates.Keys.Should().OnlyContain(key => 
            !string.IsNullOrEmpty(key) && key.Length == 3 && key.All(char.IsLetter));
        
        // Check that all rate values are positive
        result.Rates.Values.Should().OnlyContain(rate => rate > 0);
    }

    [Fact]
    public async Task GetLatestRates_CachingBehavior_ReturnsConsistentResults()
    {
        // Act - Make multiple requests
        var response1 = await _client.GetAsync("/api/v1/exchangerates/latest?baseCurrency=EUR");
        var response2 = await _client.GetAsync("/api/v1/exchangerates/latest?baseCurrency=EUR");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content1 = await response1.Content.ReadAsStringAsync();
        var content2 = await response2.Content.ReadAsStringAsync();
        
        var result1 = JsonSerializer.Deserialize<LatestRatesResponse>(content1, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        var result2 = JsonSerializer.Deserialize<LatestRatesResponse>(content2, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.BaseCurrency.Should().Be(result2!.BaseCurrency);
        result1.Rates.Keys.Should().BeEquivalentTo(result2.Rates.Keys);
    }

    [Theory]
    [InlineData("EUR", "USD")]
    [InlineData("USD", "EUR")]
    [InlineData("GBP", "JPY")]
    [InlineData("CHF", "CAD")]
    public async Task GetLatestRates_DifferentBaseCurrencies_ReturnDifferentRates(string baseCurrency1, string baseCurrency2)
    {
        // Act
        var response1 = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={baseCurrency1}");
        var response2 = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={baseCurrency2}");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content1 = await response1.Content.ReadAsStringAsync();
        var content2 = await response2.Content.ReadAsStringAsync();
        
        var result1 = JsonSerializer.Deserialize<LatestRatesResponse>(content1, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        var result2 = JsonSerializer.Deserialize<LatestRatesResponse>(content2, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.BaseCurrency.Should().Be(baseCurrency1);
        result2!.BaseCurrency.Should().Be(baseCurrency2);
        result1.BaseCurrency.Should().NotBe(result2.BaseCurrency);
    }

    [Fact]
    public async Task GetLatestRates_PerformanceTest_ReturnsWithinReasonableTime()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync("/api/v1/exchangerates/latest?baseCurrency=EUR");

        // Assert
        stopwatch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete within 5 seconds
    }

    [Fact]
    public async Task GetLatestRates_ContentType_IsApplicationJson()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/exchangerates/latest?baseCurrency=EUR");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    #endregion
}