using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NetMute
{
    internal static class Cli
    {
        private const int ExitOk = 0;
        private const int ExitFailed = 1;
        private const int ExitUsage = 2;

        private const int AttachParentProcess = -1;
        private const int StdOutputHandle = -11;
        private const int FileTypeDisk = 1;
        private const int FileTypePipe = 3;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll")]
        private static extern int GetFileType(IntPtr handle);

        public static bool IsCommand(string command)
        {
            switch (command)
            {
                case "block":
                case "unblock":
                case "toggle":
                case "list":
                case "clear":
                    return true;
                default:
                    return false;
            }
        }

        public static string Usage
        {
            get
            {
                return L.T(
                    "用法：\n" +
                    "  NetMute                     打开窗口\n" +
                    "  NetMute block <程序>...     断网\n" +
                    "  NetMute unblock <程序>...   恢复联网\n" +
                    "  NetMute toggle <程序>       切换断网/联网\n" +
                    "  NetMute list                列出已断网的程序\n" +
                    "  NetMute clear               全部恢复，并删除留在系统里的所有内容\n" +
                    "  NetMute selftest [--log 文件]  自检\n" +
                    "\n" +
                    "<程序> 可以是 exe 路径、快捷方式，或正在运行的程序名（如 steam.exe）。\n" +
                    "需要管理员权限，普通权限下运行时会弹出确认。",
                    "Usage:\n" +
                    "  NetMute                     open the window\n" +
                    "  NetMute block <program>...   cut off the network\n" +
                    "  NetMute unblock <program>... allow the network again\n" +
                    "  NetMute toggle <program>     switch between the two\n" +
                    "  NetMute list                 list blocked programs\n" +
                    "  NetMute clear                allow everything and remove all traces\n" +
                    "  NetMute selftest [--log file]  run the self-test\n" +
                    "\n" +
                    "<program> is an exe path, a shortcut, or the name of a running program (steam.exe).\n" +
                    "Administrator rights are required; Windows asks for them if needed.");
            }
        }

        public static int Show(string text)
        {
            if (HasOutput())
            {
                Console.WriteLine();
                Console.WriteLine(text);
            }
            else if (SystemInformation.UserInteractive)
            {
                Ui.Info(null, text);
            }
            return ExitOk;
        }

        public static int Run(string command, IList<string> programs)
        {
            bool hasOutput = HasOutput();
            StringBuilder output = new StringBuilder();
            bool toastBlocked = false;
            bool toast = false;
            int exitCode = ExitOk;

            try
            {
                using (Firewall firewall = new Firewall())
                {
                    switch (command)
                    {
                        case "block":
                            exitCode = RequirePrograms(programs, output);
                            foreach (string program in Each(programs, exitCode))
                            {
                                Block(firewall, PathUtil.ToProgramPath(program), output);
                                toast = toastBlocked = true;
                            }
                            break;

                        case "unblock":
                            exitCode = RequirePrograms(programs, output);
                            foreach (string program in Each(programs, exitCode))
                            {
                                Unblock(firewall, ForgivingPath(program), output);
                                toast = true;
                            }
                            break;

                        case "toggle":
                            exitCode = RequirePrograms(programs, output);
                            foreach (string program in Each(programs, exitCode))
                            {
                                string path = ForgivingPath(program);
                                toast = true;
                                if (firewall.IsBlocked(path))
                                {
                                    Unblock(firewall, path, output);
                                }
                                else
                                {
                                    Block(firewall, PathUtil.ToProgramPath(program), output);
                                    toastBlocked = true;
                                }
                            }
                            break;

                        case "list":
                            List(firewall, output);
                            break;

                        case "clear":
                            Muter.RemoveEverything(firewall);
                            output.AppendLine(L.T("已全部清除。", "All removed."));
                            toast = true;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine(ex.Message);
                exitCode = ExitFailed;
                toast = false;
            }

            Present(output.ToString().TrimEnd(), hasOutput, toast && exitCode == ExitOk, toastBlocked, exitCode);
            return exitCode;
        }

        private static int RequirePrograms(IList<string> programs, StringBuilder output)
        {
            if (programs.Count > 0)
                return ExitOk;
            output.AppendLine(Usage);
            return ExitUsage;
        }

        private static IEnumerable<string> Each(IList<string> programs, int exitCode)
        {
            return exitCode == ExitOk ? programs : new string[0];
        }

        private static void Block(Firewall firewall, string path, StringBuilder output)
        {
            Muter.Block(firewall, path);
            output.AppendLine(string.Format(L.T("{0} 已断网", "{0} is blocked"), Path.GetFileName(path)));
        }

        private static void Unblock(Firewall firewall, string path, StringBuilder output)
        {
            string format = Muter.Unblock(firewall, path)
                ? L.T("{0} 已恢复联网", "{0} is allowed again")
                : L.T("{0} 本来就没有断网", "{0} was not blocked");
            output.AppendLine(string.Format(format, Path.GetFileName(path)));
        }

        // A program that has since been deleted must still be possible to unblock.
        private static string ForgivingPath(string program)
        {
            try
            {
                return PathUtil.ToProgramPath(program);
            }
            catch (Exception)
            {
                return Path.GetFullPath(program.Trim().Trim('"'));
            }
        }

        private static void List(Firewall firewall, StringBuilder output)
        {
            List<BlockedApp> apps = firewall.Blocked();
            if (apps.Count == 0)
                output.AppendLine(L.T("没有断网的程序。", "No programs are blocked."));
            foreach (BlockedApp app in apps)
                output.AppendLine(app.Path);
        }

        // True when text written to the console will reach someone: output is redirected to a
        // file or pipe, or the program was started from a console window.
        private static bool HasOutput()
        {
            int type = GetFileType(GetStdHandle(StdOutputHandle));
            return type == FileTypeDisk || type == FileTypePipe || AttachConsole(AttachParentProcess);
        }

        private static void Present(string text, bool hasOutput, bool toast, bool blocked, int exitCode)
        {
            if (hasOutput)
            {
                Console.WriteLine();
                Console.WriteLine(text);
            }
            else if (toast && SystemInformation.UserInteractive)
            {
                Application.Run(new ToastForm(text, blocked));
            }
            else if (SystemInformation.UserInteractive)
            {
                MessageBox.Show(text, Ui.AppName, MessageBoxButtons.OK,
                    exitCode == ExitOk ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
        }
    }
}
