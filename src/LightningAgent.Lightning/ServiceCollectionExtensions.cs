using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;

namespace LightningAgent.Lightning;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLightningServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection("Lightning").Get<LightningSettings>() ?? new();

        services.AddTransient<LndMacaroonHandler>(sp => new LndMacaroonHandler(settings.MacaroonPath));

        services.AddHttpClient<ILightningClient, LndRestClient>(client =>
        {
            client.BaseAddress = new Uri(settings.LndRestUrl);
            client.Timeout = TimeSpan.FromSeconds(90); // Fix #6: explicit global timeout
        })
        .ConfigurePrimaryHttpMessageHandler(() => LndTlsCertHandler.CreateHttpClientHandler(settings.TlsCertPath))
        .AddHttpMessageHandler<LndMacaroonHandler>()
        .AddStandardResilienceHandler();

        return services;
    }
}
