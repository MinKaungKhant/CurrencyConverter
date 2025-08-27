using CurrencyConverter.API.DTOs;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace CurrencyConverter.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ExchangeRatesController : ControllerBase
{
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ILogger<ExchangeRatesController> _logger;

    // Excluded currencies as per requirements - keeping this in sync with the service
    private static readonly HashSet<string> ExcludedCurrencies = new()
    {
        "TRY", "PLN", "THB", "MXN"
    };

    public ExchangeRatesController(
        IExchangeRateService exchangeRateService,
        ILogger<ExchangeRatesController> logger)
    {
        _exchangeRateService = exchangeRateService;
        _logger = logger;
    }

    /// <summary>
    /// Get the latest exchange rates for a specific base currency
    /// </summary>
    /// <param name="baseCurrency">The base currency code (e.g., EUR, USD)</param>
    /// <returns>Latest exchange rates</returns>
    [HttpGet("latest")]
    public async Task<ActionResult<LatestRatesResponse>> GetLatestRates([FromQuery] string? baseCurrency)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(baseCurrency))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidRequest",
                    Message = "Base currency is required",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var rates = await _exchangeRateService.GetLatestExchangeRatesAsync(baseCurrency.ToUpper());

            var response = new LatestRatesResponse
            {
                BaseCurrency = baseCurrency.ToUpper(),
                Date = DateTime.UtcNow.Date,
                Rates = rates
            };

            return Ok(response);
        }
        catch (UnsupportedCurrencyException ex)
        {
            _logger.LogWarning(ex, "Unsupported currency requested: {Currency}", baseCurrency);
            return BadRequest(new ErrorResponse
            {
                Error = "UnsupportedCurrency",
                Message = ex.Message,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (ExternalApiException ex)
        {
            _logger.LogError(ex, "External API error when getting latest rates for {Currency}", baseCurrency);
            return StatusCode(503, new ErrorResponse
            {
                Error = "ServiceUnavailable",
                Message = "Exchange rate service is temporarily unavailable",
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when getting latest rates for {Currency}", baseCurrency);
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "An unexpected error occurred",
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// Get historical exchange rates for a date range
    /// </summary>
    /// <param name="baseCurrency">Base currency code</param>
    /// <param name="startDate">Start date in YYYY-MM-DD format</param>
    /// <param name="endDate">End date in YYYY-MM-DD format</param>
    /// <param name="targetCurrencies">Optional comma-separated list of target currencies</param>
    /// <returns>Historical exchange rates for the date range</returns>
    [HttpGet("range")]
    public async Task<ActionResult<RangeRatesResponse>> GetRangeRates(
        [Required][FromQuery] string baseCurrency,
        [Required][FromQuery] string startDate,
        [Required][FromQuery] string endDate,
        [FromQuery] string? targetCurrencies = null)
    {
        try
        {
            _logger.LogInformation("Getting exchange rates range for {BaseCurrency} from {StartDate} to {EndDate}", 
                baseCurrency, startDate, endDate);

            // Validate base currency first - this will throw UnsupportedCurrencyException for excluded currencies
            if (string.IsNullOrWhiteSpace(baseCurrency))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidRequest",
                    Message = "Base currency is required",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Check for excluded currencies upfront
            if (ExcludedCurrencies.Contains(baseCurrency.ToUpper()))
            {
                throw new UnsupportedCurrencyException(baseCurrency);
            }

            // Validate date formats
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStartDate))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidStartDateFormat",
                    Message = "Start date must be in YYYY-MM-DD format",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEndDate))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidEndDateFormat",
                    Message = "End date must be in YYYY-MM-DD format",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Validate date range
            if (parsedStartDate > parsedEndDate)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidDateRange",
                    Message = "Start date must be before or equal to end date",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Limit range to 1 year maximum
            if ((parsedEndDate - parsedStartDate).Days > 365)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "DateRangeTooLarge",
                    Message = "Date range cannot exceed 365 days",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Validate dates are not in the future
            if (parsedEndDate.Date > DateTime.UtcNow.Date)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "FutureDate",
                    Message = "Cannot retrieve rates for future dates",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // For now, return a sample implementation but with proper validation
            var response = new RangeRatesResponse
            {
                BaseCurrency = baseCurrency.ToUpper(),
                StartDate = parsedStartDate,
                EndDate = parsedEndDate,
                Rates = await GetRangeRatesFromProvider(baseCurrency, parsedStartDate, parsedEndDate, targetCurrencies)
            };

            _logger.LogInformation("Successfully retrieved exchange rates range for {BaseCurrency}", baseCurrency);
            return Ok(response);
        }
        catch (UnsupportedCurrencyException ex)
        {
            _logger.LogWarning(ex, "Unsupported currency in range request: {BaseCurrency}", baseCurrency);
            return BadRequest(new ErrorResponse
            {
                Error = "UnsupportedCurrency",
                Message = ex.Message,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving exchange rates range");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "An error occurred while retrieving exchange rates",
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    private async Task<Dictionary<string, Dictionary<string, decimal>>> GetRangeRatesFromProvider(
        string baseCurrency, DateTime startDate, DateTime endDate, string? targetCurrencies)
    {
        // Placeholder implementation - in production this would call the external API
        await Task.Delay(50);

        var rangeRates = new Dictionary<string, Dictionary<string, decimal>>();
        
        // Generate sample data for each day in the range
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var dailyRates = new Dictionary<string, decimal>();

            // Sample rates based on base currency
            if (baseCurrency.ToUpper() == "EUR")
            {
                dailyRates.Add("USD", 1.1234m + (decimal)(new Random().NextDouble() * 0.1 - 0.05));
                dailyRates.Add("GBP", 0.8567m + (decimal)(new Random().NextDouble() * 0.1 - 0.05));
                dailyRates.Add("JPY", 123.45m + (decimal)(new Random().NextDouble() * 10 - 5));
            }
            else if (baseCurrency.ToUpper() == "USD")
            {
                dailyRates.Add("EUR", 0.8901m + (decimal)(new Random().NextDouble() * 0.1 - 0.05));
                dailyRates.Add("GBP", 0.7623m + (decimal)(new Random().NextDouble() * 0.1 - 0.05));
                dailyRates.Add("JPY", 109.87m + (decimal)(new Random().NextDouble() * 10 - 5));
            }

            // Filter by target currencies if specified
            if (!string.IsNullOrEmpty(targetCurrencies))
            {
                var targets = targetCurrencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim().ToUpper())
                    .ToList();
                
                dailyRates = dailyRates.Where(kvp => targets.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // Filter out excluded currencies from the response
            dailyRates = dailyRates.Where(kvp => !ExcludedCurrencies.Contains(kvp.Key.ToUpper()))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            rangeRates[dateKey] = dailyRates;
        }

        return rangeRates;
    }
}
