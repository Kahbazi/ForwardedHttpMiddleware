using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace AspNetCore.ForwardedHttp
{
    public class ForwardedValues
    {
        public StringSegment For { get; set; }
        public StringSegment By { get; set; }
        public StringSegment Host { get; set; }
        public StringSegment Proto { get; set; }
    }

    public class ParseProxyValues
    {
        private static readonly char[] CommaChar = new[] { ',' };
        private static readonly char[] SemiColonChar = new[] { ';' };

        public ParseProxyValues()
        {

        }

        public static bool TryParse(StringValues values, out ParseProxyValues parseProxyValues)
        {
            parseProxyValues = null;

            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];

                var valueSegment = new StringSegment(value);

                var proxyValues = valueSegment.Split(CommaChar);

                foreach (var proxyValue in proxyValues)
                {
                    var trimmedProxyValue = proxyValue.TrimStart();

                    var parameters = trimmedProxyValue.Split(SemiColonChar);

                    ForwardedValues forwardedValues = null;

                    foreach (var parameter in parameters)
                    {
                        if (parameter.StartsWith("for=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (!StringSegment.IsNullOrEmpty(forwardedValues?.For ?? StringSegment.Empty))
                            {
                                return false;
                            }

                            forwardedValues ??= new ForwardedValues();

                            forwardedValues.For = StripQuotations(parameter.Subsegment("for=".Length));
                        }
                        else if (parameter.StartsWith("by=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (!StringSegment.IsNullOrEmpty(forwardedValues?.By ?? StringSegment.Empty))
                            {
                                return false;
                            }

                            forwardedValues ??= new ForwardedValues();

                            forwardedValues.By = StripQuotations(parameter.Subsegment("by=".Length));
                        }
                        else if (parameter.StartsWith("host=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (!StringSegment.IsNullOrEmpty(forwardedValues?.Host ?? StringSegment.Empty))
                            {
                                return false;
                            }

                            forwardedValues ??= new ForwardedValues();

                            forwardedValues.Host = StripQuotations(parameter.Subsegment("host=".Length));
                        }
                        else if (parameter.StartsWith("proto=", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (!StringSegment.IsNullOrEmpty(forwardedValues?.Proto ?? StringSegment.Empty))
                            {
                                return false;
                            }

                            forwardedValues ??= new ForwardedValues();

                            forwardedValues.Proto = StripQuotations(parameter.Subsegment("proto=".Length));
                        }
                    }

                    if (forwardedValues == null)
                    {
                        return false;
                    }

                    parseProxyValues ??= new ParseProxyValues();

                    parseProxyValues.ForwardedValues.Add(forwardedValues);
                }
            }

            return true;
        }

        private static StringSegment StripQuotations(StringSegment value)
        {
            if (value.Length == 0 || value.Length == 1)
            {
                return value;
            }

            if (value[0] == '\"' && value[^1] == '\"')
            {
                return value.Subsegment(1, value.Length - 2);
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
                    builder.Append((ReadOnlySpan<char>)forwardedValue.For);
                    builder.Append(";");
                }

                if (forwardedValue.By != null)
                {
                    builder.Append("by=");
                    builder.Append((ReadOnlySpan<char>)forwardedValue.By);
                    builder.Append(";");
                }

                if (forwardedValue.Host != null)
                {
                    builder.Append("host=");
                    builder.Append((ReadOnlySpan<char>)forwardedValue.Host);
                    builder.Append(";");
                }

                if (forwardedValue.Proto != null)
                {
                    builder.Append("proto=");
                    builder.Append((ReadOnlySpan<char>)forwardedValue.Proto);
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
