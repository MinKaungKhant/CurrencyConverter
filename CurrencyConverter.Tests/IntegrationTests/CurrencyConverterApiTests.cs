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

namespace CurrencyConverter.Tests.IntegrationTests;

// Shared test authentication components
public class TestAuthenticationSchemeOptions : AuthenticationSchemeOptions { }

public class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(IOptionsMonitor<TestAuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.NameIdentifier, "123"),
            new Claim("client_id", "test-client"),
            new Claim(ClaimTypes.Role, "ApiUser")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class CurrencyConverterApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CurrencyConverterApiTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task GetLatestRates_WithValidCurrency_ReturnsOk()
    {
        // Arrange
        var baseCurrency = "EUR";

        // Act
        var response = await _client.GetAsync($"/api/v1/exchangerates/latest?baseCurrency={baseCurrency}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LatestRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency);
        result.Rates.Should().NotBeEmpty();
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
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConvertCurrency_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100,
            FromCurrency = "EUR",
            ToCurrency = "USD"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ConversionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.OriginalAmount.Should().Be(100);
        result.FromCurrency.Should().Be("EUR");
        result.ToCurrency.Should().Be("USD");
        result.ConvertedAmount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConvertCurrency_WithExcludedCurrency_ReturnsBadRequest()
    {
        // Arrange
        var request = new ConversionRequest
        {
            Amount = 100,
            FromCurrency = "EUR",
            ToCurrency = "TRY" // Excluded currency
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/conversion/convert", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRangeRates_WithValidParameters_ReturnsOk()
    {
        // Arrange
        var baseCurrency = "EUR";
        var startDate = "2025-01-01";
        var endDate = "2025-01-31";

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={baseCurrency}&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<RangeRatesResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.BaseCurrency.Should().Be(baseCurrency);
        result.StartDate.Should().Be(DateTime.Parse(startDate));
        result.EndDate.Should().Be(DateTime.Parse(endDate));
        result.Rates.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetRangeRates_WithExcludedCurrency_ReturnsBadRequest()
    {
        // Arrange
        var excludedCurrency = "TRY";
        var startDate = "2025-01-01";
        var endDate = "2025-01-31";

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/exchangerates/range?baseCurrency={excludedCurrency}&startDate={startDate}&endDate={endDate}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
