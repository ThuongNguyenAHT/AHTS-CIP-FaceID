using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Cip.Server.Net40.Web
{
    internal static class AssetProvider
    {
        private const string Prefix = "Cip.Server.Net40.Assets.";
        private static readonly object SyncRoot = new object();
        private static readonly IDictionary<string, string> Cache = new Dictionary<string, string>();

        public static string Get(string assetName)
        {
            lock (SyncRoot)
            {
                string cached;
                if (Cache.TryGetValue(assetName, out cached))
                {
                    return cached;
                }

                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(Prefix + assetName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Không tìm thấy embedded asset " + assetName + ".");
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string content = reader.ReadToEnd();
                        if (string.Equals(assetName, "tablet.js", StringComparison.OrdinalIgnoreCase))
                        {
                            content = content.Replace(
                                "        connectSignalR();",
                                "        setConnection('online', 'Đồng bộ mỗi giây');");
                        }

                        Cache[assetName] = content;
                        return content;
                    }
                }
            }
        }
    }
}
