using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NetMute
{
    // Lists running programs, the busiest on the network first, to pick from.
    internal sealed class PickerForm : Form
    {
        private readonly HashSet<string> hidden;
        private readonly TextBox search = new TextBox();
        private readonly CheckBox showSystem = new CheckBox();
        private readonly ListView list = new ListView();
        private readonly ImageList icons = Ui.NewIconList();
        private readonly Button accept;
        private List<RunningApp> apps = new List<RunningApp>();

        public PickerForm(ICollection<string> alreadyBlocked)
        {
            hidden = new HashSet<string>(alreadyBlocked, StringComparer.OrdinalIgnoreCase);
            hidden.Add(Application.ExecutablePath);

            SuspendLayout();
            Ui.PrepareWindow(this, 760, 480);
            Text = L.T("从运行中的程序选", "Pick a running program");
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            accept = Ui.NewButton(L.T("断网所选", "Block selected"), null);
            accept.DialogResult = DialogResult.OK;
            accept.Enabled = false;
            AcceptButton = accept;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildSearchBar(), 0, 0);

            ConfigureList();
            root.Controls.Add(list, 0, 1);
            root.Controls.Add(BuildButtons(), 0, 2);
            Controls.Add(root);

            ResumeLayout(false);
            PerformLayout();
            Reload();
        }

        public List<string> SelectedPaths
        {
            get
            {
                List<string> paths = new List<string>();
                foreach (ListViewItem item in list.SelectedItems)
                    paths.Add((string)item.Tag);
                return paths;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            search.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                icons.Dispose();
            base.Dispose(disposing);
        }

        private Control BuildSearchBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 3;
            bar.RowCount = 1;
            bar.Margin = new Padding(0, 0, 0, 8);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            search.Dock = DockStyle.Fill;
            search.Margin = new Padding(0, 1, 12, 0);
            search.HandleCreated += delegate
            {
                Ui.SetCueBanner(search, L.T("按名称、窗口标题或路径搜索", "Search by name, window title or path"));
            };
            search.TextChanged += delegate { Fill(); };
            bar.Controls.Add(search, 0, 0);

            showSystem.Text = L.T("显示 Windows 系统程序", "Show Windows programs");
            showSystem.AutoSize = true;
            showSystem.Anchor = AnchorStyles.Left;
            showSystem.Margin = new Padding(0, 0, 12, 0);
            showSystem.CheckedChanged += delegate { Fill(); };
            bar.Controls.Add(showSystem, 1, 0);

            Button refresh = Ui.NewButton(L.T("刷新", "Refresh"), delegate { Reload(); });
            refresh.Margin = new Padding(0);
            bar.Controls.Add(refresh, 2, 0);
            return bar;
        }

        private void ConfigureList()
        {
            list.Dock = DockStyle.Fill;
            list.Margin = new Padding(0);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.SmallImageList = icons;
            list.Columns.Add(L.T("程序", "Program"), Ui.Scaled(190));
            list.Columns.Add(L.T("联网连接", "Connections"), Ui.Scaled(70));
            list.Columns.Add(L.T("窗口标题", "Window title"), Ui.Scaled(180));
            list.Columns.Add(L.T("位置", "Location"), Ui.Scaled(290));
            list.HandleCreated += delegate { Ui.UseExplorerTheme(list); };
            list.Resize += delegate { Ui.FillLastColumn(list); };
            list.SelectedIndexChanged += delegate { accept.Enabled = list.SelectedItems.Count > 0; };
            list.ItemActivate += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };
        }

        private Control BuildButtons()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.Margin = new Padding(0, 10, 0, 0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label hint = new Label();
            hint.AutoSize = true;
            hint.Anchor = AnchorStyles.Left;
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = L.T("联网连接越多的排得越靠前。按住 Ctrl 可以多选。", "The busiest programs come first. Hold Ctrl to select several.");
            bar.Controls.Add(hint, 0, 0);

            FlowLayoutPanel buttons = Ui.NewButtonRow();
            buttons.Controls.Add(accept);
            Button cancel = Ui.NewButton(L.T("取消", "Cancel"), null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Margin = new Padding(0);
            CancelButton = cancel;
            buttons.Controls.Add(cancel);
            bar.Controls.Add(buttons, 1, 0);
            return bar;
        }

        private void Reload()
        {
            apps = ProcessInfo.Snapshot(true);
            Fill();
        }

        private void Fill()
        {
            string query = search.Text.Trim();
            IEnumerable<RunningApp> visible = apps
                .Where(app => !hidden.Contains(app.Path))
                .Where(app => showSystem.Checked || !PathUtil.IsSystemProgram(app.Path))
                .Where(app => query.Length == 0 || Matches(app, query))
                .OrderByDescending(app => app.Connections)
                .ThenBy(app => Path.GetFileName(app.Path), StringComparer.OrdinalIgnoreCase);

            list.BeginUpdate();
            list.Items.Clear();
            foreach (RunningApp app in visible)
            {
                ListViewItem item = new ListViewItem(Path.GetFileName(app.Path));
                item.Tag = app.Path;
                item.ImageKey = Ui.IconKey(icons, app.Path);
                item.SubItems.Add(app.Connections.ToString());
                item.SubItems.Add(app.Title);
                item.SubItems.Add(app.Path);
                list.Items.Add(item);
            }
            list.EndUpdate();
            Ui.FillLastColumn(list);
            accept.Enabled = false;
        }

        private static bool Matches(RunningApp app, string query)
        {
            return app.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || app.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
