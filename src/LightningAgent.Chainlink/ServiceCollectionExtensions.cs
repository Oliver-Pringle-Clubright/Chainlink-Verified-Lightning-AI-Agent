using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LightningAgent.Chainlink;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChainlinkServices(this IServiceCollection services)
    {
        services.AddScoped<IChainlinkPriceFeedClient, ChainlinkPriceFeedClient>();
        services.AddScoped<IChainlinkFunctionsClient, ChainlinkFunctionsClient>();
        services.AddScoped<IChainlinkVrfClient, ChainlinkVrfClient>();
        services.AddScoped<IChainlinkAutomationClient, ChainlinkAutomationClient>();
        return services;
    }
}
