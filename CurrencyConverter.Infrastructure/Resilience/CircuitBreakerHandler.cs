using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

namespace CurrencyConverter.Infrastructure.Resilience;

public static class CircuitBreakerHandler
{
    //public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ILogger logger)
    //{
    //    return HttpPolicyExtensions
    //        .HandleTransientHttpError()
    //        .CircuitBreakerAsync(
    //            handledEventsAllowedBeforeBreaking: 3,
    //            durationOfBreak: TimeSpan.FromSeconds(30),
    //            onBreak: (exception, duration) =>
    //            {
    //                logger.LogWarning("Circuit breaker opened for {Duration}s due to: {Exception}", 
    //                    duration.TotalSeconds, exception.Exception?.Message);
    //            },
    //            onReset: () =>
    //            {
    //                logger.LogInformation("Circuit breaker reset");
    //            },
    //            onHalfOpen: () =>
    //            {
    //                logger.LogInformation("Circuit breaker half-open");
    //            });
    //}

    public static IAsyncPolicy<HttpResponseMessage> GetAdvancedCircuitBreakerPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5, // 50% failure rate
                samplingDuration: TimeSpan.FromSeconds(10),
                minimumThroughput: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (exception, duration) =>
                {
                    logger.LogWarning("Advanced circuit breaker opened for {Duration}s due to: {Exception}", 
                        duration.TotalSeconds, exception.Exception?.Message);
                },
                onReset: () =>
                {
                    logger.LogInformation("Advanced circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    logger.LogInformation("Advanced circuit breaker half-open");
                });
    }
}
