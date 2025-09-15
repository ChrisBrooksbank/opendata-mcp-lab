using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

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
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;
        protected readonly IMemoryCache Cache;

        // HTTP configuration constants
        protected static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
        public const int MaxRetryAttempts = 3;
        protected static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        // Cache configuration constants
        protected static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(15);

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
        /// <param name="baseUrl">The base URL</param>
        /// <param name="parameters">Dictionary of parameter key-value pairs</param>
        /// <returns>Complete URL with query string</returns>
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
        /// Makes an HTTP GET request with retry logic, caching, and comprehensive error handling.
        /// Returns structured response with URL and data/error information.
        /// </summary>
        /// <param name="url">The URL to make the request to</param>
        /// <param name="cacheSettings">Optional cache settings for the request</param>
        /// <returns>Structured response containing URL and either data or error details</returns>
        protected async Task<McpToolResponse> GetResult(string url, CacheSettings? cacheSettings = null)
        {
            var settings = cacheSettings ?? CacheSettings.Default;

            if (settings.Enabled && Cache.TryGetValue(url, out McpToolResponse? cachedResponse) && cachedResponse != null)
            {
                Logger.LogInformation("Retrieved cached result for {Url}", url);
                return cachedResponse;
            }

            for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var httpClient = HttpClientFactory.CreateClient();
                    httpClient.Timeout = HttpTimeout;

                    Logger.LogInformation(
                        "Making HTTP request to {Url} (attempt {Attempt}/{MaxAttempts})",
                        url,
                        attempt + 1,
                        MaxRetryAttempts);

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

                    if (IsTransientFailure(response.StatusCode))
                    {
                        Logger.LogWarning(
                            "Transient failure for {Url}: {StatusCode}. Attempt {Attempt}/{MaxAttempts}",
                            url,
                            response.StatusCode,
                            attempt + 1,
                            MaxRetryAttempts);

                        if (attempt < MaxRetryAttempts - 1)
                        {
                            await Task.Delay(RetryDelay * (attempt + 1));
                            continue;
                        }
                    }

                    var errorMessage = $"HTTP request failed with status {response.StatusCode}: {response.ReasonPhrase}";
                    Logger.LogError("Final failure for {Url}: {StatusCode}", url, response.StatusCode);
                    return CreateErrorResponse(url, errorMessage, (int)response.StatusCode);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    Logger.LogWarning(
                        "Request to {Url} timed out. Attempt {Attempt}/{MaxAttempts}",
                        url,
                        attempt + 1,
                        MaxRetryAttempts);

                    if (attempt < MaxRetryAttempts - 1)
                    {
                        await Task.Delay(RetryDelay * (attempt + 1));
                        continue;
                    }

                    var timeoutError = "Request timed out after multiple attempts";
                    Logger.LogError("Request to {Url} timed out after all retry attempts", url);
                    return CreateErrorResponse(url, timeoutError);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogWarning(
                        ex,
                        "HTTP request exception for {Url}. Attempt {Attempt}/{MaxAttempts}",
                        url,
                        attempt + 1,
                        MaxRetryAttempts);

                    if (attempt < MaxRetryAttempts - 1)
                    {
                        await Task.Delay(RetryDelay * (attempt + 1));
                        continue;
                    }

                    var networkError = $"Network error: {ex.Message}";
                    Logger.LogError(ex, "Network error for {Url} after all retry attempts", url);
                    return CreateErrorResponse(url, networkError);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error for {Url}", url);
                    return CreateErrorResponse(url, $"Unexpected error: {ex.Message}");
                }
            }

            return CreateErrorResponse(url, "Maximum retry attempts exceeded");
        }

        /// <summary>
        /// Determines if an HTTP status code represents a transient failure that should be retried.
        /// </summary>
        /// <param name="statusCode">The HTTP status code to check</param>
        /// <returns>True if the failure is transient and should be retried</returns>
        private static bool IsTransientFailure(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.InternalServerError ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout;
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