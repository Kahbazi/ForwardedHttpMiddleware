using System.Net;

namespace AspNetCore.ForwardedHttp
{
    public interface IForwardedHttpFeature
    {
        public IPAddress OriginalRemoteIpAddress { get; }

        public int OriginalRemotePort { get; }

        public IPAddress OriginalLocalIpAddress { get; }

        public int OriginalLocalPort { get; }

        public string OriginalHost { get; }

        public string OriginalProto { get; }
    }
}
