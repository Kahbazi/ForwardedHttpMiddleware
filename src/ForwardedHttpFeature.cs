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
    }
}
