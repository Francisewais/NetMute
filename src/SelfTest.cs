using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetMute
{
    // Proves on the machine it runs on that blocking does what NetMute promises. A copy of the
    // curl.exe that ships with Windows plays the blocked program, the original plays "every
    // other program", and NetMute itself plays a program that already has open connections.
    // Nothing else on the machine is touched, and every rule is removed again at the end.
    internal sealed class SelfTest
    {
        private const int ExpectedFilterCount = 6;

        private static readonly string[] Urls =
        {
            "http://www.msftconnecttest.com/connecttest.txt",
            "https://www.baidu.com/",
            "https://example.com/"
        };

        private static readonly string[] KeepAliveHosts =
        {
            "www.msftconnecttest.com",
            "www.baidu.com",
            "www.qq.com",
            "example.com"
        };

        private readonly string logPath;
        private readonly StringBuilder report = new StringBuilder();
        private int failures;
        private int checks;

        private SelfTest(string logPath)
        {
            this.logPath = logPath;
        }

        public static int Run(string logPath)
        {
            SelfTest test = new SelfTest(logPath);
            test.RunAll();
            if (logPath == null && SystemInformation.UserInteractive)
                MessageBox.Show(test.report.ToString(), Ui.AppName + " self-test", MessageBoxButtons.OK,
                    test.failures == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return test.failures == 0 ? 0 : 1;
        }

        private void RunAll()
        {
            if (logPath != null)
                File.WriteAllText(logPath, "", new UTF8Encoding(false));

            Line("NetMute " + Program.Version + " self-test");
            Line(Environment.OSVersion + ", " + (IntPtr.Size * 8) + "-bit process");

            string folder = Path.Combine(Path.GetTempPath(), "NetMute-selftest-" + Process.GetCurrentProcess().Id);
            string subject = Path.Combine(folder, "curl-subject.exe");
            string systemCurl = Path.Combine(Environment.SystemDirectory, "curl.exe");
            string self = Application.ExecutablePath;

            try
            {
                string layout = WfpNative.LayoutProblems();
                Check("Struct layout matches the Windows headers", layout.Length == 0, layout);
                Check("Local proxy setting is parsed",
                    LocalProxy.ParseProxySetting("http=127.0.0.1:7890;https=localhost:7891;socks=10.0.0.1:1080").SequenceEqual(new ushort[] { 7890, 7891 }));

                Directory.CreateDirectory(folder);
                File.Copy(systemCurl, subject);

                string url = FindWorkingUrl(subject);
                Check("Test program can reach the internet before blocking", url != null, "tried " + string.Join(", ", Urls));
                if (url == null)
                    return;
                Line("    using " + url);

                using (Firewall firewall = new Firewall())
                {
                    try
                    {
                        TestBlockedProgram(firewall, subject, systemCurl, url);
                        TestOpenConnections(firewall, self);
                    }
                    finally
                    {
                        firewall.Unblock(subject);
                        firewall.Unblock(self);
                        Check("No test rules are left behind", !firewall.IsBlocked(subject) && !firewall.IsBlocked(self));
                    }
                }
            }
            catch (Exception ex)
            {
                Check("Test ran to the end", false, ex.ToString());
            }
            finally
            {
                TryDeleteFolder(folder);
                Line("");
                Line(failures == 0
                    ? "All " + checks + " checks passed."
                    : failures + " of " + checks + " checks FAILED.");
            }
        }

        private void TestBlockedProgram(Firewall firewall, string subject, string systemCurl, string url)
        {
            firewall.Block(subject);

            BlockedApp rule = firewall.Blocked().FirstOrDefault(app => PathUtil.Same(app.Path, subject));
            Check("Blocking adds one set of " + ExpectedFilterCount + " rules",
                rule != null && rule.FilterIds.Count == ExpectedFilterCount,
                rule == null ? "no rules found" : rule.FilterIds.Count + " rules");
            firewall.Block(subject);
            rule = firewall.Blocked().FirstOrDefault(app => PathUtil.Same(app.Path, subject));
            Check("Blocking twice does not pile up rules", rule != null && rule.FilterIds.Count == ExpectedFilterCount);

            Check("Blocked program cannot reach the internet", !IsOnline(subject, url));
            Check("Other programs still reach the internet", IsOnline(systemCurl, url));

            List<ushort> freeProxyPorts = LocalProxy.Ports().Where(IsPortFree).ToList();
            Check("Found free proxy ports to test with", freeProxyPorts.Count >= 2);
            if (freeProxyPorts.Count >= 2)
            {
                using (LocalServer first = new LocalServer(freeProxyPorts.First()))
                using (LocalServer last = new LocalServer(freeProxyPorts.Last()))
                using (LocalServer unrelated = LocalServer.OnUnlistedPort())
                {
                    Check("Blocked program cannot reach a local proxy, port " + first.Port, !IsOnline(subject, first.Url));
                    Check("Blocked program cannot reach a local proxy, port " + last.Port, !IsOnline(subject, last.Url));
                    Check("Blocked program still reaches other local ports, port " + unrelated.Port, IsOnline(subject, unrelated.Url));
                    Check("Other programs still reach the local proxy", IsOnline(systemCurl, first.Url));
                }

                if (Socket.OSSupportsIPv6)
                {
                    using (LocalServer overIpv6 = new LocalServer(freeProxyPorts.First(), IPAddress.IPv6Loopback))
                        Check("Blocked program cannot reach a local proxy over IPv6", !IsOnline(subject, overIpv6.Url));
                }
            }

            Check("Unblocking reports success", firewall.Unblock(subject));
            Check("Program is online again after unblocking", IsOnline(subject, url));
        }

        private void TestOpenConnections(Firewall firewall, string self)
        {
            string host = FindKeepAliveHost();
            if (host == null)
            {
                Line("[SKIP] Open connections: no test server keeps a connection open");
                return;
            }

            using (TcpClient reauthorized = Connect(host))
            using (TcpClient closed = Connect(host))
            {
                Check("Connections work before blocking", Head(reauthorized, host) && Head(closed, host));

                firewall.Block(self);
                using (TcpClient blockedNew = Connect(host))
                    Check("New connections fail after blocking", blockedNew == null);
                Check("Connections that were already open stop working", !Head(reauthorized, host));

                NetStat.CloseConnections(new[] { Process.GetCurrentProcess().Id });
                Stopwatch watch = Stopwatch.StartNew();
                bool stillWorks = Head(closed, host);
                Check("Open connections can be closed at once", !stillWorks && watch.ElapsedMilliseconds < 2000,
                    watch.ElapsedMilliseconds + " ms");

                firewall.Unblock(self);
                using (TcpClient restored = Connect(host))
                    Check("New connections work again after unblocking", restored != null && Head(restored, host));
            }
        }

        private static string FindWorkingUrl(string curl)
        {
            return Urls.FirstOrDefault(url => IsOnline(curl, url));
        }

        // A server that answers two requests on one connection, or a blocked connection could
        // not be told apart from one the server simply closed.
        private static string FindKeepAliveHost()
        {
            foreach (string host in KeepAliveHosts)
            {
                using (TcpClient client = Connect(host))
                {
                    if (client != null && Head(client, host) && Head(client, host))
                        return host;
                }
            }
            return null;
        }

        private static bool IsOnline(string curl, string url)
        {
            ProcessStartInfo info = new ProcessStartInfo(curl,
                "-sS --noproxy \"*\" -o NUL -w \"%{http_code}\" --connect-timeout 8 --max-time 20 \"" + url + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                Task<string> errors = process.StandardError.ReadToEndAsync();
                string code = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                errors.Wait();
                return process.ExitCode == 0 && code.Length > 0 && code[0] >= '2' && code[0] <= '3';
            }
        }

        private static TcpClient Connect(string host)
        {
            TcpClient client = new TcpClient();
            try
            {
                if (client.ConnectAsync(host, 80).Wait(8000) && client.Connected)
                    return client;
            }
            catch (AggregateException)
            {
            }
            client.Close();
            return null;
        }

        private static bool Head(TcpClient client, string host)
        {
            if (client == null)
                return false;
            try
            {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 6000;
                stream.WriteTimeout = 6000;
                byte[] request = Encoding.ASCII.GetBytes(
                    "HEAD / HTTP/1.1\r\nHost: " + host + "\r\nConnection: keep-alive\r\n\r\n");
                stream.Write(request, 0, request.Length);
                byte[] buffer = new byte[8192];
                int read = stream.Read(buffer, 0, buffer.Length);
                return read > 0 && Encoding.ASCII.GetString(buffer, 0, read).StartsWith("HTTP/", StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsPortFree(ushort port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static void TryDeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Check(string name, bool passed)
        {
            Check(name, passed, "");
        }

        private void Check(string name, bool passed, string detail)
        {
            checks++;
            if (!passed)
                failures++;
            Line((passed ? "[PASS] " : "[FAIL] ") + name + (!passed && detail.Length > 0 ? ": " + detail : ""));
        }

        private void Line(string text)
        {
            report.AppendLine(text);
            if (logPath != null)
                File.AppendAllText(logPath, text + Environment.NewLine, new UTF8Encoding(false));
        }

        // A tiny web server on 127.0.0.1, standing in for a local proxy client.
        private sealed class LocalServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread thread;
            private readonly string host;

            public LocalServer(int port)
                : this(port, IPAddress.Loopback)
            {
            }

            public LocalServer(int port, IPAddress address)
            {
                host = address.Equals(IPAddress.IPv6Loopback) ? "[::1]" : "127.0.0.1";
                listener = new TcpListener(address, port);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                thread = new Thread(Serve);
                thread.IsBackground = true;
                thread.Start();
            }

            public int Port { get; private set; }

            public string Url
            {
                get { return "http://" + host + ":" + Port + "/"; }
            }

            // Windows picks the port. Retry in the rare case it picks one on the proxy list.
            public static LocalServer OnUnlistedPort()
            {
                List<ushort> listed = LocalProxy.Ports();
                while (true)
                {
                    LocalServer server = new LocalServer(0);
                    if (!listed.Contains((ushort)server.Port))
                        return server;
                    server.Dispose();
                }
            }

            public void Dispose()
            {
                listener.Stop();
                thread.Join(2000);
            }

            private void Serve()
            {
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
                while (true)
                {
                    try
                    {
                        using (TcpClient client = listener.AcceptTcpClient())
                        {
                            NetworkStream stream = client.GetStream();
                            stream.ReadTimeout = 3000;
                            stream.Read(new byte[4096], 0, 4096);
                            stream.Write(response, 0, response.Length);
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
            }
        }
    }
}
