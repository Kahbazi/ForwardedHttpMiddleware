using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AspNetCore.ForwardedHttp.Tests
{
    public class ParseProxyValuesTests
    {
        [Theory]
        [InlineData("for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost")]
        [InlineData("FOR=11.11.11.11;bY=101.101.101.101;PRotO=http;hOsT=testhost")]
        [InlineData("host=testhost;proto=http;by=101.101.101.101;for=11.11.11.11")]
        [InlineData("by=101.101.101.101;for=11.11.11.11;host=testhost;proto=http")]
        [InlineData("anotherParameter=randomvalue;for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost")]
        public void SingleProxy(string headerValue)
        {
            // Arrange
            var value = new StringValues(headerValue);

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            var firstProxy = Assert.Single(parser.ForwardedValues);
            Assert.Equal("11.11.11.11", firstProxy.For);
            Assert.Equal("101.101.101.101", firstProxy.By);
            Assert.Equal("http", firstProxy.Proto);
            Assert.Equal("testhost", firstProxy.Host);
        }

        [Fact]
        public void TwoProxiesWithSingleStringValues()
        {
            // Arrange
            var value = new StringValues("for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost,for=12.12.12.12;by=102.102.102.102;proto=https;host=testhost2");

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            Assert.Equal(2, parser.ForwardedValues.Count);

            var firstProxy = parser.ForwardedValues[0];
            Assert.Equal("11.11.11.11", firstProxy.For);
            Assert.Equal("101.101.101.101", firstProxy.By);
            Assert.Equal("http", firstProxy.Proto);
            Assert.Equal("testhost", firstProxy.Host);

            var secondProxy = parser.ForwardedValues[1];
            Assert.Equal("12.12.12.12", secondProxy.For);
            Assert.Equal("102.102.102.102", secondProxy.By);
            Assert.Equal("https", secondProxy.Proto);
            Assert.Equal("testhost2", secondProxy.Host);
        }

        [Fact]
        public void TwoProxiesWithMultipleStringValues()
        {
            // Arrange
            var value = new StringValues(new string[]
            {
                "for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost",
                "for=12.12.12.12;by=102.102.102.102;proto=https;host=testhost2"
            });

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            Assert.Equal(2, parser.ForwardedValues.Count);

            var firstProxy = parser.ForwardedValues[0];
            Assert.Equal("11.11.11.11", firstProxy.For);
            Assert.Equal("101.101.101.101", firstProxy.By);
            Assert.Equal("http", firstProxy.Proto);
            Assert.Equal("testhost", firstProxy.Host);

            var secondProxy = parser.ForwardedValues[1];
            Assert.Equal("12.12.12.12", secondProxy.For);
            Assert.Equal("102.102.102.102", secondProxy.By);
            Assert.Equal("https", secondProxy.Proto);
            Assert.Equal("testhost2", secondProxy.Host);
        }

        [Fact]
        public void ThreeoProxiesWithMultipleStringValues()
        {
            // Arrange
            var value = new StringValues(new string[]
            {
                "for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost,for=12.12.12.12;by=102.102.102.102;proto=https;host=testhost2",
                "for=13.13.13.13;by=103.103.103.103;proto=http;host=testhost3"
            });

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            Assert.Equal(3, parser.ForwardedValues.Count);

            var firstProxy = parser.ForwardedValues[0];
            Assert.Equal("11.11.11.11", firstProxy.For);
            Assert.Equal("101.101.101.101", firstProxy.By);
            Assert.Equal("http", firstProxy.Proto);
            Assert.Equal("testhost", firstProxy.Host);

            var secondProxy = parser.ForwardedValues[1];
            Assert.Equal("12.12.12.12", secondProxy.For);
            Assert.Equal("102.102.102.102", secondProxy.By);
            Assert.Equal("https", secondProxy.Proto);
            Assert.Equal("testhost2", secondProxy.Host);

            var thirdProxy = parser.ForwardedValues[2];
            Assert.Equal("13.13.13.13", thirdProxy.For);
            Assert.Equal("103.103.103.103", thirdProxy.By);
            Assert.Equal("http", thirdProxy.Proto);
            Assert.Equal("testhost3", thirdProxy.Host);
        }

        [Fact]
        public void MissingParameters()
        {
            // Arrange
            var value = new StringValues(new string[]
            {
                "for=11.11.11.11;by=101.101.101.101;proto=http;host=testhost",
                "for=12.12.12.12;by=102.102.102.102;proto=https;host=testhost2"
            });

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            Assert.Equal(2, parser.ForwardedValues.Count);

            var firstProxy = parser.ForwardedValues[0];
            Assert.Equal("11.11.11.11", firstProxy.For);
            Assert.Equal("101.101.101.101", firstProxy.By);
            Assert.Equal("http", firstProxy.Proto);
            Assert.Equal("testhost", firstProxy.Host);

            var secondProxy = parser.ForwardedValues[1];
            Assert.Equal("12.12.12.12", secondProxy.For);
            Assert.Equal("102.102.102.102", secondProxy.By);
            Assert.Equal("https", secondProxy.Proto);
            Assert.Equal("testhost2", secondProxy.Host);
        }

        [Fact]
        public void StripDoubleQuatationsForQuotedValue()
        {
            // Arrange
            var value = new StringValues("for=\"_something\"");

            // Act
            var success = ParseProxyValues.TryParse(value, out var parser);

            // Assert
            Assert.True(success);
            var firstProxy = Assert.Single(parser.ForwardedValues);
            Assert.Equal("_something", firstProxy.For);
        }

        [Theory]
        [InlineData("for=11.11.11.11;for=12.12.12.12")]
        [InlineData("by=11.11.11.11;by=12.12.12.12")]
        [InlineData("host=host1;host=host2")]
        [InlineData("proto=http;proto=https")]
        public void DuplicateFieldThrowsException(string headerValue)
        {
            // Arrange
            var value = new StringValues(headerValue);

            // Assert
            var success = ParseProxyValues.TryParse(value, out var parser);

            Assert.False(success);
        }

        [Fact]
        public void NoProxyCreateHeader()
        {
            // Arrange
            var parser = new ParseProxyValues();

            // Act
            var headerValue = parser.Value;

            // Assert
            Assert.Empty(headerValue);
        }

        [Fact]
        public void SingleProxyCreateHeader()
        {
            // Arrange
            var parser = new ParseProxyValues
            {
                ForwardedValues = new List<ForwardedValues>
                {
                    new ForwardedValues
                    {
                        For = "11.11.11.11",
                        By = "101.101.101.101",
                        Host = "testhost",
                        Proto = "https",
                    }
                }
            };

            // Act
            var headerValue = parser.Value;

            // Assert
            var value = Assert.Single(headerValue);
            Assert.Equal("for=11.11.11.11;by=101.101.101.101;host=testhost;proto=https", value);
        }

        [Fact]
        public void TwoProxiesCreateHeader()
        {
            // Arrange
            var parser = new ParseProxyValues
            {
                ForwardedValues = new List<ForwardedValues>
                {
                    new ForwardedValues
                    {
                        For = "11.11.11.11",
                        By = "101.101.101.101",
                        Host = "testhost",
                        Proto = "https",
                    },
                    new ForwardedValues
                    {
                        For = "12.12.12.12",
                        By = "102.102.102.102",
                        Host = "testhost2",
                        Proto = "https",
                    }
                }
            };

            // Act
            var headerValue = parser.Value;

            // Assert
            var value = Assert.Single(headerValue);
            Assert.Equal("for=11.11.11.11;by=101.101.101.101;host=testhost;proto=https, for=12.12.12.12;by=102.102.102.102;host=testhost2;proto=https", value);
        }
    }
}
