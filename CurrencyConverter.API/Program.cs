using CurrencyConverter.API.Auth;
using CurrencyConverter.API.Configuration;
using CurrencyConverter.API.Middleware;
using CurrencyConverter.API.Services;
using CurrencyConverter.Application.Interfaces;
using CurrencyConverter.Application.Services;
using CurrencyConverter.Application.Factories;
using CurrencyConverter.Infrastructure.Caching;
using CurrencyConverter.Infrastructure.ExternalApi;
using CurrencyConverter.Infrastructure.Resilience;
using Microsoft.OpenApi.Models;
using Serilog;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Bind configuration sections
var apiSettings = new ApiSettings();
builder.Configuration.GetSection(ApiSettings.SectionName).Bind(apiSettings);
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(ApiSettings.SectionName));

var jwtSettings = new JwtSettings();
builder.Configuration.GetSection("Jwt").Bind(jwtSettings);
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Add services to the container.
builder.Services.AddControllers();

// Add JWT Authentication
builder.Services.AddJwtAuthentication(builder.Configuration);

// Register JWT Token Service
builder.Services.AddScoped<ITokenService, TokenService>();

// Add Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiUser", policy =>
        policy.RequireRole("ApiUser", "User", "Admin"));
        
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

// Add caching with fallback to memory cache if Redis is not available
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnectionString) && apiSettings.Cache.EnableDistributedCache)
{
    try
    {
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "CurrencyConverter";
        });
        Log.Information("Redis cache configured successfully");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to configure Redis cache, falling back to memory cache");
        builder.Services.AddDistributedMemoryCache();
    }
}
else
{
    Log.Information("No Redis connection string found or distributed cache disabled, using in-memory cache");
    builder.Services.AddDistributedMemoryCache();
}

// Register application services
builder.Services.AddScoped<ICacheService, DistributedCacheService>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();
builder.Services.AddScoped<ICurrencyProviderFactory, CurrencyProviderFactory>();
builder.Services.AddScoped<ICurrencyProvider, FrankfurterApiClient>();

// Configure HttpClient with resilience policies
builder.Services.AddHttpClient<FrankfurterApiClient>(client =>
{
    client.BaseAddress = new Uri(apiSettings.ExternalApis.FrankfurterApi.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(apiSettings.ExternalApis.FrankfurterApi.TimeoutSeconds);
    client.DefaultRequestHeaders.Add("User-Agent", "CurrencyConverter-API/1.0");
})
.AddPolicyHandler((services, request) => 
    RetryPolicyHandler.GetRetryPolicyWithJitter(services.GetRequiredService<ILogger<FrankfurterApiClient>>()))
.AddPolicyHandler((services, request) => 
    CircuitBreakerHandler.GetAdvancedCircuitBreakerPolicy(services.GetRequiredService<ILogger<FrankfurterApiClient>>()));

// Configure throttling
builder.Services.AddSingleton(new CurrencyConverter.API.Middleware.ThrottlingOptions
{
    MaxRequests = builder.Configuration.GetValue<int>("Throttling:MaxRequests", 100),
    TimeWindow = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Throttling:TimeWindowMinutes", 1))
});

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("CurrencyConverter.API", "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.namespace"] = "CurrencyConverter",
                ["service.instance.id"] = Environment.MachineName,
                ["deployment.environment"] = builder.Environment.EnvironmentName
            }))
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddConsoleExporter());

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Currency Converter API", 
        Version = "v1.0.0",
        Description = "A simplified currency conversion API with 3 core endpoints: token, convert, latest, and range"
    });
    
    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });

    // Include XML comments for better documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000", 
            "https://localhost:3000",
            "http://localhost:8080",
            "https://localhost:8080"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
    
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Currency Converter API v1");
        c.RoutePrefix = "swagger";
        c.DisplayRequestDuration();
        c.EnableTryItOutByDefault();
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Add custom middleware in the correct order
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseHttpsRedirection();

// Use environment-specific CORS policy
var corsPolicy = app.Environment.IsDevelopment() ? "AllowAll" : "DefaultPolicy";
app.UseCors(corsPolicy);

app.UseAuthentication();
app.UseMiddleware<TokenValidationMiddleware>();
app.UseAuthorization();

app.UseMiddleware<ApiThrottlingMiddleware>();

app.MapControllers();

// Initialize the provider factory with a scope
using (var scope = app.Services.CreateScope())
{
    try
    {
        var providerFactory = scope.ServiceProvider.GetRequiredService<ICurrencyProviderFactory>() as CurrencyProviderFactory;
        providerFactory?.RegisterProvider<FrankfurterApiClient>("Frankfurter");
        Log.Information("Currency provider factory initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to initialize currency provider factory");
    }
}

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Log.Information("Application is shutting down gracefully...");
});

Log.Information("Currency Converter API starting up...");
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

// Make Program class public for integration tests
public partial class Program { }
