using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OpenData.Mcp.Server.Infrastructure;
using OpenData.Mcp.Server.Tools;
using Polly;
using Polly.Extensions.Http;
using Xunit;

namespace OpenData.Mcp.Server.Tests;

public class BaseToolsTests
{
    [Fact]
    public async Task GetResult_ReturnsJsonAndRawData()
    {
        var handler = new RecordingHandler((_, _) => SuccessResponse("{\"value\":42}"));
        var tools = CreateTools(handler);

        var response = await tools.ExecuteAsync("https://example.com/data");

        Assert.True(response.Success);
        Assert.True(response.Json.HasValue);
        Assert.Equal("{\"value\":42}", response.RawContent);

        var typed = response.GetData<TestPayload>();
        Assert.NotNull(typed);
        Assert.Equal(42, typed!.Value);
    }

    [Fact]
    public async Task GetResult_CachesResponsesByDefault()
    {
        var handler = new RecordingHandler((_, call) => SuccessResponse($"{{\"call\":{call}}}"));
        var tools = CreateTools(handler);

        var first = await tools.ExecuteAsync("https://example.com/cache");
        var second = await tools.ExecuteAsync("https://example.com/cache");

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(first.RawContent, second.RawContent);
    }

    [Fact]
    public async Task GetResult_RespectsDisabledCache()
    {
        var handler = new RecordingHandler((_, call) => SuccessResponse($"{{\"call\":{call}}}"));
        var tools = CreateTools(handler);

        await tools.ExecuteAsync("https://example.com/no-cache", BaseTools.CacheSettings.Disabled);
        await tools.ExecuteAsync("https://example.com/no-cache", BaseTools.CacheSettings.Disabled);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetResult_AllowsCustomExpiration()
    {
        var handler = new RecordingHandler((_, call) => SuccessResponse($"{{\"call\":{call}}}"));
        var tools = CreateTools(handler);

        await tools.ExecuteAsync("https://example.com/ttl", BaseTools.CacheSettings.FromTtl(TimeSpan.FromMilliseconds(25)));
        await Task.Delay(60);
        await tools.ExecuteAsync("https://example.com/ttl", BaseTools.CacheSettings.FromTtl(TimeSpan.FromMilliseconds(25)));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetResult_RetriesTransientFailures()
    {
        var handler = new RecordingHandler((_, call) =>
        {
            if (call == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(string.Empty)
                };
            }

            return SuccessResponse("{\"value\":1}");
        });

        var tools = CreateTools(handler);
        var response = await tools.ExecuteAsync("https://example.com/retry");

        Assert.True(response.Success);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetResult_ReturnsErrorAfterRetries()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Server Error",
            Content = new StringContent(string.Empty)
        });

        var tools = CreateTools(handler);
        var response = await tools.ExecuteAsync("https://example.com/fail");

        Assert.False(response.Success);
        Assert.Equal((int)HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("HTTP request failed", response.Error);
        Assert.Equal(HttpClientPolicyFactory.RetryCount + 1, handler.CallCount);
    }

    [Fact]
    public async Task GetResult_ReturnsRawForNonJson()
    {
        const string rss = "<rss><channel></channel></rss>";
        var handler = new RecordingHandler((_, _) => SuccessResponse(rss, "application/rss+xml"));
        var tools = CreateTools(handler);

        var response = await tools.ExecuteAsync("https://example.com/rss");

        Assert.True(response.Success);
        Assert.False(response.Json.HasValue);
        Assert.Equal(rss, response.RawContent);
        Assert.Null(response.GetData<TestPayload>());
    }

    [Fact]
    public async Task GetResult_ReturnsCircuitBreakerError()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var retryPolicy = CreateTestRetryPolicy();
        var breakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(1, TimeSpan.FromSeconds(1));
        var tools = CreateTools(handler, retryPolicy, breakerPolicy);

        await tools.ExecuteAsync("https://example.com/breaker");

        var response = await tools.ExecuteAsync("https://example.com/breaker");

        Assert.False(response.Success);
        Assert.Contains("circuit breaker", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage SuccessResponse(string content, string mediaType = "application/json")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
        };
    }

    private static TestBaseTools CreateTools(
        RecordingHandler handler,
        IAsyncPolicy<HttpResponseMessage>? retryPolicy = null,
        IAsyncPolicy<HttpResponseMessage>? circuitBreakerPolicy = null)
    {
        retryPolicy ??= CreateTestRetryPolicy();
        circuitBreakerPolicy ??= CreateTestCircuitBreakerPolicy();

        var client = CreateHttpClient(handler, retryPolicy, circuitBreakerPolicy);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new TestBaseTools(client, cache);
    }

    private static HttpClient CreateHttpClient(
        RecordingHandler handler,
        IAsyncPolicy<HttpResponseMessage> retryPolicy,
        IAsyncPolicy<HttpResponseMessage> circuitBreakerPolicy)
    {
        var circuitHandler = new PolicyDelegatingHandler(circuitBreakerPolicy)
        {
            InnerHandler = handler
        };

        var retryHandler = new PolicyDelegatingHandler(retryPolicy)
        {
            InnerHandler = circuitHandler
        };

        return new HttpClient(retryHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateTestRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(HttpClientPolicyFactory.RetryCount, _ => TimeSpan.Zero);
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateTestCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(HttpClientPolicyFactory.CircuitBreakerFailureThreshold, TimeSpan.FromMilliseconds(50));
    }

    private sealed record TestPayload(int Value);

    private sealed class TestBaseTools : BaseTools
    {
        public TestBaseTools(HttpClient httpClient, IMemoryCache cache)
            : base(httpClient, NullLogger.Instance, cache)
        {
        }

        public Task<McpToolResponse> ExecuteAsync(string url, CacheSettings? settings = null)
            => GetResult(url, settings);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = ++CallCount;
            var response = _responseFactory(request, call);
            return Task.FromResult(response);
        }
    }

    private sealed class PolicyDelegatingHandler : DelegatingHandler
    {
        private readonly IAsyncPolicy<HttpResponseMessage> _policy;

        public PolicyDelegatingHandler(IAsyncPolicy<HttpResponseMessage> policy)
        {
            _policy = policy;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _policy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
        }
    }
}
