using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenData.Mcp.Server.Tools;
using Xunit;
using Xunit.Sdk;

namespace OpenData.Mcp.Server.IntegrationTests;

public class ParliamentApiIntegrationTests
{
    [ParliamentIntegrationFact]
    public async Task BillsTools_ReturnsRecentBills()
    {
        using var provider = ParliamentIntegrationTestHost.CreateProvider();
        var tools = provider.GetRequiredService<BillsTools>();

        var response = await tools.GetRecentlyUpdatedBillsAsync(5);
        AssertSuccess(response, nameof(BillsTools.GetRecentlyUpdatedBillsAsync));

        AssertJsonArray(response.Json, "recent bills");
    }

    [ParliamentIntegrationFact]
    public async Task BillsTools_ReturnsBillTypes()
    {
        using var provider = ParliamentIntegrationTestHost.CreateProvider();
        var tools = provider.GetRequiredService<BillsTools>();

        var response = await tools.BillTypesAsync();
        AssertSuccess(response, nameof(BillsTools.BillTypesAsync));

        AssertJsonArray(response.Json, "bill types");
    }

    [ParliamentIntegrationFact]
    public async Task MembersTools_ReturnsAnsweringBodies()
    {
        using var provider = ParliamentIntegrationTestHost.CreateProvider();
        var tools = provider.GetRequiredService<MembersTools>();

        var response = await tools.GetAnsweringBodiesAsync();
        AssertSuccess(response, nameof(MembersTools.GetAnsweringBodiesAsync));

        AssertJsonArray(response.Json, "answering bodies");
    }

    [ParliamentIntegrationFact]
    public async Task MembersTools_SearchMembers()
    {
        using var provider = ParliamentIntegrationTestHost.CreateProvider();
        var tools = provider.GetRequiredService<MembersTools>();

        var response = await tools.SearchMembersAsync(name: "Johnson");
        AssertSuccess(response, nameof(MembersTools.SearchMembersAsync));

        AssertJsonArray(response.Json, "member search results");
    }

    [ParliamentIntegrationFact]
    public async Task NowTools_CommonsFeedResponds()
    {
        using var provider = ParliamentIntegrationTestHost.CreateProvider();
        var tools = provider.GetRequiredService<NowTools>();

        var response = await tools.HappeningNowInCommonsAsync();
        AssertSuccess(response, nameof(NowTools.HappeningNowInCommonsAsync));

        Assert.False(string.IsNullOrWhiteSpace(response.RawContent));
    }

    private static void AssertSuccess(McpToolResponse response, string operation)
    {
        if (!response.Success)
        {
            var message = $"{operation} failed with status {response.StatusCode}: {response.Error}";
            throw new XunitException(message);
        }

        Assert.False(string.IsNullOrWhiteSpace(response.RawContent));
    }

    private static void AssertJsonArray(JsonElement? element, string description)
    {
        if (element is not JsonElement jsonElement)
        {
            throw new XunitException($"{description} payload missing or not JSON.");
        }

        if (jsonElement.ValueKind == JsonValueKind.Object)
        {
            if (!jsonElement.EnumerateObject().Any())
            {
                throw new XunitException($"{description} payload empty.");
            }

            return;
        }

        if (jsonElement.ValueKind == JsonValueKind.Array)
        {
            if (!jsonElement.EnumerateArray().Any())
            {
                throw new XunitException($"{description} array empty.");
            }

            return;
        }

        throw new XunitException($"Unexpected JSON shape for {description}: {jsonElement.ValueKind}");
    }
}
