using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedHttpMiddleware
    {
        private const string ForwardedHeader = "Forwarded";
        private static readonly bool[] HostCharValidity = new bool[127];
        private static readonly bool[] SchemeCharValidity = new bool[123];
        private static readonly bool[] ObfuscatedCharValidity = new bool[123];

        private readonly RequestDelegate _next;
        private readonly ILogger<ForwardedHttpMiddleware> _logger;
        private readonly ForwardedHttpOptions _options;
        private bool _allowAllHosts;
        private IList<StringSegment> _allowedHosts;

        static ForwardedHttpMiddleware()
        {
            // RFC 7239 obfnode = 1*( ALPHA / DIGIT / "." / "_" / "-")
            ObfuscatedCharValidity['_'] = true;
            ObfuscatedCharValidity['-'] = true;
            ObfuscatedCharValidity['.'] = true;

            // RFC 3986 scheme = ALPHA * (ALPHA / DIGIT / "+" / "-" / ".")
            SchemeCharValidity['+'] = true;
            SchemeCharValidity['-'] = true;
            SchemeCharValidity['.'] = true;

            // Host Matches Http.Sys and Kestrel
            // Host Matches RFC 3986 except "*" / "+" / "," / ";" / "=" and "%" HEXDIG HEXDIG which are not allowed by Http.Sys
            HostCharValidity['!'] = true;
            HostCharValidity['$'] = true;
            HostCharValidity['&'] = true;
            HostCharValidity['\''] = true;
            HostCharValidity['('] = true;
            HostCharValidity[')'] = true;
            HostCharValidity['-'] = true;
            HostCharValidity['.'] = true;
            HostCharValidity['_'] = true;
            HostCharValidity['~'] = true;
            for (var ch = '0'; ch <= '9'; ch++)
            {
                SchemeCharValidity[ch] = true;
                HostCharValidity[ch] = true;
                ObfuscatedCharValidity[ch] = true;
            }
            for (var ch = 'A'; ch <= 'Z'; ch++)
            {
                SchemeCharValidity[ch] = true;
                HostCharValidity[ch] = true;
                ObfuscatedCharValidity[ch] = true;
            }
            for (var ch = 'a'; ch <= 'z'; ch++)
            {
                SchemeCharValidity[ch] = true;
                HostCharValidity[ch] = true;
                ObfuscatedCharValidity[ch] = true;
            }
        }

        public ForwardedHttpMiddleware(RequestDelegate next, IOptions<ForwardedHttpOptions> options, ILogger<ForwardedHttpMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            PreProcessHosts();
        }

        private void PreProcessHosts()
        {
            if (_options.AllowedHosts == null || _options.AllowedHosts.Count == 0)
            {
                _allowAllHosts = true;
                return;
            }

            var allowedHosts = new List<StringSegment>();
            foreach (var entry in _options.AllowedHosts)
            {
                // Punycode. Http.Sys requires you to register Unicode hosts, but the headers contain punycode.
                var host = new HostString(entry).ToUriComponent();

                if (IsTopLevelWildcard(host))
                {
                    // Disable filtering
                    _allowAllHosts = true;
                    return;
                }

                if (!allowedHosts.Contains(host, StringSegmentComparer.OrdinalIgnoreCase))
                {
                    allowedHosts.Add(host);
                }
            }

            _allowedHosts = allowedHosts;
        }

        private bool IsTopLevelWildcard(string host)
        {
            return (string.Equals("*", host, StringComparison.Ordinal) // HttpSys wildcard
                           || string.Equals("[::]", host, StringComparison.Ordinal) // Kestrel wildcard, IPv6 Any
                           || string.Equals("0.0.0.0", host, StringComparison.Ordinal)); // IPv4 Any
        }

        public Task Invoke(HttpContext context)
        {
            ApplyForwarders(context);
            return _next(context);
        }

        private void ApplyForwarders(HttpContext context)
        {
            StoreOriginalValues(context);

            var checkFor = _options.ForwardedHttp.HasFlag(ForwardedHttp.For);
            var checkBy = _options.ForwardedHttp.HasFlag(ForwardedHttp.By);
            var checkProto = _options.ForwardedHttp.HasFlag(ForwardedHttp.Proto);
            var checkHost = _options.ForwardedHttp.HasFlag(ForwardedHttp.Host);

            var request = context.Request;
            var forwarded = request.Headers[ForwardedHeader];
            var success = ParseProxyValues.TryParse(forwarded, out var forwardedHeader);
            if (!success)
            {
                _logger.LogWarning("Invalid forwarded header");
                return;
            }

            var entryCount = forwardedHeader.ForwardedValues.Count;

            // Apply ForwardLimit, if any
            if (_options.ForwardLimit.HasValue && entryCount > _options.ForwardLimit)
            {
                entryCount = _options.ForwardLimit.Value;
            }

            var sets = forwardedHeader.ForwardedValues
                .Select(x => new SetOfForwarders
                {
                    RemoteIpAndPortText = x.For,
                    LocalIpAndPortText = x.By,
                    Scheme = x.Proto,
                    Host = x.Host
                })
                .Reverse()
                .Take(entryCount)
                .ToArray();


            // Gather initial values
            var connection = context.Connection;
            var currentValues = new SetOfForwarders()
            {
                RemoteIp = connection.RemoteIpAddress,
                RemotePort = connection.RemotePort,
                LocalIp = connection.LocalIpAddress,
                LocalPort = connection.LocalPort,
                // Host and Scheme initial values are never inspected, no need to set them here.
            };

            var checkKnownIps = _options.KnownNetworks.Count > 0 || _options.KnownProxies.Count > 0;
            var applyChanges = false;

            var entriesConsumed = 0;
            for (; entriesConsumed < sets.Length; entriesConsumed++)
            {
                var set = sets[entriesConsumed];
                if (checkFor)
                {
                    // For the first instance, allow remoteIp to be null for servers that don't support it natively.
                    if (currentValues.RemoteIp != null && checkKnownIps && !CheckKnownAddress(currentValues.RemoteIp))
                    {
                        // Stop at the first unknown remote IP, but still apply changes processed so far.
                        _logger.LogDebug(1, "Unknown proxy: {RemoteIpAndPort}", currentValues.RemoteIp);
                        break;
                    }


                    var (ip, port) = GetIpPort(set.RemoteIpAndPortText);

                    IpType ipType;

                    if (IPAddress.TryParse(ip, out var parsedIp))
                    {
                        ipType = IpType.Valid;
                    }
                    else if (IsValidObfuscatedNode(ip))
                    {
                        ipType = IpType.Obfuscated;
                    }
                    else if (IsUnknownNode(ip))
                    {
                        ipType = IpType.Unknown;
                    }
                    else
                    {
                        // Stop at the first unparsable IP, but still apply changes processed so far.
                        _logger.LogDebug(1, "Unparsable IP: {IpAndPortText}", set.RemoteIpAndPortText);
                        break;
                    }

                    PortType portType = PortType.None;
                    int parsedPort = 0;
                    if (port != StringSegment.Empty)
                    {
                        if (int.TryParse(port, out parsedPort))
                        {
                            portType = PortType.Valid;
                        }
                        else if (IsValidObfuscatedNode(ip))
                        {
                            portType = PortType.Obfuscated;
                        }
                        else
                        {
                            // Stop at the first unparsable port, but still apply changes processed so far.
                            _logger.LogDebug(1, "Unparsable port: {IpAndPortText}", set.RemoteIpAndPortText);
                            break;
                        }
                    }

                    switch ((ipType, portType))
                    {
                        case (IpType.Valid, PortType.Valid):
                            applyChanges = true;

                            set.RemoteIp = parsedIp;
                            set.RemotePort = parsedPort;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.IpAndPort;
                            break;
                        case (IpType.Valid, PortType.None):
                            applyChanges = true;

                            set.RemoteIp = parsedIp;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.Ip;
                            break;
                        case (IpType.Valid, PortType.Obfuscated):
                            applyChanges = true;

                            set.RemoteIp = parsedIp;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.IpAndObfuscatedPort;
                            break;

                        case (IpType.Unknown, PortType.None):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.Unknown;
                            break;

                        case (IpType.Unknown, PortType.Obfuscated):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.UnknownAndObfuscatedPort;
                            break;

                        case (IpType.Unknown, PortType.Valid):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = parsedPort;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.UnknownAndPort;
                            break;

                        case (IpType.Obfuscated, PortType.None):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.Obfuscated;
                            break;

                        case (IpType.Obfuscated, PortType.Obfuscated):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = 0;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.ObfuscatedAndObfuscatedPort;
                            break;

                        case (IpType.Obfuscated, PortType.Valid):
                            applyChanges = true;

                            set.RemoteIp = null;
                            set.RemotePort = parsedPort;

                            currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                            currentValues.RemoteIp = set.RemoteIp;
                            currentValues.RemotePort = set.RemotePort;
                            currentValues.ForType = NodeType.ObfuscatedAndPort;
                            break;
                    }
                }

                if (checkBy)
                {
                    if (!StringSegment.IsNullOrEmpty(set.LocalIpAndPortText))
                    {
                        var (ip, port) = GetIpPort(set.LocalIpAndPortText);

                        IpType ipType;

                        if (IPAddress.TryParse(ip, out var parsedIp))
                        {
                            ipType = IpType.Valid;
                        }
                        else if (IsValidObfuscatedNode(ip))
                        {
                            ipType = IpType.Obfuscated;
                        }
                        else if (IsUnknownNode(ip))
                        {
                            ipType = IpType.Unknown;
                        }
                        else
                        {
                            // Stop at the first unparsable IP, but still apply changes processed so far.
                            _logger.LogDebug(1, "Unparsable IP: {IpAndPortText}", set.LocalIpAndPortText);
                            break;
                        }

                        PortType portType = PortType.None;
                        short parsedPort = 0;
                        if (port != StringSegment.Empty)
                        {
                            if (short.TryParse(port, out parsedPort))
                            {
                                portType = PortType.Valid;
                            }
                            else if (IsValidObfuscatedNode(ip))
                            {
                                portType = PortType.Obfuscated;
                            }
                            else
                            {
                                // Stop at the first unparsable port, but still apply changes processed so far.
                                _logger.LogDebug(1, "Unparsable port: {IpAndPortText}", set.LocalIpAndPortText);
                                break;
                            }
                        }

                        switch ((ipType, portType))
                        {
                            case (IpType.Valid, PortType.Valid):
                                applyChanges = true;

                                set.LocalIp = parsedIp;
                                set.LocalPort = parsedPort;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.IpAndPort;
                                break;
                            case (IpType.Valid, PortType.None):
                                applyChanges = true;

                                set.LocalIp = parsedIp;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.Ip;
                                break;
                            case (IpType.Valid, PortType.Obfuscated):
                                applyChanges = true;

                                set.LocalIp = parsedIp;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.IpAndObfuscatedPort;
                                break;

                            case (IpType.Unknown, PortType.None):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.Unknown;
                                break;

                            case (IpType.Unknown, PortType.Obfuscated):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.UnknownAndObfuscatedPort;
                                break;

                            case (IpType.Unknown, PortType.Valid):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = parsedPort;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.UnknownAndPort;
                                break;

                            case (IpType.Obfuscated, PortType.None):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.Obfuscated;
                                break;

                            case (IpType.Obfuscated, PortType.Obfuscated):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = 0;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.ObfuscatedAndObfuscatedPort;
                                break;

                            case (IpType.Obfuscated, PortType.Valid):
                                applyChanges = true;

                                set.LocalIp = null;
                                set.LocalPort = parsedPort;

                                currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                                currentValues.LocalIp = set.LocalIp;
                                currentValues.LocalPort = set.LocalPort;
                                currentValues.ForType = NodeType.ObfuscatedAndPort;
                                break;
                        }
                    }
                }

                if (checkProto)
                {
                    if (!StringSegment.IsNullOrEmpty(set.Scheme))
                    {
                        if (TryValidateScheme(set.Scheme))
                        {
                            applyChanges = true;
                            currentValues.Scheme = set.Scheme;
                        }
                        else
                        {
                            // Stop at the first invalid scheme, but still apply changes processed so far.
                            _logger.LogDebug(1, "invalid scheme: {scheme}", set.Scheme);
                            break;
                        }
                    }
                }

                if (checkHost)
                {
                    if (!StringSegment.IsNullOrEmpty(set.Host))
                    {
                        if (!TryValidateHost(set.Host))
                        {
                            // Stop at the first invalid host, but still apply changes processed so far.
                            _logger.LogDebug(1, "invalid host: {scheme}", set.Host);
                            break;
                        }
                        else
                        {
                            if (!_allowAllHosts && !HostString.MatchesAny(set.Host, _allowedHosts))
                            {
                                // Stop at the first invalid host, but still apply changes processed so far.
                                _logger.LogDebug(1, "host not allowed: {host}", set.Host);
                                break;
                            }
                            else
                            {
                                applyChanges = true;
                                currentValues.Host = set.Host;
                            }
                        }
                    }
                }

                if (applyChanges)
                {
                    if (checkFor && !StringSegment.IsNullOrEmpty(currentValues.RemoteIpAndPortText))
                    {
                        if (currentValues.ForType == NodeType.Ip
                            || currentValues.ForType == NodeType.IpAndObfuscatedPort
                            || currentValues.ForType == NodeType.IpAndPort)
                        {
                            connection.RemoteIpAddress = currentValues.RemoteIp;
                        }
                        else
                        {
                            connection.RemoteIpAddress = null;
                        }

                        if (currentValues.ForType == NodeType.UnknownAndPort
                            || currentValues.ForType == NodeType.ObfuscatedAndPort
                            || currentValues.ForType == NodeType.IpAndPort)
                        {
                            connection.RemotePort = currentValues.RemotePort;
                        }
                        else
                        {
                            connection.RemotePort = 0;
                        }
                    }

                    if (checkBy && !StringSegment.IsNullOrEmpty(currentValues.LocalIpAndPortText))
                    {
                        if (currentValues.ByType == NodeType.Ip
                            || currentValues.ByType == NodeType.IpAndObfuscatedPort
                            || currentValues.ByType == NodeType.IpAndPort)
                        {
                            connection.LocalIpAddress = currentValues.LocalIp;
                        }
                        else
                        {
                            connection.LocalIpAddress = null;
                        }

                        if (currentValues.ByType == NodeType.UnknownAndPort
                            || currentValues.ByType == NodeType.ObfuscatedAndPort
                            || currentValues.ByType == NodeType.IpAndPort)
                        {
                            connection.LocalPort = currentValues.LocalPort;
                        }
                        else
                        {
                            connection.LocalPort = 0;
                        }
                    }

                    if (checkProto && currentValues.Scheme != null)
                    {
                        request.Scheme = currentValues.Scheme.ToString();
                    }

                    if (checkHost && currentValues.Host != null)
                    {
                        request.Host = HostString.FromUriComponent(currentValues.Host.ToString());
                    }
                }
            }
        }

        private static void StoreOriginalValues(HttpContext context)
        {
            var connection = context.Connection;

            var feature = new ForwardedHttpFeature();
            if (connection.LocalIpAddress != null)
            {
                feature.OriginalLocalIpAddress = connection.LocalIpAddress;
            }

            feature.OriginalLocalPort = connection.LocalPort;

            if (connection.RemoteIpAddress != null)
            {
                feature.OriginalRemoteIpAddress = connection.RemoteIpAddress;
            }

            feature.OriginalRemotePort = connection.RemotePort;

            var request = context.Request;

            feature.OriginalProto = request.Scheme;

            feature.OriginalHost = request.Host.Value;

            context.Features.Set<IForwardedHttpFeature>(feature);
        }

        private static (StringSegment ip, StringSegment port) GetIpPort(StringSegment ipAndPortText)
        {
            StringSegment ip;
            StringSegment port;


            if (ipAndPortText.StartsWith("[", StringComparison.OrdinalIgnoreCase))
            {
                // ipv6 

                if (ipAndPortText.EndsWith("]", StringComparison.OrdinalIgnoreCase))
                {
                    // ipv6 without port
                    // [2001:db8:cafe::17]

                    ip = ipAndPortText;
                    port = StringSegment.Empty;
                }
                else
                {
                    // ipv6 with port
                    // [2001:db8:cafe::17]:12345

                    var index = ipAndPortText.IndexOf(']');
                    ip = ipAndPortText.Subsegment(1, index - 1);
                    port = ipAndPortText.Subsegment(index + 2);
                }
            }
            else
            {
                // ipv4 

                var colonIndex = ipAndPortText.IndexOf(':');
                var hasPort = colonIndex > 0;
                if (hasPort)
                {
                    // ipv4 with port
                    // 192.0.2.43:47011

                    ip = ipAndPortText.Subsegment(0, colonIndex);
                    port = ipAndPortText.Subsegment(colonIndex + 1);
                }
                else
                {
                    // ipv4 without port
                    // 192.0.2.43

                    ip = ipAndPortText;
                    port = StringSegment.Empty;
                }
            }

            return (ip, port);
        }

        private bool IsValidObfuscatedNode(StringSegment ipAndPortText)
        {
            if (ipAndPortText[0] != '_')
            {
                return false;
            }

            for (var i = 1; i < ipAndPortText.Length; i++)
            {
                if (!IsValidObfuscatedChar(ipAndPortText[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsUnknownNode(StringSegment remoteIpAndPortText)
        {
            // Need any string comparision?
            return remoteIpAndPortText.Equals("unknown");
        }

        // Empty was checked for by the caller
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateScheme(StringSegment scheme)
        {
            for (var i = 0; i < scheme.Length; i++)
            {
                if (!IsValidSchemeChar(scheme[i]))
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidSchemeChar(char ch)
        {
            return ch < SchemeCharValidity.Length && SchemeCharValidity[ch];
        }

        // Empty was checked for by the caller
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateHost(StringSegment host)
        {
            if (host[0] == '[')
            {
                return TryValidateIPv6Host(host);
            }

            if (host[0] == ':')
            {
                // Only a port
                return false;
            }

            var i = 0;
            for (; i < host.Length; i++)
            {
                if (!IsValidHostChar(host[i]))
                {
                    break;
                }
            }
            return TryValidateHostPort(host, i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidHostChar(char ch)
        {
            return ch < HostCharValidity.Length && HostCharValidity[ch];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidObfuscatedChar(char ch)
        {
            return ch < ObfuscatedCharValidity.Length && ObfuscatedCharValidity[ch];
        }

        // The lead '[' was already checked
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateIPv6Host(StringSegment hostText)
        {
            for (var i = 1; i < hostText.Length; i++)
            {
                var ch = hostText[i];
                if (ch == ']')
                {
                    // [::1] is the shortest valid IPv6 host
                    if (i < 4)
                    {
                        return false;
                    }
                    return TryValidateHostPort(hostText, i + 1);
                }

                if (!IsHex(ch) && ch != ':' && ch != '.')
                {
                    return false;
                }
            }

            // Must contain a ']'
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateHostPort(StringSegment hostText, int offset)
        {
            if (offset == hostText.Length)
            {
                // No port
                return true;
            }

            if (hostText[offset] != ':' || hostText.Length == offset + 1)
            {
                // Must have at least one number after the colon if present.
                return false;
            }

            for (var i = offset + 1; i < hostText.Length; i++)
            {
                if (!IsNumeric(hostText[i]))
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNumeric(char ch)
        {
            return '0' <= ch && ch <= '9';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsHex(char ch)
        {
            return IsNumeric(ch)
                || ('a' <= ch && ch <= 'f')
                || ('A' <= ch && ch <= 'F');
        }

        private bool CheckKnownAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                var ipv4Address = address.MapToIPv4();
                if (CheckKnownAddress(ipv4Address))
                {
                    return true;
                }
            }
            if (_options.KnownProxies.Contains(address))
            {
                return true;
            }
            foreach (var network in _options.KnownNetworks)
            {
                if (network.Contains(address))
                {
                    return true;
                }
            }
            return false;
        }

        private struct SetOfForwarders
        {
            public NodeType ForType;
            public StringSegment RemoteIpAndPortText;
            public IPAddress RemoteIp;
            public int RemotePort;

            public NodeType ByType;
            public StringSegment LocalIpAndPortText;
            public IPAddress LocalIp;
            public int LocalPort;

            public StringSegment Host;
            public StringSegment Scheme;
        }
    }
}
