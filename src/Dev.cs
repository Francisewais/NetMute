using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NetMute
{
    // Sample rows for rendering the window without administrator rights.
    internal static class DevSamples
    {
        public static readonly List<string> Paths = new List<string>
        {
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"D:\Games\Example\Game.exe"
        };

        public static readonly List<string> Blocked = new List<string>
        {
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe"
        };

        public static void Clear()
        {
            Paths.Clear();
            Blocked.Clear();
        }

        public static void FillConnections(ListView list, int column)
        {
            foreach (ListViewItem item in list.Items)
            {
                string name = item.Text;
                item.SubItems[column].Text =
                    name == "msedge.exe" ? "27" :
                    name == "Game.exe" ? L.T("未运行", "Not running") : "0";
            }
        }
    }

    // "NetMute render <main|empty|picker|checklist|toast> <file.png>" draws a window off screen and
    // saves a picture of it: for checking layouts and for the README screenshot.
    internal static class Dev
    {
        private const uint PW_RENDERFULLCONTENT = 2;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int PaintWaitMilliseconds = 600;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        public static int Render(string kind, string outputPath)
        {
            using (Form form = CreateForm(kind))
            {
                if (form == null)
                    return 2;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < PaintWaitMilliseconds)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }

                using (Bitmap picture = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                {
                    // PrintWindow includes the themed title bar, but gives borderless windows
                    // back all black, and those have no title bar to miss.
                    if (form.FormBorderStyle == FormBorderStyle.None)
                    {
                        form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size));
                        picture.Save(outputPath, ImageFormat.Png);
                    }
                    else
                    {
                        using (Graphics g = Graphics.FromImage(picture))
                        {
                            IntPtr hdc = g.GetHdc();
                            PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT);
                            g.ReleaseHdc(hdc);
                        }
                        using (Bitmap visible = picture.Clone(VisibleArea(form), picture.PixelFormat))
                            visible.Save(outputPath, ImageFormat.Png);
                    }
                }
                form.Close();
            }
            return 0;
        }

        // Windows counts an invisible resize border as part of the window; leave it out.
        private static Rectangle VisibleArea(Form form)
        {
            RECT window;
            RECT frame;
            int size = Marshal.SizeOf(typeof(RECT));
            if (!GetWindowRect(form.Handle, out window) ||
                DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, size) != 0)
                return new Rectangle(Point.Empty, form.Size);
            return new Rectangle(frame.Left - window.Left, frame.Top - window.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
        }

        private static Form CreateForm(string kind)
        {
            switch (kind)
            {
                case "main":
                    return new MainForm(null, null);
                case "empty":
                    DevSamples.Clear();
                    return new MainForm(null, null);
                case "picker":
                    return new PickerForm(new string[0]);
                case "checklist":
                    List<KeyValuePair<string, bool>> entries = new List<KeyValuePair<string, bool>>();
                    foreach (string path in DevSamples.Paths)
                        entries.Add(new KeyValuePair<string, bool>(path, true));
                    return new ChecklistForm(
                        L.T("还有相关程序在联网", "Related programs are online"),
                        L.T("steam.exe 同一目录下的这些程序也在联网，要一起断网吗？", "These programs from the same folder as steam.exe are also online. Block them too?"),
                        entries,
                        L.T("一起断网", "Block them too"));
                case "toast":
                    return new ToastForm(L.T("steam.exe 已断网", "steam.exe is blocked"), true);
                default:
                    return null;
            }
        }
    }
}
