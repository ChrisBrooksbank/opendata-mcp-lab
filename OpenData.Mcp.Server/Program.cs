using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenData.Mcp.Server.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
builder.Services.AddMemoryCache();

var httpClientBuilder = builder.Services.AddHttpClient(HttpClientPolicyFactory.ClientName);
httpClientBuilder.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
httpClientBuilder.AddPolicyHandler((services, _) =>
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PollyRetry");
    return HttpClientPolicyFactory.CreateRetryPolicy(logger);
});
httpClientBuilder.AddPolicyHandler((services, _) =>
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PollyCircuit");
    return HttpClientPolicyFactory.CreateCircuitBreakerPolicy(logger);
});

await builder.Build().RunAsync();