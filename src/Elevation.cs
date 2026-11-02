using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace NetMute
{
    // NetMute starts with normal rights and asks for more only when it needs them. That keeps
    // --help and --version free of a UAC prompt.
    internal static class Elevation
    {
        private const int ErrorCancelled = 1223;

        public static bool IsAdmin
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // Starts this exe again with the same arguments and administrator rights.
        public static int Relaunch(string[] args, bool wait)
        {
            ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, JoinArguments(AbsolutePaths(args)));
            info.UseShellExecute = true;
            info.Verb = "runas";
            try
            {
                using (Process process = Process.Start(info))
                {
                    if (!wait)
                        return 0;
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != ErrorCancelled)
                    Ui.Error(null, ex.Message);
                return 1;
            }
        }

        // The elevated process starts in System32, where relative paths mean something else.
        private static string[] AbsolutePaths(string[] args)
        {
            string[] result = (string[])args.Clone();
            for (int i = 0; i < result.Length; i++)
                if (File.Exists(result[i]) || (i > 0 && PathUtil.Same(result[i - 1], "--log")))
                    result[i] = Path.GetFullPath(result[i]);
            return result;
        }

        private static string JoinArguments(string[] args)
        {
            StringBuilder line = new StringBuilder();
            foreach (string arg in args)
            {
                if (line.Length > 0)
                    line.Append(' ');
                AppendQuoted(line, arg);
            }
            return line.ToString();
        }

        // The quoting rules CommandLineToArgvW reads back.
        private static void AppendQuoted(StringBuilder line, string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                line.Append(arg);
                return;
            }

            line.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                    line.Append('\\', backslashes * 2 + 1);
                else
                    line.Append('\\', backslashes);
                backslashes = 0;
                line.Append(c);
            }
            line.Append('\\', backslashes * 2);
            line.Append('"');
        }
    }
}
