using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AspNetCore.ForwardedHttp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace AspNetCore.ForwardedHttp.Tests
{
    public class ForwardedHeadersMiddlewareTests
    {
        [Fact]
        public async Task ForwardedForDefaultSettingsChangeRemoteIpAndPort()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "For=11.111.111.11:9090";
            });

            Assert.Equal("11.111.111.11", context.Connection.RemoteIpAddress.ToString());
            Assert.Equal(9090, context.Connection.RemotePort);
            // No Original set if RemoteIpAddress started null.
            var feature = context.Features.Get<IForwardedHttpFeature>();
            Assert.NotNull(feature);
            Assert.Null(feature.OriginalRemoteIpAddress);
        }

        [Theory]
        [InlineData(1, "For=11.111.111.11.12345", "10.0.0.1", 99)] // Invalid
        public async Task ForwardedForFirstValueIsInvalid(int limit, string forwardedHeader, string expectedIp, int expectedPort)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For,
                            ForwardLimit = limit,
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = forwardedHeader;
                c.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
                c.Connection.RemotePort = 99;
            });

            Assert.Equal(expectedIp, context.Connection.RemoteIpAddress.ToString());
            Assert.Equal(expectedPort, context.Connection.RemotePort);
            Assert.True(context.Request.Headers.ContainsKey("Forwarded"));
            Assert.Equal(forwardedHeader, context.Request.Headers["Forwarded"]);
        }

        [Theory]
        [InlineData(1, "For=11.111.111.11:12345", "11.111.111.11", 12345)]
        [InlineData(10, "For=11.111.111.11:12345", "11.111.111.11", 12345)]
        [InlineData(1, "For=12.112.112.12:23456, For=11.111.111.11:12345", "11.111.111.11", 12345)]
        [InlineData(2, "For=12.112.112.12:23456, For=11.111.111.11:12345", "12.112.112.12", 23456)]
        [InlineData(10, "For=12.112.112.12:23456, For=11.111.111.11:12345", "12.112.112.12", 23456)]
        [InlineData(10, "For=12.112.112.12.23456, For=11.111.111.11:12345", "11.111.111.11", 12345)] // Invalid 2nd value
        [InlineData(10, "For=13.113.113.13:34567, For=12.112.112.12.23456, For=11.111.111.11:12345", "11.111.111.11", 12345)] // Invalid 2nd value
        [InlineData(2, "For=13.113.113.13:34567, For=12.112.112.12:23456, For=11.111.111.11:12345", "12.112.112.12", 23456)]
        [InlineData(3, "For=13.113.113.13:34567, For=12.112.112.12:23456, For=11.111.111.11:12345", "13.113.113.13", 34567)]
        public async Task ForwardedForForwardLimit(int limit, string forwardedHeader, string expectedIp, int expectedPort)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        var options = new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For,
                            ForwardLimit = limit,
                        };
                        options.KnownProxies.Clear();
                        options.KnownNetworks.Clear();
                        app.UseForwardedHttp(options);
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = forwardedHeader;
                c.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
                c.Connection.RemotePort = 99;
            });

            var feature = context.Features.Get<IForwardedHttpFeature>();
            Assert.NotNull(feature);
            Assert.Equal(IPAddress.Parse("10.0.0.1"), feature.OriginalRemoteIpAddress);
            Assert.Equal(99, feature.OriginalRemotePort);
            Assert.Equal(expectedIp, context.Connection.RemoteIpAddress.ToString());
            Assert.Equal(expectedPort, context.Connection.RemotePort);
        }

        [Theory]
        [InlineData("11.111.111.11", false)]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.0.1.1", true)]
        [InlineData("::1", true)]
        [InlineData("::", false)]
        public async Task ForwardedForLoopback(string originalIp, bool expectForwarded)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For,
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "For=10.0.0.1:1234";
                c.Connection.RemoteIpAddress = IPAddress.Parse(originalIp);
                c.Connection.RemotePort = 99;
            });

            if (expectForwarded)
            {
                Assert.Equal("10.0.0.1", context.Connection.RemoteIpAddress.ToString());
                Assert.Equal(1234, context.Connection.RemotePort);
            }
            else
            {
                Assert.Equal(originalIp, context.Connection.RemoteIpAddress.ToString());
                Assert.Equal(99, context.Connection.RemotePort);
            }
        }

        [Theory]
        [InlineData(1, "For=11.111.111.11:12345", "20.0.0.1", "10.0.0.1", 99)]
        [InlineData(1, "", "10.0.0.1", "10.0.0.1", 99)]
        [InlineData(1, "For=11.111.111.11:12345", "10.0.0.1", "11.111.111.11", 12345)]
        [InlineData(1, "For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1", "11.111.111.11", 12345)]
        [InlineData(1, "For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11", "11.111.111.11", 12345)]
        [InlineData(2, "For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11", "12.112.112.12", 23456)]
        [InlineData(1, "For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11,12.112.112.12", "11.111.111.11", 12345)]
        [InlineData(2, "For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11,12.112.112.12", "12.112.112.12", 23456)]
        [InlineData(3, "For=13.113.113.13:34567, For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11,12.112.112.12", "13.113.113.13", 34567)]
        [InlineData(3, "For=13.113.113.13:34567, For=12.112.112.12.23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11,12.112.112.12", "11.111.111.11", 12345)] // Invalid 2nd IP
        [InlineData(3, "For=13.113.113.13.34567, For=12.112.112.12:23456, For=11.111.111.11:12345", "10.0.0.1,11.111.111.11,12.112.112.12", "12.112.112.12", 23456)] // Invalid 3rd IP
        public async Task ForwardedForForwardKnownIps(int limit, string forwardedHeader, string knownIPs, string expectedIp, int expectedPort)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        var options = new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For,
                            ForwardLimit = limit,
                        };
                        foreach (var ip in knownIPs.Split(',').Select(text => IPAddress.Parse(text)))
                        {
                            options.KnownProxies.Add(ip);
                        }
                        app.UseForwardedHttp(options);
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = forwardedHeader;
                c.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
                c.Connection.RemotePort = 99;
            });

            Assert.Equal(expectedIp, context.Connection.RemoteIpAddress.ToString());
            Assert.Equal(expectedPort, context.Connection.RemotePort);
        }

        [Fact]
        public async Task ForwardedForOverrideBadIpDoesntChangeRemoteIp()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "For=BAD-IP";
            });

            Assert.Null(context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task ForwardedHostOverrideChangesRequestHost()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Host"] = "originalhost";
                c.Request.Headers["Forwarded"] = "Host=testhost";
            });

            Assert.Equal("testhost", context.Request.Host.ToString());
            var feature = context.Features.Get<IForwardedHttpFeature>();
            Assert.NotNull(feature);
            Assert.Equal("originalhost", feature.OriginalHost);
        }

        public static TheoryData<string> HostHeaderData
        {
            get
            {
                return new TheoryData<string>() {
                    "z",
                    "1",
                    "y:1",
                    "1:1",
                    "[ABCdef]",
                    "[abcDEF]:0",
                    "[abcdef:127.2355.1246.114]:0",
                    "[::1]:80",
                    "127.0.0.1:80",
                    "900.900.900.900:9523547852",
                    "foo",
                    "foo:234",
                    "foo.bar.baz",
                    "foo.BAR.baz:46245",
                    "foo.ba-ar.baz:46245",
                    "-foo:1234",
                    "xn--c1yn36f:134",
                    "-",
                    "_",
                    "~",
                    "!",
                    "$",
                    "'",
                    "(",
                    ")",
                };
            }
        }

        [Theory]
        [MemberData(nameof(HostHeaderData))]
        public async Task ForwardedHostAllowsValidCharacters(string hostHeader)
        {
            var assertsExecuted = false;

            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host
                        });
                        app.Run(context =>
                        {
                            Assert.Equal(hostHeader, context.Request.Host.ToString());
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Host=" + hostHeader;
            });
            Assert.True(assertsExecuted);
        }

        public static TheoryData<string> HostHeaderInvalidData
        {
            get
            {
                // see https://tools.ietf.org/html/rfc7230#section-5.4
                var data = new TheoryData<string>() {
                    "", // Empty
                    "[]", // Too short
                    "[::]", // Too short
                    "[ghijkl]", // Non-hex
                    "[afd:adf:123", // Incomplete
                    "[afd:adf]123", // Missing :
                    "[afd:adf]:", // Missing port digits
                    "[afd adf]", // Space
                    "[ad-314]", // dash
                    ":1234", // Missing host
                    "a:b:c", // Missing []
                    "::1", // Missing []
                    "::", // Missing everything
                    "abcd:1abcd", // Letters in port
                    "abcd:1.2", // Dot in port
                    "1.2.3.4:", // Missing port digits
                    "1.2 .4", // Space
                };

                // These aren't allowed anywhere in the host header
                var invalid = "\"#%*+/;<=>?@[]\\^`{}|";
                foreach (var ch in invalid)
                {
                    data.Add(ch.ToString());
                }

                invalid = "!\"#$%&'()*+,/;<=>?@[]\\^_`{}|~-";
                foreach (var ch in invalid)
                {
                    data.Add("[abd" + ch + "]:1234");
                }

                invalid = "!\"#$%&'()*+/;<=>?@[]\\^_`{}|~:abcABC-.";
                foreach (var ch in invalid)
                {
                    data.Add("a.b.c:" + ch);
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(HostHeaderInvalidData))]
        public async Task ForwardedHostFailsForInvalidCharacters(string hostHeader)
        {
            var assertsExecuted = false;

            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host
                        });
                        app.Run(context =>
                        {
                            Assert.NotEqual(hostHeader, context.Request.Host.Value);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Host=" + hostHeader;
            });
            Assert.True(assertsExecuted);
        }

        [Theory]
        [InlineData("localHost", "localhost")]
        [InlineData("localHost", "*")] // Any - Used by HttpSys
        [InlineData("localHost", "[::]")] // IPv6 Any - This is what Kestrel reports when binding to *
        [InlineData("localHost", "0.0.0.0")] // IPv4 Any
        [InlineData("localhost:9090", "example.com;localHost")]
        [InlineData("example.com:443", "example.com;localhost")]
        [InlineData("localHost:80", "localhost;")]
        [InlineData("foo.eXample.com:443", "*.exampLe.com")]
        [InlineData("f.eXample.com:443", "*.exampLe.com")]
        [InlineData("127.0.0.1", "127.0.0.1")]
        [InlineData("127.0.0.1:443", "127.0.0.1")]
        [InlineData("xn--c1yn36f:443", "xn--c1yn36f")]
        [InlineData("xn--c1yn36f:443", "點看")]
        [InlineData("[::ABC]", "[::aBc]")]
        [InlineData("[::1]:80", "[::1]")]
        public async Task ForwardedHostAllowsSpecifiedHost(string hostHeader, string allowedHost)
        {
            bool assertsExecuted = false;
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host,
                            AllowedHosts = allowedHost.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        });
                        app.Run(context =>
                        {
                            Assert.Equal(hostHeader, context.Request.Headers[HeaderNames.Host]);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();
            var response = await server.SendAsync(ctx =>
            {
                ctx.Request.Headers["forwarded"] = "Host=" + hostHeader;
            });
            Assert.True(assertsExecuted);
        }

        [Theory]
        [InlineData("example.com", "localhost")]
        [InlineData("localhost:9090", "example.com;")]
        [InlineData(";", "example.com;localhost")]
        [InlineData(";:80", "example.com;localhost")]
        [InlineData(":80", "localhost")]
        [InlineData(":", "localhost")]
        [InlineData("example.com:443", "*.example.com")]
        [InlineData(".example.com:443", "*.example.com")]
        [InlineData("foo.com:443", "*.example.com")]
        [InlineData("foo.example.com.bar:443", "*.example.com")]
        [InlineData(".com:443", "*.com")]
        // Unicode in the host shouldn't be allowed without punycode anyways. This match fails because the middleware converts
        // its input to punycode.
        [InlineData("點看", "點看")]
        [InlineData("[::1", "[::1]")]
        [InlineData("[::1:80", "[::1]")]
        public async Task ForwardedHostFailsMismatchedHosts(string hostHeader, string allowedHost)
        {
            bool assertsExecuted = false;
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host,
                            AllowedHosts = new[] { allowedHost }
                        });
                        app.Run(context =>
                        {
                            Assert.NotEqual<string>(hostHeader, context.Request.Headers[HeaderNames.Host]);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();
            var response = await server.SendAsync(ctx =>
            {
                ctx.Request.Headers["forwarded"] = "Host=" + hostHeader;
            });
            Assert.True(assertsExecuted);
        }

        [Fact]
        public async Task ForwardedHostStopsAtFirstUnspecifiedHost()
        {
            bool assertsExecuted = false;
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Host,
                            ForwardLimit = 10,
                            AllowedHosts = new[] { "bar.com", "*.foo.com" }
                        });
                        app.Run(context =>
                        {
                            Assert.Equal("bar.foo.com:432", context.Request.Headers[HeaderNames.Host]);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();
            var response = await server.SendAsync(ctx =>
            {
                ctx.Request.Headers["forwarded"] = "Host=stuff:523, Host=bar.foo.com:432, Host=bar.com:80";
            });
            Assert.True(assertsExecuted);
        }

        [Theory]
        [InlineData(0, "Proto=h1", "http")]
        [InlineData(1, "", "http")]
        [InlineData(1, "Proto=h1", "h1")]
        [InlineData(3, "Proto=h1", "h1")]
        [InlineData(1, "Proto=h2, Proto=h1", "h1")]
        [InlineData(2, "Proto=h2, Proto=h1", "h2")]
        [InlineData(10, "Proto=h3, Proto=h2, Proto=h1", "h3")]
        public async Task ForwardedProtoOverrideChangesRequestProtocol(int limit, string header, string expected)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Proto,
                            ForwardLimit = limit,
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = header;
            });

            Assert.Equal(expected, context.Request.Scheme);
        }

        public static TheoryData<string> ProtoHeaderData
        {
            get
            {
                // ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
                return new TheoryData<string>() {
                    "z",
                    "Z",
                    "1",
                    "y+",
                    "1-",
                    "a.",
                };
            }
        }

        [Theory]
        [MemberData(nameof(ProtoHeaderData))]
        public async Task ForwardedProtoAcceptsValidProtocols(string scheme)
        {
            var assertsExecuted = false;

            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Proto
                        });
                        app.Run(context =>
                        {
                            Assert.Equal(scheme, context.Request.Scheme);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Proto=" + scheme;
            });
            Assert.True(assertsExecuted);
        }

        public static TheoryData<string> ProtoHeaderInvalidData
        {
            get
            {
                // ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
                var data = new TheoryData<string>() {
                    "Proto=a b", // Space
                };

                // These aren't allowed anywhere in the scheme header
                var invalid = "!\"#$%&'()*/:;<=>?@[]\\^_`{}|~";
                foreach (var ch in invalid)
                {
                    data.Add("Proto=" + ch.ToString());
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(ProtoHeaderInvalidData))]
        public async Task ForwardedProtoRejectsInvalidProtocols(string scheme)
        {
            var assertsExecuted = false;

            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.Proto,
                        });
                        app.Run(context =>
                        {
                            Assert.Equal("http", context.Request.Scheme);
                            assertsExecuted = true;
                            return Task.FromResult(0);
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = scheme;
            });
            Assert.True(assertsExecuted);
        }

        [Fact]
        public void AllForwardsDisabledByDefault()
        {
            var options = new ForwardedHttpOptions();
            Assert.True(options.ForwardedHttp == ForwardedHttp.None);
            Assert.Equal(1, options.ForwardLimit);
            Assert.Single(options.KnownNetworks);
            Assert.Single(options.KnownProxies);
        }

        [Fact]
        public async Task AllForwardsEnabledChangeRequestRemoteIpHostandProtocol()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.All
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Proto=Protocol;For=11.111.111.11;Host=testhost";
            });

            Assert.Equal("11.111.111.11", context.Connection.RemoteIpAddress.ToString());
            Assert.Equal("testhost", context.Request.Host.ToString());
            Assert.Equal("Protocol", context.Request.Scheme);
        }

        [Fact]
        public async Task AllOptionsDisabledRequestDoesntChange()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.None
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Proto=Protocol;For=11.111.111.11;Host=otherhost";
            });

            Assert.Null(context.Connection.RemoteIpAddress);
            Assert.Equal("localhost", context.Request.Host.ToString());
            Assert.Equal("http", context.Request.Scheme);
        }

        [Fact]
        public async Task PartiallyEnabledForwardsPartiallyChangesRequest()
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(new ForwardedHttpOptions
                        {
                            ForwardedHttp = ForwardedHttp.For | ForwardedHttp.Proto
                        });
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = "Proto=Protocol;For=11.111.111.11";
            });

            Assert.Equal("11.111.111.11", context.Connection.RemoteIpAddress.ToString());
            Assert.Equal("localhost", context.Request.Host.ToString());
            Assert.Equal("Protocol", context.Request.Scheme);
        }

        [Theory]
        [InlineData("For=22.33.44.55, For=[::ffff:127.0.0.1]", "", "", "22.33.44.55")]
        [InlineData("For=22.33.44.55, For=[::ffff:172.123.142.121]", "172.123.142.121", "", "22.33.44.55")]
        [InlineData("For=22.33.44.55, For=[::ffff:172.123.142.121]", "::ffff:172.123.142.121", "", "22.33.44.55")]
        [InlineData("For=22.33.44.55, For=[::ffff:172.123.142.121], For=172.32.24.23", "", "172.0.0.0/8", "22.33.44.55")]
        [InlineData("For=[2a00:1450:4009:802::200e], For=[2a02:26f0:2d:183::356e], For=[::ffff:172.123.142.121], For=172.32.24.23", "", "172.0.0.0/8,2a02:26f0:2d:183::1/64", "2a00:1450:4009:802::200e")]
        [InlineData("For=22.33.44.55, For=[2a02:26f0:2d:183::356e], For=[::ffff:127.0.0.1]", "2a02:26f0:2d:183::356e", "", "22.33.44.55")]
        public async Task XForwardForIPv4ToIPv6Mapping(string forwardedHeader, string knownProxies, string knownNetworks, string expectedRemoteIp)
        {
            var options = new ForwardedHttpOptions
            {
                ForwardedHttp = ForwardedHttp.For,
                ForwardLimit = null,
            };

            foreach (var knownProxy in knownProxies.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
            {
                var proxy = IPAddress.Parse(knownProxy);
                options.KnownProxies.Add(proxy);
            }
            foreach (var knownNetwork in knownNetworks.Split(new string[] { "," }, options: StringSplitOptions.RemoveEmptyEntries))
            {
                var knownNetworkParts = knownNetwork.Split('/');
                var networkIp = IPAddress.Parse(knownNetworkParts[0]);
                var prefixLength = int.Parse(knownNetworkParts[1]);
                options.KnownNetworks.Add(new IPNetwork(networkIp, prefixLength));
            }

            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseForwardedHttp(options);
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = forwardedHeader;
            });

            Assert.Equal(expectedRemoteIp, context.Connection.RemoteIpAddress.ToString());
        }

        [Theory]
        [InlineData(1, "Proto=httpa, Proto=httpb, Proto=httpc", "httpc")]
        [InlineData(2, "Proto=httpa, Proto=httpb, Proto=httpc", "httpb")]
        public async Task ForwardersWithDIOptionsRunsOnce(int limit, string forwardedHeader, string expectedScheme)
        {
            using var host = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.Configure<ForwardedHttpOptions>(options =>
                        {
                            options.ForwardedHttp = ForwardedHttp.Proto;
                            options.KnownProxies.Clear();
                            options.KnownNetworks.Clear();
                            options.ForwardLimit = limit;
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseForwardedHttp();
                        app.UseForwardedHttp();
                    });
                }).Build();

            await host.StartAsync();

            var server = host.GetTestServer();

            var context = await server.SendAsync(c =>
            {
                c.Request.Headers["Forwarded"] = forwardedHeader;
            });

            Assert.Equal(expectedScheme, context.Request.Scheme);
        }
    }
}
