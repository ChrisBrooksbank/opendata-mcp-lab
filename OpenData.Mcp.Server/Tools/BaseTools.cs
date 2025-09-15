using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OpenData.Mcp.Server.Infrastructure;
using Polly.CircuitBreaker;

namespace OpenData.Mcp.Server.Tools
{
    /// <summary>
    /// Structured response for MCP tools containing either data or error information.
    /// Provides both raw content and parsed JSON accessors for consumers.
    /// </summary>
    public class McpToolResponse
    {
        private static readonly JsonSerializerOptions DefaultSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public string Url { get; set; } = string.Empty;
        public JsonElement? Json { get; set; }
        public string? RawContent { get; set; }
        public string? Error { get; set; }
        public int? StatusCode { get; set; }
        public bool Success => Error == null;

        /// <summary>
        /// Materialises the response payload into a strongly typed object when possible.
        /// </summary>
        public T? GetData<T>(JsonSerializerOptions? options = null)
        {
            if (Json is JsonElement jsonElement)
            {
                return jsonElement.Deserialize<T>(options ?? DefaultSerializerOptions);
            }

            if (!string.IsNullOrEmpty(RawContent))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(RawContent, options ?? DefaultSerializerOptions);
                }
                catch (JsonException)
                {
                    // If parsing fails we simply return the default value.
                }
            }

            return default;
        }
    }

    /// <summary>
    /// Base class for Parliament API tools providing common functionality for HTTP operations,
    /// retry logic, URL building, error handling, and caching.
    /// </summary>
    public abstract class BaseTools
    {
        protected static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(15);

        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;
        protected readonly IMemoryCache Cache;

        /// <summary>
        /// Allows individual tool calls to override cache behaviour.
        /// </summary>
        public record CacheSettings(bool Enabled, TimeSpan? AbsoluteExpirationRelativeToNow = null)
        {
            public static CacheSettings Default { get; } = new(true, null);
            public static CacheSettings Disabled { get; } = new(false, null);
            public static CacheSettings FromTtl(TimeSpan ttl) => new(true, ttl);
        }

        protected BaseTools(IHttpClientFactory httpClientFactory, ILogger logger, IMemoryCache cache)
        {
            HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Builds a URL with query parameters, filtering out null or empty values.
        /// </summary>
        protected static string BuildUrl(string baseUrl, Dictionary<string, string?> parameters)
        {
            var validParams = parameters
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value!)}")
                .ToArray();

            return validParams.Length > 0
                ? $"{baseUrl}?{string.Join("&", validParams)}"
                : baseUrl;
        }

        /// <summary>
        /// Makes an HTTP GET request with resiliency that relies on configured Polly policies.
        /// </summary>
        protected async Task<McpToolResponse> GetResult(string url, CacheSettings? cacheSettings = null)
        {
            var settings = cacheSettings ?? CacheSettings.Default;

            if (settings.Enabled && Cache.TryGetValue(url, out McpToolResponse? cachedResponse) && cachedResponse != null)
            {
                Logger.LogInformation("Retrieved cached result for {Url}", url);
                return cachedResponse;
            }

            try
            {
                using var httpClient = HttpClientFactory.CreateClient(HttpClientPolicyFactory.ClientName);
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Logger.LogInformation("Successfully retrieved data from {Url}", url);

                    var result = CreateSuccessResponse(url, responseContent);

                    if (settings.Enabled)
                    {
                        var cacheEntryOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = settings.AbsoluteExpirationRelativeToNow ?? CacheExpiration
                        };

                        Cache.Set(url, result, cacheEntryOptions);
                    }

                    return result;
                }

                var errorMessage = $"HTTP request failed with status {response.StatusCode}: {response.ReasonPhrase}";
                Logger.LogWarning("Non-success status {StatusCode} for {Url}", response.StatusCode, url);
                return CreateErrorResponse(url, errorMessage, (int)response.StatusCode);
            }
            catch (BrokenCircuitException ex)
            {
                Logger.LogError(ex, "Circuit breaker open for {Url}", url);
                return CreateErrorResponse(url, "Service temporarily unavailable (circuit breaker open)");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                Logger.LogWarning(ex, "Request to {Url} timed out", url);
                return CreateErrorResponse(url, "Request timed out after multiple attempts");
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError(ex, "Network error for {Url}", url);
                return CreateErrorResponse(url, $"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error for {Url}", url);
                return CreateErrorResponse(url, $"Unexpected error: {ex.Message}");
            }
        }

        private static McpToolResponse CreateSuccessResponse(string url, string content)
        {
            var json = TryParseJson(content);

            return new McpToolResponse
            {
                Url = url,
                Json = json,
                RawContent = content
            };
        }

        private static McpToolResponse CreateErrorResponse(string url, string error, int? statusCode = null)
        {
            return new McpToolResponse
            {
                Url = url,
                Error = error,
                StatusCode = statusCode
            };
        }

        private static JsonElement? TryParseJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
