namespace AspNetCore.ForwardedHttpMiddleware
{
    public class ForwardedHttpFeature : IForwardedHttpFeature
    {
        public ForwardedHttpFeature(NodeType forType, string @for)
        {
            ForType = forType;
            For = @for;
        }

        public NodeType ForType { get; }

        public string For { get; }
    }
}
