using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Verification.Plugins;
using LightningAgentMarketPlace.Verification.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace LightningAgentMarketPlace.Verification;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVerificationServices(this IServiceCollection services)
    {
        services.AddScoped<IVerificationPipeline, VerificationPipeline>();
        services.AddScoped<IVerificationStrategy, AiJudgeVerification>();
        services.AddScoped<IVerificationStrategy, CodeCompileVerification>();
        services.AddScoped<IVerificationStrategy, SchemaValidationVerification>();
        services.AddScoped<IVerificationStrategy, TextSimilarityVerification>();
        services.AddScoped<IVerificationStrategy, ClipScoreVerification>();

        // Plugin verification system
        services.AddScoped<IVerificationPlugin, CodeQualityPlugin>();
        services.AddScoped<IVerificationPlugin, DataIntegrityPlugin>();
        services.AddScoped<IVerificationPlugin, TextQualityPlugin>();
        services.AddScoped<PluginVerificationRunner>();

        return services;
    }
}
