using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AspNetCore.ForwardedHttp
{
    //TODO: Need more efficient implementation
    internal class ForwardedHeader
    {
        public ForwardedHeader(StringValues value)
        {
            for (var i = 0; i < value.Count; i++)
            {
                var header = value[i];
                var sections = header.Split(";");

                for (int z = 0; z < sections.Length; z++)
                {
                    var items = sections[z].Split(",");

                    for (var j = 0; j < items.Length; j++)
                    {
                        var kv = items[j].Split("=");

                        var key = kv[0].Trim();
                        if (key.Equals("For"))
                        {
                            For.AddRange(kv[1].Split(","));
                        }
                        else if (key.Equals("By"))
                        {
                            By.AddRange(kv[1].Split(","));
                        }
                        else if (key.Equals("Host"))
                        {
                            Host.AddRange(kv[1].Split(","));
                        }
                        else if (key.Equals("Proto"))
                        {
                            Proto.AddRange(kv[1].Split(","));
                        }
                    }
                }
            }
        }

        public List<string> For { get; internal set; } = new List<string>();

        public List<string> By { get; internal set; } = new List<string>();

        public List<string> Host { get; internal set; } = new List<string>();

        public List<string> Proto { get; internal set; } = new List<string>();

        public StringValues Value => Create();

        private StringValues Create()
        {
            if (For.Count == 0
                && By.Count == 0
                && Host.Count == 0
                && Proto.Count == 0)
            {
                return StringValues.Empty;
            }

            var builder = new StringBuilder();
            if (For.Count > 0)
            {
                for (var i = 0; i < For.Count; i++)
                {
                    builder.Append("For=");
                    builder.Append(For[i]);

                    if (i < For.Count-1)
                    { 
                        builder.Append(", ");
                    }
                }
                builder.Append(';');
            }

            if (By.Count > 0)
            {
                for (var i = 0; i < By.Count; i++)
                {
                    builder.Append("By=");
                    builder.Append(By[i]);

                    if (i < By.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }
                builder.Append(';');
            }

            if (Host.Count > 0)
            {
                for (var i = 0; i < Host.Count; i++)
                {
                    builder.Append("Host=");
                    builder.Append(Host[i]);

                    if (i < Host.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }
                builder.Append(';');
            }

            if (Proto.Count > 0)
            {
                for (var i = 0; i < Proto.Count; i++)
                {
                    builder.Append("Proto=");
                    builder.Append(Proto[i]);

                    if (i < Proto.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }
                builder.Append(';');
            }

            if (builder.Length > 0)
            {
                builder.Remove(builder.Length - 1, 1);
            }

            return builder.ToString();
        }
    }
}
