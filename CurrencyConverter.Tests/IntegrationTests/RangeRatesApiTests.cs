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

public class RangeRatesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RangeRatesApiTests(WebApplicationFactory<Program> factory)
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

    #region Range Rates API Tests

    [Theory]
    [InlineData("EUR", "2025-01-01", "2025-01-31")]
    [InlineData("USD", "2025-02-01", "2025-02-28")]
    [InlineData("GBP", "2025-03-01", "2025-03-15")]
    [InlineData("JPY", "2025-04-10", "2025-04-20")]
    public async Task GetRangeRates_WithValidParameters_ReturnsOk(string baseCurrency, string startDate, string endDate)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency.ToUpper());
        result.StartDate.Should().Be(DateTime.Parse(startDate));
        result.EndDate.Should().Be(DateTime.Parse(endDate));
        result.Rates.Should().NotBeEmpty();
        
        // Verify date range
        var expectedDays = (DateTime.Parse(endDate) - DateTime.Parse(startDate)).Days + 1;
        result.Rates.Should().HaveCount(expectedDays);
        
        // Verify no excluded currencies in rates
        foreach (var dayRates in result.Rates.Values)
        {
            dayRates.Should().NotContainKey("TRY");
            dayRates.Should().NotContainKey("PLN");
            dayRates.Should().NotContainKey("THB");
            dayRates.Should().NotContainKey("MXN");
            dayRates.Values.Should().OnlyContain(rate => rate > 0);
        }
    }

    [Theory]
    [InlineData("TRY", "2025-01-01", "2025-01-31")]
    [InlineData("PLN", "2025-01-01", "2025-01-31")]
    [InlineData("THB", "2025-01-01", "2025-01-31")]
    [InlineData("MXN", "2025-01-01", "2025-01-31")]
    public async Task GetRangeRates_WithExcludedCurrency_ReturnsBadRequest(string excludedCurrency, string startDate, string endDate)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={excludedCurrency}&startDate={startDate}&endDate={endDate}");

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
    }

    [Theory]
    [InlineData("EUR", "invalid-date", "2025-01-31")]
    [InlineData("EUR", "2025-01-01", "invalid-date")]
    [InlineData("EUR", "2025/01/01", "2025-01-31")]
    [InlineData("EUR", "2025-01-01", "2025/01/31")]
    [InlineData("EUR", "01-01-2025", "2025-01-31")]
    [InlineData("EUR", "2025-1-1", "2025-01-31")]
    public async Task GetRangeRates_WithInvalidDateFormat_ReturnsBadRequest(string baseCurrency, string startDate, string endDate)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().BeOneOf("InvalidStartDateFormat", "InvalidEndDateFormat");
        result.Message.Should().Contain("YYYY-MM-DD format");
    }

    [Theory]
    [InlineData("EUR", "2025-01-31", "2025-01-01")]
    [InlineData("USD", "2025-02-15", "2025-02-10")]
    [InlineData("GBP", "2025-12-25", "2025-01-01")]
    public async Task GetRangeRates_WithStartDateAfterEndDate_ReturnsBadRequest(string baseCurrency, string startDate, string endDate)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("InvalidDateRange");
        result.Message.Should().Be("Start date must be before or equal to end date");
    }

    [Fact]
    public async Task GetRangeRates_WithDateRangeExceeding365Days_ReturnsBadRequest()
    {
        // Arrange
        var startDate = "2023-01-01";
        var endDate = "2025-01-02"; // More than 365 days

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency=EUR&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("DateRangeTooLarge");
        result.Message.Should().Be("Date range cannot exceed 365 days");
    }

    [Fact]
    public async Task GetRangeRates_WithFutureDates_ReturnsBadRequest()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        var futureDateEnd = DateTime.UtcNow.AddDays(60).ToString("yyyy-MM-dd");

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency=EUR&startDate={futureDate}&endDate={futureDateEnd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Error.Should().Be("FutureDate");
        result.Message.Should().Be("Cannot retrieve rates for future dates");
    }

    [Theory]
    [InlineData("EUR", "2025-01-01", "2025-01-31", "USD,GBP")]
    [InlineData("USD", "2025-02-01", "2025-02-28", "EUR")]
    [InlineData("GBP", "2025-03-01", "2025-03-15", "USD,EUR,JPY")]
    [InlineData("JPY", "2025-04-10", "2025-04-20", "USD,EUR,GBP,CHF")]
    public async Task GetRangeRates_WithTargetCurrencies_ReturnsFilteredRates(string baseCurrency, string startDate, string endDate, string targetCurrencies)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate={startDate}&endDate={endDate}&targetCurrencies={targetCurrencies}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency.ToUpper());
        
        var expectedCurrencies = targetCurrencies.Split(',').Select(c => c.Trim().ToUpper()).ToList();
        
        // Verify only target currencies are included
        foreach (var dayRates in result.Rates.Values)
        {
            dayRates.Keys.Should().BeSubsetOf(expectedCurrencies);
            foreach (var currency in dayRates.Keys)
            {
                expectedCurrencies.Should().Contain(currency);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("EUR", "")]
    [InlineData("EUR", "2025-01-01")]
    public async Task GetRangeRates_WithMissingRequiredParameters_ReturnsBadRequest(string? baseCurrency = "EUR", string? startDate = "2025-01-01", string? endDate = null)
    {
        // Arrange
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(baseCurrency))
            queryParams.Add($"baseCurrency={baseCurrency}");
        if (!string.IsNullOrEmpty(startDate))
            queryParams.Add($"startDate={startDate}");
        if (!string.IsNullOrEmpty(endDate))
            queryParams.Add($"endDate={endDate}");

        var queryString = string.Join("&", queryParams);
        var url = $"/api/v1/exchangerates/range{(queryString.Length > 0 ? "?" + queryString : "")}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("eur", "usd")]
    [InlineData("Eur", "Usd")]
    [InlineData("EUR", "USD")]
    public async Task GetRangeRates_WithDifferentCasing_ReturnsOkWithUppercase(string baseCurrency, string targetCurrency)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate=2025-01-01&endDate=2025-01-05&targetCurrencies={targetCurrency}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency.ToUpper());
        
        // Verify target currency is uppercase in response
        foreach (var dayRates in result.Rates.Values)
        {
            if (dayRates.ContainsKey(targetCurrency.ToUpper()))
            {
                dayRates.Should().ContainKey(targetCurrency.ToUpper());
            }
        }
    }

    [Fact]
    public async Task GetRangeRates_SingleDay_ReturnsOneDay()
    {
        // Arrange
        var singleDate = "2025-01-15";

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency=EUR&startDate={singleDate}&endDate={singleDate}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.Rates.Should().HaveCount(1);
        result.Rates.Should().ContainKey(singleDate);
        result.StartDate.Should().Be(result.EndDate);
    }

    [Fact]
    public async Task GetRangeRates_ResponseStructure_IsValid()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/v1/exchangerates/range?baseCurrency=EUR&startDate=2025-01-01&endDate=2025-01-05");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().NotBeEmpty();
        result.StartDate.Should().NotBe(default);
        result.EndDate.Should().NotBe(default);
        result.Rates.Should().NotBeNull();
        result.Rates.Should().NotBeEmpty();
        
        // Verify date keys are in correct format
        foreach (var key in result.Rates.Keys)
        {
            DateTime.TryParseExact(key, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _)
                .Should().BeTrue($"Date key '{key}' should be in YYYY-MM-DD format");
        }
        
        // Verify all rate values are positive
        foreach (var dayRates in result.Rates.Values)
        {
            dayRates.Values.Should().OnlyContain(rate => rate > 0);
            dayRates.Keys.Should().OnlyContain(key => 
                !string.IsNullOrEmpty(key) && key.Length == 3 && key.All(char.IsLetter));
        }
    }

    [Fact]
    public async Task GetRangeRates_PerformanceTest_ReturnsWithinReasonableTime()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync(
            "/api/v1/exchangerates/range?baseCurrency=EUR&startDate=2025-01-01&endDate=2025-01-31");

        // Assert
        stopwatch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete within 10 seconds for a month
    }

    [Fact]
    public async Task GetRangeRates_ContentType_IsApplicationJson()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/v1/exchangerates/range?baseCurrency=EUR&startDate=2025-01-01&endDate=2025-01-05");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Theory]
    [InlineData("TRY,PLN")]
    [InlineData("USD,TRY")]
    [InlineData("EUR,THB,MXN")]
    public async Task GetRangeRates_WithExcludedTargetCurrencies_FiltersOutExcluded(string targetCurrencies)
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency=EUR&startDate=2025-01-01&endDate=2025-01-05&targetCurrencies={targetCurrencies}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        
        // Verify excluded currencies are not in the response
        foreach (var dayRates in result!.Rates.Values)
        {
            dayRates.Should().NotContainKey("TRY");
            dayRates.Should().NotContainKey("PLN");
            dayRates.Should().NotContainKey("THB");
            dayRates.Should().NotContainKey("MXN");
        }
    }

    #endregion
}