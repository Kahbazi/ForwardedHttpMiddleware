namespace AspNetCore.ForwardedHttpMiddleware
{
    public interface IForwardedHttpFeature
    {
        public NodeType ForType { get; }
       
        public string For { get; }
    }
}
