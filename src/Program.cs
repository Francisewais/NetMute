using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NetMute
{
    internal static class Program
    {
        public const string Version = "1.0.0";
        public const string HomePage = "https://github.com/Francisewais/NetMute";

        private const uint LoadLibrarySearchSystem32 = 0x00000800;
        private const int SW_RESTORE = 9;

        [DllImport("kernel32.dll")]
        private static extern bool SetDefaultDllDirectories(uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [STAThread]
        private static int Main(string[] args)
        {
            // See the note in AssemblyInfo.cs. This covers DLLs loaded by others in our process.
            SetDefaultDllDirectories(LoadLibrarySearchSystem32);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string command = args.Length > 0 ? args[0].TrimStart('-', '/').ToLowerInvariant() : "";
            switch (command)
            {
                case "help":
                case "h":
                case "?":
                    return Cli.Show(Cli.Usage);
                case "version":
                    return Cli.Show(Ui.AppName + " " + Version);
                case "render":
                    return args.Length == 3 ? Dev.Render(args[1], args[2]) : 2;
            }

            bool isGui = args.Length == 0 || File.Exists(args[0]);
            if (!isGui && command != "selftest" && !Cli.IsCommand(command))
            {
                Ui.Error(null, Cli.Usage);
                return 2;
            }

            if (!Elevation.IsAdmin)
                return Elevation.Relaunch(args, !isGui);

            if (isGui)
                return RunGui(args);
            if (command == "selftest")
                return SelfTest.Run(ReadLogPath(args));
            return Cli.Run(command, args.Skip(1).ToList());
        }

        private static int RunGui(IEnumerable<string> programsToAdd)
        {
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Ui.Error(null, e.Exception.Message);
            };

            bool isFirstInstance;
            using (new Mutex(true, @"Local\NetMute-window", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    ActivateFirstInstance();
                    return 0;
                }

                Firewall firewall;
                try
                {
                    firewall = new Firewall();
                }
                catch (Exception ex)
                {
                    Ui.Error(null, ex.Message);
                    return 1;
                }

                using (firewall)
                    Application.Run(new MainForm(firewall, programsToAdd));
            }
            return 0;
        }

        private static void ActivateFirstInstance()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                using (process)
                {
                    if (process.Id != self && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(process.MainWindowHandle);
                        return;
                    }
                }
            }
        }

        private static string ReadLogPath(string[] args)
        {
            int index = Array.FindIndex(args, arg => PathUtil.Same(arg, "--log"));
            return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
        }
    }
}
