using System;
using Microsoft.AspNetCore.Builder;

namespace AspNetCore.ForwardedHttpMiddleware
{
    public static class ForwardedHttpExtensions
    {
        private const string ForwardedHttpAdded = "ForwardedHttpAdded";

        public static IApplicationBuilder UseForwardedHttp(this IApplicationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Don't add more than one instance of this middleware to the pipeline using the options from the DI container.
            // Doing so could cause a request to be processed multiple times and the ForwardLimit to be exceeded.
            if (!builder.Properties.ContainsKey(ForwardedHttpAdded))
            {
                builder.Properties[ForwardedHttpAdded] = true;
                return builder.UseMiddleware<ForwardedHttpMiddleware>();
            }

            return builder;
        }
    }
}
