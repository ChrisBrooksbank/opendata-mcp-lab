using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OpenData.Mcp.Server;
using OpenData.Mcp.Server.Infrastructure;
using OpenData.Mcp.Server.Tools;
using Polly;
using Polly.Extensions.Http;
using Xunit;

namespace OpenData.Mcp.Server.Tests;

public class ToolCachingTests
{
    private static readonly IAsyncPolicy<HttpResponseMessage> RetryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(HttpClientPolicyFactory.RetryCount, _ => TimeSpan.Zero);

    private static readonly IAsyncPolicy<HttpResponseMessage> CircuitPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .CircuitBreakerAsync(HttpClientPolicyFactory.CircuitBreakerFailureThreshold, TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task MembersTools_UsesCachedResponses()
    {
        var handler = new RecordingHandler((request, call) => SuccessResponse($"{{\"call\":{call}}}"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = CreateHttpClient(handler);
        var tools = new MembersTools(httpClient, NullLogger<MembersTools>.Instance, cache);

        await tools.GetAnsweringBodiesAsync();
        await tools.GetAnsweringBodiesAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task NowTools_DisablesCaching()
    {
        var handler = new RecordingHandler((request, call) => SuccessResponse($"{{\"call\":{call}}}"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = CreateHttpClient(handler);
        var tools = new NowTools(httpClient, NullLogger<NowTools>.Instance, cache);

        await tools.HappeningNowInCommonsAsync();
        await tools.HappeningNowInCommonsAsync();

        Assert.Equal(2, handler.CallCount);
    }

    private static HttpResponseMessage SuccessResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static HttpClient CreateHttpClient(RecordingHandler handler)
    {
        var circuitHandler = new PolicyDelegatingHandler(CircuitPolicy)
        {
            InnerHandler = handler
        };

        var retryHandler = new PolicyDelegatingHandler(RetryPolicy)
        {
            InnerHandler = circuitHandler
        };

        return new HttpClient(retryHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
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