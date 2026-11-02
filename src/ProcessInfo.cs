using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NetMute
{
    internal sealed class RunningApp
    {
        public string Path;
        public string Title = "";
        public int Connections;
        public readonly List<int> Pids = new List<int>();
    }

    internal static class ProcessInfo
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

        // Null for processes Windows will not tell us about.
        public static string ImagePath(int pid)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero)
                return null;
            try
            {
                // The window asks about every process every two seconds, so start with a buffer
                // that fits nearly all paths, not the longest one possible.
                foreach (int capacity in new[] { 1024, 32768 })
                {
                    StringBuilder name = new StringBuilder(capacity);
                    int size = name.Capacity;
                    if (QueryFullProcessImageName(process, 0, name, ref size))
                        return name.ToString();
                }
                return null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        // Running programs, one entry per exe file however many processes it has.
        public static List<RunningApp> Snapshot(bool withTitles)
        {
            Dictionary<int, int> connections = NetStat.ActiveByPid();
            Dictionary<string, RunningApp> apps = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    int pid = process.Id;
                    string path = pid > 4 ? ImagePath(pid) : null;
                    if (path == null)
                        continue;

                    RunningApp app;
                    if (!apps.TryGetValue(path, out app))
                    {
                        app = new RunningApp();
                        app.Path = path;
                        apps[path] = app;
                    }
                    app.Pids.Add(pid);

                    int count;
                    if (connections.TryGetValue(pid, out count))
                        app.Connections += count;

                    if (withTitles && app.Title.Length == 0)
                        app.Title = WindowTitle(process);
                }
            }
            return new List<RunningApp>(apps.Values);
        }

        public static List<int> PidsOf(string path)
        {
            List<int> pids = new List<int>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id > 4 && PathUtil.Same(ImagePath(process.Id), path))
                        pids.Add(process.Id);
                }
            }
            return pids;
        }

        private static string WindowTitle(Process process)
        {
            try
            {
                return process.MainWindowTitle ?? "";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }
    }
}
