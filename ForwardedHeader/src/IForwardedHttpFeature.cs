namespace AspNetCore.ForwardedHttp
{
    public interface IForwardedHttpFeature
    {
        public NodeType ForType { get; }
       
        public string For { get; }

        public NodeType ByType { get; }

        public string By { get; }
    }
}
