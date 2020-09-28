using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Primitives;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedValues
    {
        public string For { get; set; }
        public string By { get; set; }
        public string Host { get; set; }
        public string Proto { get; set; }
    }

    //TODO: Need more efficient implementation
    public class ParseProxyValues
    {
        public ParseProxyValues(StringValues values)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];

                var proxyValues = value.Split(',');

                for (int m = 0; m < proxyValues.Length; m++)
                {
                    var proxyValue = proxyValues[m];

                    proxyValue = proxyValue.TrimStart(' ');

                    var parameters = proxyValue.Split(";");

                    var forwardedValues = new ForwardedValues();

                    for (int z = 0; z < parameters.Length; z++)
                    {
                        var parameter = parameters[z];

                        if (parameter.StartsWith("for=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (forwardedValues.For != null)
                            {
                                throw new ArgumentException(nameof(values));
                            }

                            forwardedValues.For = StripQuotations(parameter.Remove(0, "for=".Length));
                        }
                        else if (parameter.StartsWith("by=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (forwardedValues.By != null)
                            {
                                throw new ArgumentException(nameof(values));
                            }

                            forwardedValues.By = StripQuotations(parameter.Remove(0, "by=".Length));
                        }
                        else if (parameter.StartsWith("host=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (forwardedValues.Host != null)
                            {
                                throw new ArgumentException(nameof(values));
                            }

                            forwardedValues.Host = StripQuotations(parameter.Remove(0, "host=".Length));
                        }
                        else if (parameter.StartsWith("proto=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (forwardedValues.Proto != null)
                            {
                                throw new ArgumentException(nameof(values));
                            }

                            forwardedValues.Proto = StripQuotations(parameter.Remove(0, "proto=".Length));
                        }
                    }

                    ForwardedValues.Add(forwardedValues);
                }
            }
        }

        private static string StripQuotations(string value)
        {
            if (value.Length == 0 || value.Length == 1)
            {
                return value;
            }

            if (value[0] == '\"' && value[^1] == '\"')
            {
                return value[1..^1];
            }
            else
            {
                return value;
            }
        }

        public IList<ForwardedValues> ForwardedValues { get; set; } = new List<ForwardedValues>();

        public StringValues Value => Create();

        private StringValues Create()
        {
            if (ForwardedValues.Count == 0)
            {
                return StringValues.Empty;
            }

            var builder = new StringBuilder();

            for (int i = 0; i < ForwardedValues.Count; i++)
            {
                var forwardedValue = ForwardedValues[i];

                if (forwardedValue.For != null)
                {
                    builder.Append("for=");
                    builder.Append(forwardedValue.For);
                    builder.Append(";");
                }

                if (forwardedValue.By != null)
                {
                    builder.Append("by=");
                    builder.Append(forwardedValue.By);
                    builder.Append(";");
                }

                if (forwardedValue.Host != null)
                {
                    builder.Append("host=");
                    builder.Append(forwardedValue.Host);
                    builder.Append(";");
                }

                if (forwardedValue.Proto != null)
                {
                    builder.Append("proto=");
                    builder.Append(forwardedValue.Proto);
                    builder.Append(";");
                }

                builder.Remove(builder.Length - 1, 1);
                builder.Append(", ");
            }

            builder.Remove(builder.Length - 2, 2);

            return new StringValues(builder.ToString());
        }
    }
}
