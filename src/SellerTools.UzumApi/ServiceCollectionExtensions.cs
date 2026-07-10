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
            services.AddHttpClient<IUzumApiClient, RealUzumApiClient>((sp, http) =>
                {
                    var opt = sp.GetRequiredService<IOptions<UzumApiOptions>>().Value;
                    // Общий таймаут запроса (на попытку; ретраи внутри клиента). 0 → без лимита.
                    http.Timeout = opt.RequestTimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(opt.RequestTimeoutSeconds)
                        : Timeout.InfiniteTimeSpan;
                })
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
                        ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, opt.ConnectTimeoutSeconds)),
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
