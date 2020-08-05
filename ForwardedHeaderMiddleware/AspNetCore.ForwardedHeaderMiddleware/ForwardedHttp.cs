using System;

namespace AspNetCore.ForwardedHttpMiddleware
{
    [Flags]
    public enum ForwardedHttp
    {
        None = 0,
        By = 1 << 0,
        For = 1 << 1,
        Proto = 1 << 2,
        Host = 1 << 3,
        All = By | For | Proto | Host
    }
}
