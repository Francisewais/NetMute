using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NetMute
{
    // The loopback ports a program could use to reach the internet through a proxy client.
    internal static class LocalProxy
    {
        private static readonly ushort[] CommonPorts =
        {
            1080, 1081, 1089,        // Shadowsocks, ShadowsocksR, Qv2ray
            2080, 2081,              // NekoBox, sing-box
            6152, 6153,              // Surge
            7890, 7891, 7892, 7893,  // Clash, Mihomo, FlClash
            7897, 7898, 7899,        // Clash Verge
            8888, 8889,              // Fiddler, Charles, Qv2ray
            9050, 9150,              // Tor
            10808, 10809, 10810,     // v2rayN
            12334, 12335,            // Hiddify
            20170, 20171, 20172      // v2rayA
        };

        private static readonly Regex LocalAddress = new Regex(
            @"(?:^|[=;\s/])(?:127\.\d+\.\d+\.\d+|localhost|\[?::1\]?):(\d{1,5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // The common ports plus whatever the Windows proxy setting points at, even while that
        // setting is switched off: proxy clients leave it behind and turn it on and off at will.
        public static List<ushort> Ports()
        {
            List<ushort> ports = new List<ushort>(CommonPorts);
            foreach (ushort port in ParseProxySetting(ReadProxySetting()))
                if (!ports.Contains(port))
                    ports.Add(port);
            return ports;
        }

        private static string ReadProxySetting()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    return key == null ? "" : key.GetValue("ProxyServer", "") as string ?? "";
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        // Accepts both forms Windows uses: "127.0.0.1:7890" and
        // "http=127.0.0.1:7890;https=127.0.0.1:7890;socks=127.0.0.1:7891".
        public static List<ushort> ParseProxySetting(string setting)
        {
            List<ushort> ports = new List<ushort>();
            foreach (Match match in LocalAddress.Matches(setting))
            {
                ushort port;
                if (ushort.TryParse(match.Groups[1].Value, out port) && port != 0 && !ports.Contains(port))
                    ports.Add(port);
            }
            return ports;
        }
    }
}
