using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace OpenData.Mcp.Server.Infrastructure;

public static class HttpClientPolicyFactory
{
    public const string ClientName = "parliament-http";
    public const int RetryCount = 3;
    public const int CircuitBreakerFailureThreshold = 5;
    public static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromSeconds(30);

    public static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                RetryCount,
                retryAttempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt - 1)),
                (outcome, timespan, attempt, context) =>
                {
                    var url = outcome.Result?.RequestMessage?.RequestUri?.ToString()
                              ?? outcome.Exception?.GetBaseException().Message
                              ?? "unknown";
                    logger.LogWarning(
                        "Transient HTTP failure for {Url}. Retry {Attempt} in {Delay}ms.",
                        url,
                        attempt,
                        timespan.TotalMilliseconds);
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                CircuitBreakerFailureThreshold,
                CircuitBreakerDuration,
                (outcome, breakDelay) =>
                {
                    var url = outcome.Result?.RequestMessage?.RequestUri?.ToString()
                              ?? outcome.Exception?.GetBaseException().Message
                              ?? "unknown";
                    logger.LogWarning(
                        "Circuit opened after failures for {Url}. Blocking for {Delay}s.",
                        url,
                        breakDelay.TotalSeconds);
                },
                () => logger.LogInformation("Circuit closed."),
                () => logger.LogInformation("Circuit half-open."));
    }
}
