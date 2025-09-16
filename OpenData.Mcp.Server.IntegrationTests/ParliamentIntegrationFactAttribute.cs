using System;
using Xunit;

namespace OpenData.Mcp.Server.IntegrationTests;

/// <summary>
/// Marks integration tests that require live Parliament APIs.
/// Tests are skipped unless RUN_PARLIAMENT_INTEGRATION_TESTS=true.
/// </summary>
public sealed class ParliamentIntegrationFactAttribute : FactAttribute
{
    private const string EnvVar = "RUN_PARLIAMENT_INTEGRATION_TESTS";

    public ParliamentIntegrationFactAttribute()
    {
        var flag = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Skipped. Set {EnvVar}=true to run live Parliament integration tests.";
        }
    }
}
