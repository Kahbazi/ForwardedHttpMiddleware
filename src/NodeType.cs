namespace AspNetCore.ForwardedHttp
{
    public enum NodeType
    {
        None,
        Obfuscated,
        ObfuscatedAndPort,
        ObfuscatedAndObfuscatedPort,
        Unknown, 
        UnknownAndPort,
        UnknownAndObfuscatedPort,
        Ip, 
        IpAndPort,
        IpAndObfuscatedPort,
    }

    internal enum IpType
    {
        Valid,
        Obfuscated,
        Unknown,
    }

    internal enum PortType
    {
        None,
        Valid,
        Obfuscated,
    }
}
