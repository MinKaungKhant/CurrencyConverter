using CurrencyConverter.API.DTOs;
using CurrencyConverter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CurrencyConverter.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ITokenService tokenService, ILogger<AuthController> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Generate a JWT token for API access
    /// </summary>
    /// <param name="request">Token generation request</param>
    /// <returns>JWT token response</returns>
    [HttpPost("token")]
    public ActionResult<TokenResponse> GenerateToken([FromBody] TokenRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidRequest",
                    Message = "Invalid token request data",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            // Validate role
            var validRoles = new[] { "ApiUser", "User", "Admin" };
            if (!validRoles.Contains(request.Role))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "InvalidRole",
                    Message = $"Role must be one of: {string.Join(", ", validRoles)}",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var tokenResponse = _tokenService.GenerateToken(request);

            _logger.LogInformation("JWT token generated for client: {ClientId}", request.ClientId);

            return Ok(tokenResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for client: {ClientId}", request.ClientId);
            return StatusCode(500, new ErrorResponse
            {
                Error = "TokenGenerationFailed",
                Message = "Failed to generate JWT token",
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }
}