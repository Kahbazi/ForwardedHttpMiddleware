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
        private static readonly bool[] HostCharValidity = new bool[127];
        private static readonly bool[] SchemeCharValidity = new bool[123];
        private static readonly bool[] NodeCharValidity = new bool[123];

        private readonly RequestDelegate _next;
        private readonly ILogger<ForwardedHttpMiddleware> _logger;
        private readonly ForwardedHttpOptions _options;
        private bool _allowAllHosts;
        private IList<StringSegment> _allowedHosts;

        static ForwardedHttpMiddleware()
        {
            // RFC 7239 obfnode = 1*( ALPHA / DIGIT / "." / "_" / "-")
            NodeCharValidity['_'] = true;
            NodeCharValidity['-'] = true;
            NodeCharValidity['.'] = true;

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
            }
            for (var ch = 'A'; ch <= 'Z'; ch++)
            {
                SchemeCharValidity[ch] = true;
                HostCharValidity[ch] = true;
            }
            for (var ch = 'a'; ch <= 'z'; ch++)
            {
                SchemeCharValidity[ch] = true;
                HostCharValidity[ch] = true;
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
            // Gather expected headers.
            List<string> forwardedFor = null, forwardedProto = null, forwardedHost = null, forwardedBy = null;
            bool checkFor = false, checkProto = false, checkHost = false, checkBy = false;
            int entryCount = 0;

            var forwarded = context.Request.Headers["Forwarded"];
            var forwardedHeader = new ForwardedHeader(forwarded);

            var request = context.Request;
            var requestHeaders = context.Request.Headers;
            if ((_options.ForwardedHttp & ForwardedHttp.For) == ForwardedHttp.For)
            {
                checkFor = true;
                forwardedFor = forwardedHeader.For;
                entryCount = Math.Max(forwardedFor.Count, entryCount);
            }

            if ((_options.ForwardedHttp & ForwardedHttp.Proto) == ForwardedHttp.Proto)
            {
                checkProto = true;
                forwardedProto = forwardedHeader.Proto;
                if (_options.RequireHeaderSymmetry && checkFor && forwardedFor.Count != forwardedProto.Count)
                {
                    _logger.LogWarning(1, "Parameter count mismatch between X-Forwarded-For and X-Forwarded-Proto.");
                    return;
                }
                entryCount = Math.Max(forwardedProto.Count, entryCount);
            }

            if ((_options.ForwardedHttp & ForwardedHttp.Host) == ForwardedHttp.Host)
            {
                checkHost = true;
                forwardedHost = forwardedHeader.Host;
                if (_options.RequireHeaderSymmetry
                    && ((checkFor && forwardedFor.Count != forwardedHost.Count)
                        || (checkProto && forwardedProto.Count != forwardedHost.Count)))
                {
                    _logger.LogWarning(1, "Parameter count mismatch between X-Forwarded-Host and X-Forwarded-For or X-Forwarded-Proto.");
                    return;
                }
                entryCount = Math.Max(forwardedHost.Count, entryCount);
            }

            if ((_options.ForwardedHttp & ForwardedHttp.By) == ForwardedHttp.By)
            {
                checkBy = true;
                forwardedBy = forwardedHeader.By;
                if (_options.RequireHeaderSymmetry
                    && ((checkFor && forwardedFor.Count != forwardedBy.Count)
                        || (checkProto && forwardedProto.Count != forwardedBy.Count)
                        || (checkHost && forwardedHost.Count != forwardedBy.Count)))
                {
                    _logger.LogWarning(1, "Parameter count mismatch between X-Forwarded-Host and X-Forwarded-For or X-Forwarded-Proto.");
                    return;
                }
                entryCount = Math.Max(forwardedBy.Count, entryCount);
            }

            // Apply ForwardLimit, if any
            if (_options.ForwardLimit.HasValue && entryCount > _options.ForwardLimit)
            {
                entryCount = _options.ForwardLimit.Value;
            }

            // Group the data together.
            var sets = new SetOfForwarders[entryCount];
            for (int i = 0; i < sets.Length; i++)
            {
                // They get processed in reverse order, right to left.
                var set = new SetOfForwarders();
                if (checkFor && i < forwardedFor.Count)
                {
                    set.RemoteIpAndPortText = forwardedFor[forwardedFor.Count - i - 1];
                }
                if (checkProto && i < forwardedProto.Count)
                {
                    set.Scheme = forwardedProto[forwardedProto.Count - i - 1];
                }
                if (checkHost && i < forwardedHost.Count)
                {
                    set.Host = forwardedHost[forwardedHost.Count - i - 1];
                }
                if (checkBy && i < forwardedBy.Count)
                {
                    set.LocalIpAndPortText = forwardedBy[forwardedBy.Count - i - 1];
                }
                sets[i] = set;
            }

            // Gather initial values
            var connection = context.Connection;
            var currentValues = new SetOfForwarders()
            {
                RemoteIpAndPort = connection.RemoteIpAddress != null ? new IPEndPoint(connection.RemoteIpAddress, connection.RemotePort) : null,
                // Host and Scheme initial values are never inspected, no need to set them here.
            };

            var checkKnownIps = _options.KnownNetworks.Count > 0 || _options.KnownProxies.Count > 0;
            bool applyChanges = false;
            int entriesConsumed = 0;

            for (; entriesConsumed < sets.Length; entriesConsumed++)
            {
                var set = sets[entriesConsumed];
                if (checkFor)
                {
                    // For the first instance, allow remoteIp to be null for servers that don't support it natively.
                    if (currentValues.RemoteIpAndPort != null && checkKnownIps && !CheckKnownAddress(currentValues.RemoteIpAndPort.Address))
                    {
                        // Stop at the first unknown remote IP, but still apply changes processed so far.
                        _logger.LogDebug(1, "Unknown proxy: {RemoteIpAndPort}", currentValues.RemoteIpAndPort);
                        break;
                    }

                    if (IPEndPoint.TryParse(set.RemoteIpAndPortText, out var parsedEndPoint))
                    {
                        applyChanges = true;
                        set.RemoteIpAndPort = parsedEndPoint;
                        currentValues.RemoteIpAndPortText = set.RemoteIpAndPortText;
                        currentValues.RemoteIpAndPort = set.RemoteIpAndPort;
                    }
                    else if (!string.IsNullOrEmpty(set.RemoteIpAndPortText))
                    {
                        // Stop at the first unparsable IP, but still apply changes processed so far.
                        _logger.LogDebug(1, "Unparsable IP: {IpAndPortText}", set.RemoteIpAndPortText);
                        break;
                    }
                    else if (_options.RequireHeaderSymmetry)
                    {
                        _logger.LogWarning(2, "Missing forwarded IPAddress.");
                        return;
                    }
                }

                if (checkProto)
                {
                    if (!string.IsNullOrEmpty(set.Scheme) && TryValidateScheme(set.Scheme))
                    {
                        applyChanges = true;
                        currentValues.Scheme = set.Scheme;
                    }
                    else if (_options.RequireHeaderSymmetry)
                    {
                        _logger.LogWarning(3, $"Forwarded scheme is not present, this is required by {nameof(_options.RequireHeaderSymmetry)}");
                        return;
                    }
                }

                if (checkHost)
                {
                    if (!string.IsNullOrEmpty(set.Host) && TryValidateHost(set.Host)
                        && (_allowAllHosts || HostString.MatchesAny(set.Host, _allowedHosts)))
                    {
                        applyChanges = true;
                        currentValues.Host = set.Host;
                    }
                    else if (_options.RequireHeaderSymmetry)
                    {
                        _logger.LogWarning(4, $"Incorrect number of x-forwarded-host header values, see {nameof(_options.RequireHeaderSymmetry)}.");
                        return;
                    }
                }

                if (checkBy)
                {
                    if (IPEndPoint.TryParse(set.LocalIpAndPortText, out var parsedEndPoint))
                    {
                        applyChanges = true;
                        set.LocalIpAndPort = parsedEndPoint;
                        currentValues.LocalIpAndPortText = set.LocalIpAndPortText;
                        currentValues.LocalIpAndPort = set.LocalIpAndPort;
                    }
                    else if (!string.IsNullOrEmpty(set.LocalIpAndPortText))
                    {
                        // Stop at the first unparsable IP, but still apply changes processed so far.
                        _logger.LogDebug(1, "Unparsable IP: {IpAndPortText}", set.LocalIpAndPortText);
                        break;
                    }
                    else if (_options.RequireHeaderSymmetry)
                    {
                        _logger.LogWarning(2, "Missing forwarded IPAddress.");
                        return;
                    }
                }
            }

            if (applyChanges)
            {
                var feature = new ForwardedHttpFeature();

                if (checkFor && currentValues.RemoteIpAndPort != null)
                {
                    if (connection.RemoteIpAddress != null)
                    {
                        // Save the original
                        feature.For = new IPEndPoint(connection.RemoteIpAddress, connection.RemotePort).ToString();
                        feature.ForType = NodeType.IP;
                    }
                    if (forwardedFor.Count > entriesConsumed)
                    {
                        // Truncate the consumed header values
                        forwardedHeader.For = forwardedFor.Take(forwardedFor.Count - entriesConsumed).ToList();
                    }
                    else
                    {
                        // All values were consumed
                        forwardedHeader.For.Clear();
                    }
                    connection.RemoteIpAddress = currentValues.RemoteIpAndPort.Address;
                    connection.RemotePort = currentValues.RemoteIpAndPort.Port;
                }

                if (checkBy && currentValues.LocalIpAndPort != null)
                {
                    if (connection.LocalIpAddress != null)
                    {
                        // Save the original
                        feature.By = new IPEndPoint(connection.LocalIpAddress, connection.LocalPort).ToString();
                        feature.ByType = NodeType.IP;
                    }
                    if (forwardedBy.Count > entriesConsumed)
                    {
                        // Truncate the consumed header values
                        forwardedHeader.By = forwardedBy.Take(forwardedBy.Count - entriesConsumed).ToList();
                    }
                    else
                    {
                        // All values were consumed
                        forwardedHeader.By.Clear();
                    }
                    connection.LocalIpAddress = currentValues.LocalIpAndPort.Address;
                    connection.LocalPort = currentValues.LocalIpAndPort.Port;
                }

                if (checkProto && currentValues.Scheme != null)
                {
                    // Save the original
                    feature.Proto = request.Scheme;

                    if (forwardedProto.Count > entriesConsumed)
                    {
                        // Truncate the consumed header values
                        forwardedHeader.Proto = forwardedProto.Take(forwardedProto.Count - entriesConsumed).ToList();
                    }
                    else
                    {
                        // All values were consumed
                        forwardedHeader.Proto.Clear();
                    }
                    request.Scheme = currentValues.Scheme;
                }

                if (checkHost && currentValues.Host != null)
                {
                    // Save the original
                    feature.Host = request.Host.ToString();

                    if (forwardedHost.Count > entriesConsumed)
                    {
                        // Truncate the consumed header values
                        forwardedHeader.Host = forwardedHost.Take(forwardedHost.Count - entriesConsumed).ToList();
                    }
                    else
                    {
                        // All values were consumed
                        forwardedHeader.Host.Clear();
                    }
                    request.Host = HostString.FromUriComponent(currentValues.Host);
                }

                context.Features.Set<IForwardedHttpFeature>(feature);

                request.Headers.Remove("Forwarded");
                var value = forwardedHeader.Value;
                if (value != StringValues.Empty)
                {
                    request.Headers["Forwarded"] = forwardedHeader.Value;
                }
            }
        }

        // Empty was checked for by the caller
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateScheme(string scheme)
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
        private bool TryValidateHost(string host)
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

        // The lead '[' was already checked
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryValidateIPv6Host(string hostText)
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
        private bool TryValidateHostPort(string hostText, int offset)
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
            public string RemoteIpAndPortText;
            public IPEndPoint RemoteIpAndPort;

            public string LocalIpAndPortText;
            public IPEndPoint LocalIpAndPort;

            public string Host;
            public string Scheme;
        }
    }
}
