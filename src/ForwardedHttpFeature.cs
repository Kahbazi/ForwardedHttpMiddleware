using System.Net;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedHttpFeature : IForwardedHttpFeature
    {
        public NodeType ForType { get; set; }

        public string For { get; set; }
        
        public NodeType ByType { get; set; }
        
        public string By { get; set; }
        
        public string Host { get; set; }
        
        public string Proto { get; set; }

        public IPAddress OriginalRemoteIpAddress { get; set; }

        public int OriginalRemotePort { get; set; }

        public IPAddress OriginalBy { get; set; }
        
        public int OriginalByPort { get; set; }

        public string OriginalHost { get; set; }

        public string OriginalProto { get; set; }
    }
}
