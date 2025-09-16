using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenData.Mcp.Server.Context;
using OpenData.Mcp.Server.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

var contextDirectory = Path.Combine(AppContext.BaseDirectory, "context");
var contextResources = ContextResourceRegistry.LoadResources(contextDirectory);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResources(contextResources);

builder.Services.AddMemoryCache();
builder.Services.AddParliamentHttpClients();

await builder.Build().RunAsync();
