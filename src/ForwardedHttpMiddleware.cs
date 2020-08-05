using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedHttpMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ForwardedHttpMiddleware> _logger;
        private readonly ForwardedHttpOptions _options;

        public ForwardedHttpMiddleware(RequestDelegate next, IOptions<ForwardedHttpOptions> options, ILogger<ForwardedHttpMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public Task Invoke(HttpContext context)
        {
            return _next(context);
        }
    }
}
