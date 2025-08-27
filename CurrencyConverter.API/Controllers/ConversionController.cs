using CurrencyConverter.API.DTOs;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CurrencyConverter.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ConversionController : ControllerBase
{
    private readonly ICurrencyConversionService _conversionService;
    private readonly ILogger<ConversionController> _logger;

    public ConversionController(
        ICurrencyConversionService conversionService,
        ILogger<ConversionController> logger)
    {
        _conversionService = conversionService;
        _logger = logger;
    }

    /// <summary>
    /// Convert an amount from one currency to another
    /// </summary>
    /// <param name="request">Conversion request details</param>
    /// <returns>Conversion result</returns>
    [HttpPost("convert")]
    public async Task<ActionResult<ConversionResponse>> ConvertCurrency([FromBody] ConversionRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidRequest",
                    Message = "Invalid request data",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var convertedAmount = await _conversionService.ConvertCurrencyAsync(
                request.Amount,
                request.FromCurrency.ToUpper(),
                request.ToCurrency.ToUpper());

            // Calculate the exchange rate
            var exchangeRate = request.Amount > 0 ? convertedAmount / request.Amount : 0;

            var response = new ConversionResponse
            {
                OriginalAmount = request.Amount,
                ConvertedAmount = convertedAmount,
                FromCurrency = request.FromCurrency.ToUpper(),
                ToCurrency = request.ToCurrency.ToUpper(),
                ExchangeRate = exchangeRate,
                Timestamp = DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (UnsupportedCurrencyException ex)
        {
            _logger.LogWarning(ex, "Unsupported currency in conversion: {FromCurrency} to {ToCurrency}", 
                request.FromCurrency, request.ToCurrency);
            return BadRequest(new ErrorResponse
            {
                Error = "UnsupportedCurrency",
                Message = ex.Message,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (ExchangeRateNotFoundException ex)
        {
            _logger.LogWarning(ex, "Exchange rate not found: {FromCurrency} to {ToCurrency}", 
                request.FromCurrency, request.ToCurrency);
            return NotFound(new ErrorResponse
            {
                Error = "ExchangeRateNotFound",
                Message = ex.Message,
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (ExternalApiException ex)
        {
            _logger.LogError(ex, "External API error during conversion: {FromCurrency} to {ToCurrency}", 
                request.FromCurrency, request.ToCurrency);
            return StatusCode(503, new ErrorResponse
            {
                Error = "ServiceUnavailable",
                Message = "Exchange rate service is temporarily unavailable",
                TraceId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during conversion: {FromCurrency} to {ToCurrency}", 
                request.FromCurrency, request.ToCurrency);
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalServerError",
                Message = "An unexpected error occurred",
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}
