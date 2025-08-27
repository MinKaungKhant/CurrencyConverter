using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace CurrencyConverter.Infrastructure.Resilience;

public static class RetryPolicyHandler
{
    //public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger logger)
    //{
    //    return HttpPolicyExtensions
    //        .HandleTransientHttpError()
    //        .WaitAndRetryAsync(
    //            retryCount: 3,
    //            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
    //            onRetry: (outcome, timespan, retryCount, context) =>
    //            {
    //                logger.LogWarning("Retry {RetryCount} for {OperationKey} after {Delay}ms", 
    //                    retryCount, context.OperationKey, timespan.TotalMilliseconds);
    //            });
    //}

    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicyWithJitter(ILogger logger)
    {
        var random = new Random();
        
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    var jitter = TimeSpan.FromMilliseconds(random.Next(0, 1000));
                    return delay + jitter;
                },
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    logger.LogWarning("Retry {RetryCount} for {OperationKey} after {Delay}ms", 
                        retryCount, context.OperationKey, timespan.TotalMilliseconds);
                });
    }
}
