﻿using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedHttpOptions
    {
        /// <summary>
        /// Limits the number of entries in the headers that will be processed. The default value is 1.
        /// Set to null to disable the limit, but this should only be done if
        /// KnownProxies or KnownNetworks are configured.
        /// </summary>
        public int? ForwardLimit { get; set; } = 1;

        /// <summary>
        /// Addresses of known proxies to accept forwarded headers from.
        /// </summary>
        public IList<IPAddress> KnownProxies { get; } = new List<IPAddress>() { IPAddress.IPv6Loopback };

        /// <summary>
        /// Address ranges of known proxies to accept forwarded headers from.
        /// </summary>
        public IList<IPNetwork> KnownNetworks { get; } = new List<IPNetwork>() { new IPNetwork(IPAddress.Loopback, 8) };

        /// <summary>
        /// The allowed values from x-forwarded-host. If the list is empty then all hosts are allowed.
        /// Failing to restrict this these values may allow an attacker to spoof links generated by your service. 
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>Port numbers must be excluded.</description></item>
        /// <item><description>A top level wildcard "*" allows all non-empty hosts.</description></item>
        /// <item><description>Subdomain wildcards are permitted. E.g. "*.example.com" matches subdomains like foo.example.com,
        ///    but not the parent domain example.com.</description></item>
        /// <item><description>Unicode host names are allowed but will be converted to punycode for matching.</description></item>
        /// <item><description>IPv6 addresses must include their bounding brackets and be in their normalized form.</description></item>
        /// </list>
        /// </remarks>
        public IList<string> AllowedHosts { get; set; } = new List<string>();

        /// <summary>
        /// Identifies which forwarders should be processed.
        /// </summary>
        public ForwardedHttp ForwardedHttp { get; set; } = ForwardedHttp.All;
    }
}
