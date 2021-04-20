using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace AspNetCore.ForwardedHttp.Tests
{
    public class AppBuilderExtensionsTest
    {
        [Fact]
        public void UseForwardedHttpOnlyAddOnce()
        {
            // Arrange
            var appBuilder = new TestAppBuilder();

            // Act
            appBuilder.UseForwardedHttp();
            appBuilder.UseForwardedHttp();

            // Assert
            Assert.Single(appBuilder.Middlewares);
        }

        private class TestAppBuilder : IApplicationBuilder
        {
            public List<Func<RequestDelegate, RequestDelegate>> Middlewares { get; } = new List<Func<RequestDelegate, RequestDelegate>>();

            public IServiceProvider ApplicationServices { get; set; }

            public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

            public IFeatureCollection ServerFeatures { get; }

            public RequestDelegate Build()
            {
                return default;
            }

            public IApplicationBuilder New()
            {
                return default;
            }

            public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
            {
                Middlewares.Add(middleware);
                return this;
            }
        }
    }
}
