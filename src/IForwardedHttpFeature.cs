using System.Net;

namespace AspNetCore.ForwardedHttp
{
    public interface IForwardedHttpFeature
    {
        IPAddress OriginalRemoteIpAddress { get; set; }
        
        int OriginalRemotePort { get; set; }

        IPAddress OriginalBy { get; set; }
        
        string OriginalHost { get; set; }
        
        string OriginalProto { get; set; }

        NodeType ForType { get; }
       
        string For { get; }

        NodeType ByType { get; }

        string By { get; }

        string Host { get; }
        
        string Proto { get; }
    }
}
