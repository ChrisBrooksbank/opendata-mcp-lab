using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenData.Mcp.Server.Infrastructure;

namespace OpenData.Mcp.Server.IntegrationTests;

internal static class ParliamentIntegrationTestHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole());
        services.AddMemoryCache();
        services.AddParliamentHttpClients();

        return services.BuildServiceProvider();
    }
}
