using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NetMute
{
    internal static class Ui
    {
        public const string AppName = "NetMute";

        public static readonly Font Font = new Font(L.IsChinese ? "Microsoft YaHei UI" : "Segoe UI", 9F);
        public static readonly Color BlockedColor = Color.FromArgb(196, 43, 28);
        public static readonly Color AllowedColor = Color.FromArgb(16, 124, 16);

        private const int EM_SETCUEBANNER = 0x1501;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        private const int WM_DROPFILES = 0x0233;
        private const int WM_COPYDATA = 0x004A;
        private const int WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ALLOW = 1;

        private static readonly float DpiScale = ReadDpiScale();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, string lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int message, uint action, IntPtr filterStatus);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder file, int size);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr drop);

        public static Icon AppIcon
        {
            get
            {
                using (Stream stream = typeof(Ui).Assembly.GetManifestResourceStream("NetMute.app.ico"))
                    return new Icon(stream);
            }
        }

        public static int Scaled(int value)
        {
            return (int)Math.Round(value * DpiScale);
        }

        public static void Error(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Info(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Confirm(IWin32Window owner, string message)
        {
            return MessageBox.Show(owner, message, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static void UseExplorerTheme(Control control)
        {
            SetWindowTheme(control.Handle, "Explorer", null);
        }

        public static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, new IntPtr(1), text);
        }

        // Windows 11 only; Windows 10 reports an error and draws square corners, which is fine.
        public static void RoundCorners(IntPtr hwnd)
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        // WinForms drag and drop cannot work here: Windows does not let normal-rights Explorer
        // talk OLE to an elevated window. The older WM_DROPFILES route works once these messages
        // are let through.
        public static void EnableFileDrop(IntPtr hwnd)
        {
            ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
            DragAcceptFiles(hwnd, true);
        }

        public static bool TryReadFileDrop(ref Message message, out List<string> files)
        {
            files = null;
            if (message.Msg != WM_DROPFILES)
                return false;

            files = new List<string>();
            uint count = DragQueryFile(message.WParam, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                StringBuilder file = new StringBuilder(32768);
                if (DragQueryFile(message.WParam, i, file, file.Capacity) > 0)
                    files.Add(file.ToString());
            }
            DragFinish(message.WParam);
            return true;
        }

        public static Bitmap ProgramIcon(string path, int size)
        {
            Icon icon = null;
            try
            {
                if (File.Exists(path))
                    icon = Icon.ExtractAssociatedIcon(path);
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }

            using (Icon owned = icon ?? (Icon)SystemIcons.Application.Clone())
            using (Bitmap source = owned.ToBitmap())
            {
                Bitmap result = new Bitmap(size, size);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                }
                return result;
            }
        }

        // Adds the program's icon to the list once, and returns the key to use it by.
        public static string IconKey(ImageList icons, string path)
        {
            if (!icons.Images.ContainsKey(path))
                icons.Images.Add(path, ProgramIcon(path, icons.ImageSize.Width));
            return path;
        }

        public static ImageList NewIconList()
        {
            ImageList icons = new ImageList();
            icons.ColorDepth = ColorDepth.Depth32Bit;
            int size = Scaled(20);
            icons.ImageSize = new Size(size, size);
            return icons;
        }

        // Gives the last column whatever width the others leave over.
        public static void FillLastColumn(ListView list)
        {
            int others = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
                others += list.Columns[i].Width;
            int available = list.ClientSize.Width - others;
            if (available > Scaled(120))
                list.Columns[list.Columns.Count - 1].Width = available;
        }

        public static Button NewButton(string text, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(10, 3, 10, 3);
            button.Margin = new Padding(0, 0, 8, 0);
            button.UseVisualStyleBackColor = true;
            if (onClick != null)
                button.Click += onClick;
            return button;
        }

        public static FlowLayoutPanel NewButtonRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = false;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0);
            return row;
        }

        public static void PrepareWindow(Form form, int width, int height)
        {
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Font = Font;
            form.Icon = AppIcon;
            form.ClientSize = new Size(width, height);
            form.Padding = new Padding(12);
        }

        private static float ReadDpiScale()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96F;
        }
    }
}
