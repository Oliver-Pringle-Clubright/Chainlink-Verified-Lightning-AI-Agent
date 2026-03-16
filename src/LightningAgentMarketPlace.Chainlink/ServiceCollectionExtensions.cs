using LightningAgentMarketPlace.Chainlink.Services;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LightningAgentMarketPlace.Chainlink;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChainlinkServices(this IServiceCollection services)
    {
        services.AddScoped<IChainlinkPriceFeedClient, ChainlinkPriceFeedClient>();
        services.AddScoped<IChainlinkFunctionsClient, ChainlinkFunctionsClient>();
        services.AddScoped<IChainlinkVrfClient, ChainlinkVrfClient>();
        services.AddScoped<IChainlinkAutomationClient, ChainlinkAutomationClient>();
        services.AddScoped<AutomationService>();
        services.AddScoped<IChainlinkCcipClient, ChainlinkCcipClient>();
        return services;
    }
}
