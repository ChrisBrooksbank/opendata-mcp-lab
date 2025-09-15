using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OpenData.Mcp.Server;
using OpenData.Mcp.Server.Tools;
using Xunit;

namespace OpenData.Mcp.Server.Tests;

public class ToolCachingTests
{
    [Fact]
    public async Task MembersTools_UsesCachedResponses()
    {
        var handler = new RecordingHandler((request, call) => SuccessResponse($"{{\"call\":{call}}}"));
        var factory = new TestHttpClientFactory(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tools = new MembersTools(factory, NullLogger<MembersTools>.Instance, cache);

        await tools.GetAnsweringBodiesAsync();
        await tools.GetAnsweringBodiesAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task NowTools_DisablesCaching()
    {
        var handler = new RecordingHandler((request, call) => SuccessResponse($"{{\"call\":{call}}}"));
        var factory = new TestHttpClientFactory(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tools = new NowTools(factory, NullLogger<NowTools>.Instance, cache);

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

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
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
}