using System.Net;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedHttpFeature : IForwardedHttpFeature
    {
        public IPAddress OriginalRemoteIpAddress { get; set; }

        public int OriginalRemotePort { get; set; }

        public IPAddress OriginalLocalIpAddress  { get; set; }
        
        public int OriginalLocalPort { get; set; }

        public string OriginalHost { get; set; }

        public string OriginalProto { get; set; }
    }
}
