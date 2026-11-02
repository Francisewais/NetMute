using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NetMute
{
    // The programs shown in the list, so one you have switched back on stays there for next
    // time. Which programs are blocked is not kept here: WFP itself is the record of that.
    internal static class Store
    {
        private static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetMute");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(Folder, "programs.txt"); }
        }

        public static List<string> Load()
        {
            List<string> paths = new List<string>();
            try
            {
                if (!File.Exists(FilePath))
                    return paths;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string path = line.Trim();
                    if (path.Length > 0 && !Contains(paths, path))
                        paths.Add(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return paths;
        }

        public static void Add(string path)
        {
            List<string> paths = Load();
            if (Contains(paths, path))
                return;
            paths.Add(path);
            Save(paths);
        }

        public static void Remove(string path)
        {
            List<string> paths = Load();
            paths.RemoveAll(delegate(string p) { return PathUtil.Same(p, path); });
            Save(paths);
        }

        public static void Delete()
        {
            try
            {
                if (Directory.Exists(Folder))
                    Directory.Delete(Folder, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // Losing the list is an inconvenience, not a reason to fail a block that already worked.
        private static void Save(List<string> paths)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, paths, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool Contains(List<string> paths, string path)
        {
            return paths.Exists(delegate(string p) { return PathUtil.Same(p, path); });
        }
    }
}
