using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using OpenData.Mcp.Server.Tools;
using OpenData.Mcp.Server;

namespace OpenData.Mcp.Server.Infrastructure;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddParliamentHttpClients(this IServiceCollection services)
    {
        services.AddConfiguredClient<BillsTools>(BillsTools.BillsApiBase);
        services.AddConfiguredClient<CommitteesTools>(CommitteesTools.CommitteesApiBase);
        services.AddConfiguredClient<CommonsVotesTools>(CommonsVotesTools.CommonsVotesApiBase);
        services.AddConfiguredClient<ErskineMayTools>(ErskineMayTools.ErskineMayApiBase);
        services.AddConfiguredClient<HansardTools>(HansardTools.HansardApiBase);
        services.AddConfiguredClient<InterestsTools>(InterestsTools.InterestsApiBase);
        services.AddConfiguredClient<LordsVotesTools>(LordsVotesTools.LordsVotesApiBase);
        services.AddConfiguredClient<MembersTools>(MembersTools.MembersApiBase);
        services.AddConfiguredClient<NowTools>(NowTools.NowApiBase);
        services.AddConfiguredClient<OralQuestionsTools>(OralQuestionsTools.OralQuestionsApiBase);
        services.AddConfiguredClient<StatutoryInstrumentsTools>(StatutoryInstrumentsTools.StatutoryInstrumentsApiBase);
        services.AddConfiguredClient<TreatiesTools>(TreatiesTools.TreatiesApiBase);
        services.AddConfiguredClient<WhatsOnTools>(WhatsOnTools.WhatsonApiBase);
        services.AddConfiguredClient<CoreTools>(baseAddress: null);

        return services;
    }

    private static void AddConfiguredClient<TTool>(this IServiceCollection services, string? baseAddress)
        where TTool : BaseTools
    {
        var builder = services.AddHttpClient<TTool>(client =>
        {
            client.Timeout = DefaultTimeout;
            if (!string.IsNullOrEmpty(baseAddress))
            {
                client.BaseAddress = new Uri(baseAddress);
            }
        });

        builder.AddPolicyHandler((sp, _) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"{typeof(TTool).Name}Retry");
            return HttpClientPolicyFactory.CreateRetryPolicy(logger);
        });

        builder.AddPolicyHandler((sp, _) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"{typeof(TTool).Name}Circuit");
            return HttpClientPolicyFactory.CreateCircuitBreakerPolicy(logger);
        });
    }
}


