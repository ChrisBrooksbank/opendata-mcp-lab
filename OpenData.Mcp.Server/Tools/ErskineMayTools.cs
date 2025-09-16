using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenData.Mcp.Server.Tools;
using System.ComponentModel;

namespace OpenData.Mcp.Server
{
    [McpServerToolType]
    public class ErskineMayTools(HttpClient httpClient, ILogger<ErskineMayTools> logger, IMemoryCache cache) : BaseTools(httpClient, logger, cache)
    {
        internal const string ErskineMayApiBase = "https://erskinemay-api.parliament.uk/api";

        [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Search Erskine May parliamentary procedure manual. Use when you need to understand parliamentary rules, procedures, or precedents. Erskine May is the authoritative guide to parliamentary procedure.")]
        public async Task<McpToolResponse> SearchErskineMayAsync([Description("Search term for parliamentary procedure rules (e.g. 'Speaker', 'amendment', 'division')")] string searchTerm)
        {
            var url = $"{ErskineMayApiBase}/Search/ParagraphSearchResults/{Uri.EscapeDataString(searchTerm)}";
            return await GetResult(url);
        }
    }
}
