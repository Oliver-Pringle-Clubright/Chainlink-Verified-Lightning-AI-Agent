using LightningAgent.AI.Fraud;
using LightningAgent.AI.Judge;
using LightningAgent.AI.Negotiation;
using LightningAgent.AI.Orchestrator;
using LightningAgent.AI.TaskParser;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LightningAgent.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeAiSettings>(configuration.GetSection("ClaudeAi"));

        services.AddHttpClient<IClaudeAiClient, ClaudeApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com");
            client.Timeout = TimeSpan.FromMinutes(2);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        });

        services.AddScoped<INaturalLanguageTaskParser, NaturalLanguageTaskParser>();
        services.AddScoped<AiJudgeAgent>();
        services.AddScoped<TaskDecomposer>();
        services.AddScoped<DeliverableAssembler>();
        services.AddScoped<PriceNegotiator>();
        services.AddScoped<SybilDetector>();
        services.AddScoped<RecycledOutputDetector>();

        return services;
    }
}
