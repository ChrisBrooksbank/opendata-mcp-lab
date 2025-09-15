using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace OpenData.Mcp.Server.Tools
{
    /// <summary>
    /// Structured response for MCP tools containing either data or error information.
    /// </summary>
    public class McpToolResponse
    {
        public string Url { get; set; } = string.Empty;
        public object? Data { get; set; }
        public string? Error { get; set; }
        public int? StatusCode { get; set; }
        public bool Success => Error == null;
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
        protected const int MaxRetryAttempts = 3;
        protected static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        // Cache configuration constants
        protected static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(15);

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
        /// <returns>Structured response containing URL and either data or error details</returns>
        protected async Task<McpToolResponse> GetResult(string url)
        {
            // Check cache first
            if (Cache.TryGetValue(url, out McpToolResponse? cachedResponse) && cachedResponse != null)
            {
                Logger.LogInformation("Retrieved cached result for {Url}", url);
                return cachedResponse;
            }

            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var httpClient = HttpClientFactory.CreateClient();
                    httpClient.Timeout = HttpTimeout;
                    
                    Logger.LogInformation("Making HTTP request to {Url} (attempt {Attempt}/{MaxAttempts})", 
                        url, attempt + 1, MaxRetryAttempts);
                    
                    var response = await httpClient.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Logger.LogInformation("Successfully retrieved data from {Url}", url);

                        // Try to parse as JSON, fallback to string if it fails
                        object? data;
                        try
                        {
                            data = JsonSerializer.Deserialize<object>(responseContent);
                        }
                        catch (JsonException)
                        {
                            data = responseContent;
                        }

                        var result = new McpToolResponse { Url = url, Data = data };

                        // Cache successful results
                        Cache.Set(url, result, CacheExpiration);

                        return result;
                    }
                    
                    if (IsTransientFailure(response.StatusCode))
                    {
                        Logger.LogWarning("Transient failure for {Url}: {StatusCode}. Attempt {Attempt}/{MaxAttempts}", 
                            url, response.StatusCode, attempt + 1, MaxRetryAttempts);
                        
                        if (attempt < MaxRetryAttempts - 1)
                        {
                            await Task.Delay(RetryDelay * (attempt + 1));
                            continue;
                        }
                    }
                    
                    var errorMessage = $"HTTP request failed with status {response.StatusCode}: {response.ReasonPhrase}";
                    Logger.LogError("Final failure for {Url}: {StatusCode}", url, response.StatusCode);
                    return new McpToolResponse
                    {
                        Url = url,
                        Error = errorMessage,
                        StatusCode = (int)response.StatusCode
                    };
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    Logger.LogWarning("Request to {Url} timed out. Attempt {Attempt}/{MaxAttempts}", 
                        url, attempt + 1, MaxRetryAttempts);
                    
                    if (attempt < MaxRetryAttempts - 1)
                    {
                        await Task.Delay(RetryDelay * (attempt + 1));
                        continue;
                    }
                    
                    var timeoutError = "Request timed out after multiple attempts";
                    Logger.LogError("Request to {Url} timed out after all retry attempts", url);
                    return new McpToolResponse
                    {
                        Url = url,
                        Error = timeoutError
                    };
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogWarning(ex, "HTTP request exception for {Url}. Attempt {Attempt}/{MaxAttempts}", 
                        url, attempt + 1, MaxRetryAttempts);
                    
                    if (attempt < MaxRetryAttempts - 1)
                    {
                        await Task.Delay(RetryDelay * (attempt + 1));
                        continue;
                    }
                    
                    var networkError = $"Network error: {ex.Message}";
                    Logger.LogError(ex, "Network error for {Url} after all retry attempts", url);
                    return new McpToolResponse
                    {
                        Url = url,
                        Error = networkError
                    };
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error for {Url}", url);
                    return new McpToolResponse
                    {
                        Url = url,
                        Error = $"Unexpected error: {ex.Message}"
                    };
                }
            }

            return new McpToolResponse
            {
                Url = url,
                Error = "Maximum retry attempts exceeded"
            };
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

      
    }
}