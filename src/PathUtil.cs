using System;
using System.Collections.Generic;
using System.IO;

namespace NetMute
{
    internal static class PathUtil
    {
        private const int MaxProgramsInFolder = 2000;

        public static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // Programs in the Windows folder are shared: svchost.exe alone carries the traffic of
        // many services, so the UI asks before blocking any of them.
        public static bool IsSystemProgram(string path)
        {
            return IsInside(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }

        public static bool IsInside(string path, string folder)
        {
            return path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Steam keeps every game below its own folder, and a game is not "part of" Steam.
        public static bool IsSteamGame(string path)
        {
            return path.IndexOf(@"\steamapps\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Whether "other" looks like part of the same installation as "program".
        public static bool SameInstall(string program, string other)
        {
            string folder = Path.GetDirectoryName(program);
            if (string.IsNullOrEmpty(folder) || folder.Length <= 3 || IsSystemProgram(program))
                return false;
            if (Same(folder, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
                Same(folder, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
                return false;
            return !Same(program, other) && !IsSteamGame(other) && IsInside(other, folder);
        }

        // Turns what a person gave us (an exe, a shortcut, or a bare name like "steam.exe" of a
        // running program) into a full exe path. Throws with a readable message otherwise.
        public static string ToProgramPath(string input)
        {
            string path = input.Trim().Trim('"');

            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(string.Format(L.T(
                    "{0} 是网址快捷方式（Steam 的游戏图标就是这种），请直接选游戏的 exe 文件。",
                    "{0} is an internet shortcut (Steam game icons are). Pick the game's exe file instead."),
                    Path.GetFileName(path)));

            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target = Shortcuts.Resolve(path);
                if (string.IsNullOrEmpty(target))
                    throw new InvalidOperationException(string.Format(L.T(
                        "{0} 没有指向具体的程序文件，请直接选 exe，或者从运行中的程序里选。",
                        "{0} does not point at a program file. Pick the exe, or choose from the running programs."),
                        Path.GetFileName(path)));
                path = target;
            }

            if (!File.Exists(path) && path.IndexOf('\\') < 0 && path.IndexOf('/') < 0)
                path = FindRunning(path) ?? path;

            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format(L.T("找不到程序：{0}", "Program not found: {0}"), path));

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(string.Format(L.T(
                    "{0} 不是程序文件（exe）。", "{0} is not a program file (exe)."), Path.GetFileName(path)));

            return Path.GetFullPath(path);
        }

        // Every exe below a folder. Skips folders we may not read and links that could loop.
        public static List<string> FindPrograms(string folder)
        {
            List<string> programs = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0 && programs.Count < MaxProgramsInFolder)
            {
                string current = pending.Pop();
                try
                {
                    programs.AddRange(Directory.GetFiles(current, "*.exe"));
                    foreach (string sub in Directory.GetDirectories(current))
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(sub);
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
            return programs;
        }

        private static string FindRunning(string fileName)
        {
            string wanted = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".exe";
            foreach (RunningApp app in ProcessInfo.Snapshot(false))
                if (Same(Path.GetFileName(app.Path), wanted))
                    return app.Path;
            return null;
        }
    }
}
