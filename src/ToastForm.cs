using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetMute
{
    // A short notice in the corner of the screen, for command-line use and desktop switches.
    // It takes no keyboard focus, so whatever you were doing carries on undisturbed.
    internal sealed class ToastForm : Form
    {
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int VisibleMilliseconds = 2600;

        private readonly Timer timer = new Timer();

        public ToastForm(string text, bool blocked)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = new Font(Ui.Font.FontFamily, 10.5F);

            Panel accent = new Panel();
            accent.Dock = DockStyle.Left;
            accent.Width = Ui.Scaled(6);
            accent.BackColor = blocked ? Ui.BlockedColor : Ui.AllowedColor;

            Label label = new Label();
            label.Dock = DockStyle.Fill;
            label.Padding = new Padding(Ui.Scaled(14), 0, Ui.Scaled(12), 0);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Text = text;
            label.AutoEllipsis = true;

            Controls.Add(label);
            Controls.Add(accent);

            Size = new Size(Ui.Scaled(340), Ui.Scaled(72));
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int margin = Ui.Scaled(16);
            Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);

            timer.Interval = VisibleMilliseconds;
            timer.Tick += delegate { Close(); };
            timer.Start();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.RoundCorners(Handle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
