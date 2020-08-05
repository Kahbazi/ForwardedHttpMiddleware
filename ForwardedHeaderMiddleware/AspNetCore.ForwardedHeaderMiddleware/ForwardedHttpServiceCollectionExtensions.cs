using System;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCore.ForwardedHttpMiddleware
{
    public static class ForwardedHttpServiceCollectionExtensions
    {
        public static IServiceCollection AddForwardedHttp(this IServiceCollection services, Action<ForwardedHttpOptions> configure)
        {
            services.AddOptions<ForwardedHttpOptions>().Configure(configure);

            return services;
        }

        public static IServiceCollection AddForwardedHttp<TService>(this IServiceCollection services, Action<ForwardedHttpOptions, TService> configure) where TService : class
        {
            services.AddOptions<ForwardedHttpOptions>().Configure(configure);

            return services;
        }
    }
}
