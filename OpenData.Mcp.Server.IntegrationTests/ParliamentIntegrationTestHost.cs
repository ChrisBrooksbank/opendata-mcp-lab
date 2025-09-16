using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenData.Mcp.Server.Infrastructure;
using OpenData.Mcp.Server.Tools;

namespace OpenData.Mcp.Server.IntegrationTests;

internal static class ParliamentIntegrationTestHost
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole());
        services.AddMemoryCache();

        services.AddHttpClient(HttpClientPolicyFactory.ClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddPolicyHandler((sp, _) =>
            {
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("PollyRetry");
                return HttpClientPolicyFactory.CreateRetryPolicy(logger);
            })
            .AddPolicyHandler((sp, _) =>
            {
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("PollyCircuit");
                return HttpClientPolicyFactory.CreateCircuitBreakerPolicy(logger);
            });

        services.AddTransient<BillsTools>();
        services.AddTransient<MembersTools>();
        services.AddTransient<NowTools>();

        return services.BuildServiceProvider();
    }
}
