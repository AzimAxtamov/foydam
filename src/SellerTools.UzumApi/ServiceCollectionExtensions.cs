using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SellerTools.UzumApi;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует клиент Uzum API по режиму из конфига (Mock | Real).
    /// </summary>
    public static IServiceCollection AddUzumApi(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<UzumApiOptions>(config.GetSection(UzumApiOptions.SectionName));

        var mode = config.GetSection(UzumApiOptions.SectionName)
            .GetValue<UzumApiMode>(nameof(UzumApiOptions.Mode));

        if (mode == UzumApiMode.Real)
        {
            services.AddHttpClient<IUzumApiClient, RealUzumApiClient>()
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var opt = sp.GetRequiredService<IOptions<UzumApiOptions>>().Value;
                    // PooledConnectionLifetime: не держим соединение в пуле бесконечно —
                    // иначе после разрыва keep-alive краем Uzum .NET переиспользует
                    // мёртвый сокет и падает HttpRequestException (реальный баг на Contabo).
                    return new SocketsHttpHandler
                    {
                        PooledConnectionLifetime =
                            TimeSpan.FromSeconds(Math.Max(1, opt.PooledConnectionLifetimeSeconds)),
                        ConnectTimeout = TimeSpan.FromSeconds(15),
                    };
                });
        }
        else
        {
            services.AddSingleton<IUzumApiClient, MockUzumApiClient>();
        }

        return services;
    }
}
