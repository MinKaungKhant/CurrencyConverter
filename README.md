# Currency Converter API

A robust, enterprise-grade currency conversion API built with .NET 8 that provides real-time and historical exchange rates with JWT authentication, caching, rate limiting, and comprehensive error handling.

## ??? Architecture

This solution follows **Clean Architecture** principles with clear separation of concerns:

```
CurrencyConverter/
??? CurrencyConverter.API/          # Web API layer (Controllers, Middleware, Auth)
??? CurrencyConverter.Application/  # Application logic (Services, Interfaces)
??? CurrencyConverter.Domain/       # Domain entities and exceptions
??? CurrencyConverter.Infrastructure/# External dependencies (APIs, Cache, Resilience)
??? CurrencyConverter.Tests/        # Integration and unit tests
```

### Key Design Patterns
- **Repository Pattern** with provider factory for currency data sources
- **Circuit Breaker Pattern** for external API resilience
- **Retry Pattern** with exponential backoff and jitter
- **Dependency Injection** throughout all layers
- **Middleware Pipeline** for cross-cutting concerns

## ?? Features

### Core Functionality
- **Currency Conversion**: Convert amounts between supported currencies
- **Latest Exchange Rates**: Get current exchange rates for any base currency
- **Historical Rates**: Retrieve exchange rates for date ranges (up to 365 days)
- **JWT Authentication**: Secure API access with configurable token expiration

### Enterprise Features
- **Distributed Caching**: Redis with fallback to in-memory cache
- **Rate Limiting**: Configurable throttling per client
- **Circuit Breaker**: Automatic failure detection and recovery
- **Structured Logging**: Comprehensive logging with Serilog
- **OpenTelemetry**: Distributed tracing and monitoring
- **Health Checks**: API health monitoring
- **Swagger Documentation**: Interactive API documentation

### Data Provider
- **Frankfurter API**: Primary data source for exchange rates
- **Extensible Design**: Easy to add additional currency providers
- **Currency Filtering**: Excludes specific currencies (TRY, PLN, THB, MXN) per business requirements

## ?? Prerequisites

- **.NET 8.0 SDK** or later
- **Redis** (optional - will fallback to in-memory cache)
- **Visual Studio 2022** or **VS Code** (recommended)

## ?? Installation & Setup

### 1. Clone the Repository
```bash
git clone <repository-url>
cd CurrencyConverter
```

### 2. Restore Dependencies
```bash
dotnet restore
```

### 3. Configuration
Update `appsettings.json` in the API project:

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "SecretKey": "YourSecretKeyHere-MustBeAtLeast256Bits",
    "Issuer": "CurrencyConverter.API",
    "Audience": "CurrencyConverter.API.Users",
    "ExpirationMinutes": 60
  },
  "ApiSettings": {
    "RateLimiting": {
      "RequestsPerMinute": 100,
      "RequestsPerHour": 1000,
      "RequestsPerDay": 10000
    },
    "Cache": {
      "ExchangeRatesCacheMinutes": 5,
      "EnableDistributedCache": true
    }
  }
}
```

### 4. Run the Application
```bash
cd CurrencyConverter.API
dotnet run
```

The API will be available at:
- **HTTPS**: `https://localhost:7216`
- **HTTP**: `http://localhost:5147`
- **Swagger**: `https://localhost:7216/swagger`

## ?? API Endpoints

### Authentication

#### Generate JWT Token
```http
POST /api/v1/auth/token
Content-Type: application/json

{
  "clientId": "your-client-id",
  "clientName": "Your Application",
  "role": "ApiUser",
  "expirationMinutes": 60
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "expiresAt": "2025-01-15T12:00:00Z",
  "clientId": "your-client-id",
  "role": "ApiUser"
}
```

### Currency Conversion

#### Convert Currency
```http
POST /api/v1/conversion/convert
Authorization: Bearer {your-jwt-token}
Content-Type: application/json

{
  "amount": 100.00,
  "fromCurrency": "EUR",
  "toCurrency": "USD"
}
```

**Response:**
```json
{
  "originalAmount": 100.00,
  "convertedAmount": 110.25,
  "fromCurrency": "EUR",
  "toCurrency": "USD",
  "exchangeRate": 1.1025,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### Exchange Rates

#### Get Latest Rates
```http
GET /api/v1/exchangerates/latest?baseCurrency=EUR
Authorization: Bearer {your-jwt-token}
```

**Response:**
```json
{
  "baseCurrency": "EUR",
  "date": "2025-01-15T00:00:00Z",
  "rates": {
    "USD": 1.1025,
    "GBP": 0.8567,
    "JPY": 123.45,
    "CHF": 0.9234
  }
}
```

#### Get Historical Rates Range
```http
GET /api/v1/exchangerates/range?baseCurrency=EUR&startDate=2025-01-01&endDate=2025-01-31&targetCurrencies=USD,GBP
Authorization: Bearer {your-jwt-token}
```

**Response:**
```json
{
  "baseCurrency": "EUR",
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T00:00:00Z",
  "rates": {
    "2025-01-01": {
      "USD": 1.1025,
      "GBP": 0.8567
    },
    "2025-01-02": {
      "USD": 1.1034,
      "GBP": 0.8571
    }
  }
}
```

## ?? Authentication & Authorization

The API uses **JWT Bearer tokens** for authentication:

1. **Obtain a token** from `/api/v1/auth/token`
2. **Include the token** in the Authorization header: `Bearer {token}`
3. **Token expiration** is configurable (default: 60 minutes)

### Supported Roles
- **ApiUser**: Standard API access
- **User**: Standard user permissions
- **Admin**: Administrative access (future use)

## ? Performance & Resilience

### Caching Strategy
- **Redis Distributed Cache**: Primary caching with configurable TTL
- **Memory Cache Fallback**: Automatic fallback if Redis is unavailable
- **Cache Keys**: Structured for efficient invalidation

### Rate Limiting
- **Configurable Limits**: Requests per minute/hour/day
- **Per-Client Tracking**: Individual client quotas
- **Graceful Degradation**: Proper error responses when limits exceeded

### Resilience Patterns
- **Circuit Breaker**: Protects against cascading failures
- **Retry with Jitter**: Exponential backoff for transient failures
- **Timeout Handling**: Configurable request timeouts

## ?? Testing

### Run Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test category
dotnet test --filter Category=Integration
```

