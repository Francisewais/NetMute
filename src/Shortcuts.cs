using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NetMute
{
    // .lnk files, through the WScript.Shell object every Windows has.
    internal static class Shortcuts
    {
        public static string Resolve(string lnkPath)
        {
            object shell = CreateShell();
            object link = null;
            try
            {
                link = Call(shell, "CreateShortcut", lnkPath);
                return Get(link, "TargetPath") as string;
            }
            finally
            {
                Release(link);
                Release(shell);
            }
        }

        public static void Create(string lnkPath, string target, string arguments, string description, string icon)
        {
            object shell = CreateShell();
            object link = null;
            try
            {
                link = Call(shell, "CreateShortcut", lnkPath);
                Set(link, "TargetPath", target);
                Set(link, "Arguments", arguments);
                Set(link, "Description", description);
                Set(link, "IconLocation", icon);
                Call(link, "Save");
            }
            finally
            {
                Release(link);
                Release(shell);
            }
        }

        private static object CreateShell()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
        }

        private static object Get(object target, string property)
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new[] { value });
        }

        private static void Release(object comObject)
        {
            if (comObject != null)
                Marshal.ReleaseComObject(comObject);
        }
    }
}
