using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Verification.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace LightningAgent.Verification;

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
        return services;
    }
}
