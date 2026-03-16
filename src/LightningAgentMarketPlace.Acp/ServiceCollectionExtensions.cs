using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LightningAgentMarketPlace.Acp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Acp").Get<AcpSettings>() ?? new();

        services.AddHttpClient<IAcpClient, AcpClient>(client =>
        {
            if (!string.IsNullOrEmpty(settings.BaseUrl))
                client.BaseAddress = new Uri(settings.BaseUrl);
        })
        .AddStandardResilienceHandler();

        return services;
    }
}
