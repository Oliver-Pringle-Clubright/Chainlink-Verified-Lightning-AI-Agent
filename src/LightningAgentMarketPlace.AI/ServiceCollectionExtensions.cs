using LightningAgentMarketPlace.AI.Fraud;
using LightningAgentMarketPlace.AI.Judge;
using LightningAgentMarketPlace.AI.Negotiation;
using LightningAgentMarketPlace.AI.Orchestrator;
using LightningAgentMarketPlace.AI.TaskParser;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LightningAgentMarketPlace.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeAiSettings>(configuration.GetSection("ClaudeAi"));
        services.Configure<OpenRouterSettings>(configuration.GetSection("OpenRouter"));

        // Primary Claude API client
        services.AddHttpClient<ClaudeApiClient>(client =>
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

        // MultiModelClient is the primary IClaudeAiClient — tries OpenRouter first,
        // falls back to ClaudeApiClient when OpenRouter is unavailable or not configured
        services.AddHttpClient<MultiModelClient>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        });

        services.AddScoped<IClaudeAiClient>(sp => sp.GetRequiredService<MultiModelClient>());

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
