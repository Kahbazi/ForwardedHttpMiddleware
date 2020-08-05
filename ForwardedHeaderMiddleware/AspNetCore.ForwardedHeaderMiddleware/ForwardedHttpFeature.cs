namespace AspNetCore.ForwardedHttpMiddleware
{
    public class ForwardedHttpFeature : IForwardedHttpFeature
    {
        public ForwardedHttpFeature(NodeType forType, string @for, NodeType byType, string by)
        {
            ForType = forType;
            For = @for;
            ByType = byType;
            By = by;
        }

        public NodeType ForType { get; }

        public string For { get; }

        public NodeType ByType { get; }

        public string By { get; }
    }
}