### Test Structure
- **Integration Tests**: Full API testing with TestServer
- **Unit Tests**: Service and component testing
- **Test Fixtures**: Shared test infrastructure
- **Mock Authentication**: Simplified auth for testing

## ?? Monitoring & Logging

### Structured Logging
- **Serilog**: Structured logging with multiple sinks
- **Log Levels**: Configurable per component
- **Request Tracing**: Correlation IDs for request tracking

### Observability
- **OpenTelemetry**: Distributed tracing
- **Health Checks**: Application health monitoring
- **Metrics**: Performance and usage metrics

### Log Outputs
- **Console**: Development logging
- **File**: Persistent log storage with rotation
- **Structured Format**: JSON for log aggregation

## ?? Error Handling

### Standardized Error Responses
```json
{
  "error": "UnsupportedCurrency",
  "message": "Currency 'XYZ' is not supported",
  "timestamp": "2025-01-15T10:30:00Z",
  "traceId": "0HN7GSPL9U9QK:00000001"
}
```

### Error Categories
- **400 Bad Request**: Invalid input data
- **401 Unauthorized**: Missing or invalid authentication
- **404 Not Found**: Resource not found
- **429 Too Many Requests**: Rate limit exceeded
- **503 Service Unavailable**: External service unavailable
- **500 Internal Server Error**: Unexpected errors

## ?? Configuration

### Environment Variables
```bash
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=https://localhost:7216;http://localhost:5147
ConnectionStrings__Redis=localhost:6379
Jwt__SecretKey=YourSecretKey
```

### Configuration Sections
- **ConnectionStrings**: Database and cache connections
- **Jwt**: JWT token configuration
- **ApiSettings**: Rate limiting, caching, external APIs
- **Serilog**: Logging configuration
- **Throttling**: Request throttling settings

## ?? Deployment

### Docker Support
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CurrencyConverter.API/CurrencyConverter.API.csproj", "CurrencyConverter.API/"]
RUN dotnet restore "CurrencyConverter.API/CurrencyConverter.API.csproj"
COPY . .
WORKDIR "/src/CurrencyConverter.API"
RUN dotnet build "CurrencyConverter.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CurrencyConverter.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CurrencyConverter.API.dll"]
```

### Production Considerations
- **HTTPS Only**: Enforce HTTPS in production
- **Secret Management**: Use Azure Key Vault or similar
- **Load Balancing**: Configure for horizontal scaling
- **Database**: Consider persistent storage for historical data
- **Monitoring**: Implement comprehensive monitoring and alerting

## ??? Development

### Project Structure
```
CurrencyConverter.API/
??? Controllers/          # API controllers
??? DTOs/                # Data transfer objects
??? Middleware/          # Custom middleware
??? Services/            # API-specific services
??? Auth/               # Authentication/Authorization
??? Configuration/      # Configuration models

CurrencyConverter.Application/
??? Interfaces/         # Service contracts
??? Services/          # Business logic
??? Factories/         # Object factories

CurrencyConverter.Domain/
??? Entities/          # Domain entities
??? Exceptions/        # Domain exceptions

CurrencyConverter.Infrastructure/
??? ExternalApi/       # External API clients
??? Cache/            # Caching implementations
??? Resilience/       # Resilience patterns
```

### Adding New Currency Providers
1. Implement `ICurrencyProvider` interface
2. Register in `CurrencyProviderFactory`
3. Configure in dependency injection
4. Add provider-specific settings

### Code Style
- **C# 12** language features
- **Nullable reference types** enabled
- **Async/await** throughout
- **SOLID principles** applied
- **Clean Code** practices

## ?? Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## ?? License

This project is licensed under the MIT License - see the LICENSE file for details.

## ?? Dependencies

### Key NuGet Packages
- **Microsoft.AspNetCore.Authentication.JwtBearer** - JWT authentication
- **StackExchange.Redis** - Redis caching
- **Serilog.AspNetCore** - Structured logging
- **OpenTelemetry** - Observability and tracing
- **Polly** - Resilience patterns
- **Swashbuckle.AspNetCore** - API documentation

### External Services
- **Frankfurter API** - Exchange rate data provider
- **Redis** - Distributed caching (optional)

## ?? Support

For questions, issues, or contributions:
- Create an issue in the repository
- Check existing documentation
- Review test cases for usage examples

---

**Built with ?? using .NET 8 and Clean Architecture principles**