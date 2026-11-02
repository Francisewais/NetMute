using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NetMute
{
    internal sealed class MainForm : Form
    {
        private const int ColumnStatus = 1;
        private const int ColumnConnections = 2;

        private readonly Firewall firewall;
        private readonly List<string> programsToAdd;
        private readonly ListView list = new ListView();
        private readonly ImageList icons = Ui.NewIconList();
        private readonly Label status = new Label();
        private readonly Label emptyHint = new Label();
        private readonly Timer timer = new Timer();
        private bool syncing;

        // firewall is null only when Dev renders the window with sample rows.
        public MainForm(Firewall firewall, IEnumerable<string> programsToAdd)
        {
            this.firewall = firewall;
            this.programsToAdd = new List<string>(programsToAdd ?? new string[0]);

            SuspendLayout();
            Ui.PrepareWindow(this, 800, 480);
            Text = Ui.AppName + L.T(" · 程序断网开关", " · per-program network switch");
            MinimumSize = new Size(660, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(BuildLayout());
            ResumeLayout(false);
            PerformLayout();

            timer.Interval = 2000;
            timer.Tick += delegate { RefreshConnections(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.EnableFileDrop(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            List<string> files;
            if (Ui.TryReadFileDrop(ref m, out files))
            {
                // Runs after the drop has finished, so Explorer is not left waiting on a dialog.
                BeginInvoke((Action)delegate { AddPrograms(files); });
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Reload();
            if (firewall == null)
                return;
            timer.Start();
            Activated += delegate { Reload(); };
            if (programsToAdd.Count > 0)
                AddPrograms(programsToAdd);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                icons.Dispose();
            }
            base.Dispose(disposing);
        }

        private Control BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Margin = new Padding(0);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildToolbar(), 0, 0);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Margin = new Padding(0, 10, 0, 8);
            hint.Text = L.T(
                "勾选的程序不能联网，其它软件不受影响。规则保存在系统里，关掉本软件、重启电脑都继续有效。",
                "Checked programs have no network; everything else is left alone. Rules stay in Windows, even after closing this app or rebooting.");
            root.Controls.Add(hint, 0, 1);

            ConfigureList();
            root.Controls.Add(list, 0, 2);

            status.AutoSize = true;
            status.Margin = new Padding(0, 8, 0, 0);
            root.Controls.Add(status, 0, 3);
            return root;
        }

        private Control BuildToolbar()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.Margin = new Padding(0);
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel left = Ui.NewButtonRow();
            left.Controls.Add(Ui.NewButton(L.T("添加程序…", "Add program…"), delegate { OnAddProgram(); }));
            left.Controls.Add(Ui.NewButton(L.T("从运行中的程序选…", "Pick a running program…"), delegate { OnPickRunning(); }));
            left.Controls.Add(Ui.NewButton(L.T("添加文件夹…", "Add folder…"), delegate { OnAddFolder(); }));
            bar.Controls.Add(left, 0, 0);

            FlowLayoutPanel right = Ui.NewButtonRow();
            right.Controls.Add(Ui.NewButton(L.T("全部恢复联网", "Allow all"), delegate { OnAllowAll(); }));
            Button more = Ui.NewButton(L.T("更多 ▾", "More ▾"), null);
            more.Margin = new Padding(0);
            ContextMenuStrip moreMenu = BuildMoreMenu();
            more.Click += delegate { moreMenu.Show(more, new Point(0, more.Height)); };
            right.Controls.Add(more);
            bar.Controls.Add(right, 1, 0);
            return bar;
        }

        private void ConfigureList()
        {
            list.Dock = DockStyle.Fill;
            list.Margin = new Padding(0);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.CheckBoxes = true;
            list.HideSelection = false;
            list.ShowItemToolTips = true;
            list.Sorting = SortOrder.Ascending;
            list.SmallImageList = icons;
            list.Columns.Add(L.T("程序", "Program"), Ui.Scaled(210));
            list.Columns.Add(L.T("状态", "Status"), Ui.Scaled(90));
            list.Columns.Add(L.T("联网连接", "Connections"), Ui.Scaled(80));
            list.Columns.Add(L.T("位置", "Location"), Ui.Scaled(380));
            list.ContextMenuStrip = BuildRowMenu();
            list.HandleCreated += delegate { Ui.UseExplorerTheme(list); };
            list.ItemCheck += OnItemCheck;
            list.KeyDown += OnListKeyDown;
            list.Resize += delegate { Ui.FillLastColumn(list); };

            emptyHint.Dock = DockStyle.Fill;
            emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            emptyHint.BackColor = SystemColors.Window;
            emptyHint.ForeColor = SystemColors.GrayText;
            emptyHint.Text = L.T(
                "把程序（exe 或快捷方式）拖到这里，或者点上面的按钮添加。\n添加后立刻断网，取消勾选就恢复。",
                "Drop a program (exe or shortcut) here, or use the buttons above.\nIt loses its network right away. Uncheck it to give it back.");
            list.Controls.Add(emptyHint);
        }

        private ContextMenuStrip BuildMoreMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(L.T("运行自检…", "Run self-test…"), null, delegate { OnSelfTest(); });
            menu.Items.Add(L.T("彻底清除所有规则…", "Remove all rules…"), null, delegate { OnRemoveEverything(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("项目主页", "Project page"), null, delegate { OpenInShell(Program.HomePage); });
            menu.Items.Add(L.T("关于", "About"), null, delegate { OnAbout(); });
            return menu;
        }

        private ContextMenuStrip BuildRowMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(L.T("断网", "Block"), null, delegate { SetBlocked(SelectedPaths(), true, false); });
            menu.Items.Add(L.T("恢复联网", "Allow"), null, delegate { SetBlocked(SelectedPaths(), false, false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("在桌面创建一键开关", "Create a switch on the desktop"), null, delegate { OnCreateSwitch(); });
            menu.Items.Add(L.T("打开所在文件夹", "Open folder"), null, delegate { OnOpenFolder(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("从列表移除并恢复联网", "Remove from list and allow"), null, delegate { OnRemoveSelected(); });
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                e.Cancel = list.SelectedItems.Count == 0;
            };
            return menu;
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (syncing)
                return;

            // The box stays as it is until the rule has really changed, and dialogs must not
            // open from inside this notification.
            e.NewValue = e.CurrentValue;
            if (firewall == null)
                return;
            string path = (string)list.Items[e.Index].Tag;
            bool block = e.CurrentValue != CheckState.Checked;
            BeginInvoke((Action)delegate { SetBlocked(new[] { path }, block, false); });
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && list.SelectedItems.Count > 0)
                OnRemoveSelected();
        }

        private void OnAddProgram()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = L.T("选择要断网的程序", "Choose a program to block");
                dialog.Filter = L.T("程序或快捷方式", "Programs and shortcuts") + "|*.exe;*.lnk";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddPrograms(dialog.FileNames);
            }
        }

        private void OnPickRunning()
        {
            HashSet<string> blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BlockedApp app in firewall.Blocked())
                blocked.Add(app.Path);

            using (PickerForm picker = new PickerForm(blocked))
            {
                if (picker.ShowDialog(this) == DialogResult.OK)
                    SetBlocked(picker.SelectedPaths, true, true);
            }
        }

        private void OnAddFolder()
        {
            string folder = FolderPicker.Show(this, L.T("选择文件夹，里面的程序都可以断网", "Choose a folder of programs to block"));
            if (folder != null)
                AddPrograms(new[] { folder });
        }

        private void OnAllowAll()
        {
            SetBlocked(firewall.Blocked().Select(app => app.Path).ToList(), false, false);
        }

        private void OnRemoveSelected()
        {
            List<string> failures = new List<string>();
            foreach (string path in SelectedPaths())
            {
                try
                {
                    Muter.Forget(firewall, path);
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            Reload();
            if (failures.Count > 0)
                Ui.Error(this, string.Join("\n", failures));
        }

        private void OnCreateSwitch()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                foreach (string path in SelectedPaths())
                {
                    string name = L.T("断网开关 - ", "Network switch - ") + Path.GetFileNameWithoutExtension(path) + ".lnk";
                    Shortcuts.Create(
                        Path.Combine(desktop, name),
                        Application.ExecutablePath,
                        "toggle \"" + path + "\"",
                        L.T("切换这个程序的断网状态", "Switch this program's network on or off"),
                        Application.ExecutablePath + ",0");
                }
                Ui.Info(this, L.T(
                    "已在桌面创建。双击它就能在断网和联网之间切换，每次会弹一次管理员确认。\n之后不要移动 NetMute.exe，否则这个开关会失效。",
                    "Created on your desktop. Double-click it to switch between blocked and allowed; Windows asks for confirmation each time.\nDon't move NetMute.exe afterwards, or the switch stops working."));
            }
            catch (Exception ex)
            {
                Ui.Error(this, ex.Message);
            }
        }

        private void OnOpenFolder()
        {
            foreach (string path in SelectedPaths())
                if (File.Exists(path))
                    OpenInShell("/select,\"" + path + "\"");
        }

        private void OnSelfTest()
        {
            try
            {
                Process.Start(Application.ExecutablePath, "selftest").Dispose();
                Ui.Info(this, L.T(
                    "自检已在后台开始，大约需要一分钟，结束后会弹出结果。",
                    "The self-test is running in the background. It takes about a minute and shows its result when done."));
            }
            catch (Exception ex)
            {
                Ui.Error(this, ex.Message);
            }
        }

        private void OnRemoveEverything()
        {
            if (!Ui.Confirm(this, L.T(
                "这会让所有程序恢复联网，清空列表，并删除 NetMute 留在系统里的全部内容。\n清除后可以直接删掉 NetMute.exe。确定吗？",
                "This allows every program again, empties the list, and removes everything NetMute left in Windows.\nYou can delete NetMute.exe afterwards. Continue?")))
                return;
            try
            {
                Muter.RemoveEverything(firewall);
                Reload();
                Ui.Info(this, L.T("已全部清除。", "All removed."));
            }
            catch (Exception ex)
            {
                Ui.Error(this, ex.Message);
            }
        }

        private void OnAbout()
        {
            Ui.Info(this, Ui.AppName + " " + Program.Version + "\n\n" + L.T(
                "单独关掉某个程序的网络，其它软件不受影响。\n开源协议：MIT\n",
                "Cut off the network for single programs and leave the rest alone.\nLicense: MIT\n") + Program.HomePage);
        }

        // Going through explorer.exe opens the target with normal rights, not our elevated ones.
        private static void OpenInShell(string argument)
        {
            Process.Start("explorer.exe", argument).Dispose();
        }

        // Takes exe files, shortcuts and folders, and blocks what they lead to.
        private void AddPrograms(IEnumerable<string> items)
        {
            List<string> programs = new List<string>();
            List<string> problems = new List<string>();
            foreach (string item in items)
            {
                if (Directory.Exists(item))
                {
                    programs.AddRange(ChooseFromFolder(item));
                    continue;
                }
                try
                {
                    programs.Add(PathUtil.ToProgramPath(item));
                }
                catch (Exception ex)
                {
                    problems.Add(ex.Message);
                }
            }
            if (problems.Count > 0)
                Ui.Error(this, string.Join("\n", problems));
            if (programs.Count > 0)
                SetBlocked(programs, true, true);
        }

        private List<string> ChooseFromFolder(string folder)
        {
            List<string> found = PathUtil.FindPrograms(folder);
            if (found.Count == 0)
            {
                Ui.Info(this, string.Format(L.T("{0} 里没有找到程序。", "No programs found in {0}."), folder));
                return found;
            }

            List<KeyValuePair<string, bool>> entries = found
                .Select(path => new KeyValuePair<string, bool>(path, !PathUtil.IsSteamGame(path)))
                .ToList();
            string title = L.T("选择要断网的程序", "Choose programs to block");
            string message = string.Format(L.T(
                "在 {0} 里找到 {1} 个程序：",
                "Found {1} programs in {0}:"), folder, found.Count);

            using (ChecklistForm dialog = new ChecklistForm(title, message, entries, L.T("断网所选", "Block selected")))
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.CheckedPaths : new List<string>();
        }

        private void SetBlocked(IEnumerable<string> paths, bool block, bool offerRelated)
        {
            List<string> done = new List<string>();
            List<string> failures = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    if (!block)
                        Muter.Unblock(firewall, path);
                    else if (!PathUtil.IsSystemProgram(path) || ConfirmSystemProgram(path))
                        Muter.Block(firewall, path);
                    else
                        continue;
                    done.Add(path);
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            Reload();
            if (failures.Count > 0)
                Ui.Error(this, string.Join("\n", failures));
            if (block && offerRelated)
                OfferRelated(done);
        }

        private bool ConfirmSystemProgram(string path)
        {
            return Ui.Confirm(this, string.Format(L.T(
                "{0} 是 Windows 系统程序。系统服务可能共用它，断网后可能影响其它软件，甚至整台电脑上不了网。\n确定要断网吗？",
                "{0} is part of Windows. System services may share it, so blocking it can affect other software or even the whole computer's network.\nBlock it anyway?"),
                Path.GetFileName(path)));
        }

        // A program often has helpers that do the networking. steam.exe, for one, leaves the
        // store pages to steamwebhelper.exe, so blocking steam.exe alone would not silence it.
        private void OfferRelated(List<string> justBlocked)
        {
            if (justBlocked.Count == 0)
                return;

            HashSet<string> blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BlockedApp app in firewall.Blocked())
                blocked.Add(app.Path);

            List<KeyValuePair<string, bool>> related = ProcessInfo.Snapshot(false)
                .Where(app => app.Connections > 0 && !blocked.Contains(app.Path)
                    && justBlocked.Any(program => PathUtil.SameInstall(program, app.Path)))
                .Select(app => new KeyValuePair<string, bool>(app.Path, true))
                .ToList();
            if (related.Count == 0)
                return;

            string title = L.T("还有相关程序在联网", "Related programs are online");
            string message = string.Format(L.T(
                "{0} 同一目录下的这些程序也在联网，要一起断网吗？",
                "These programs from the same folder as {0} are also online. Block them too?"),
                Path.GetFileName(justBlocked[0]));

            using (ChecklistForm dialog = new ChecklistForm(title, message, related, L.T("一起断网", "Block them too")))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetBlocked(dialog.CheckedPaths, true, false);
            }
        }

        private List<string> SelectedPaths()
        {
            List<string> paths = new List<string>();
            foreach (ListViewItem item in list.SelectedItems)
                paths.Add((string)item.Tag);
            return paths;
        }

        // Brings the rows in line with what Windows is actually enforcing.
        private void Reload()
        {
            List<string> paths;
            HashSet<string> blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (firewall == null)
                {
                    paths = DevSamples.Paths;
                    blocked.UnionWith(DevSamples.Blocked);
                }
                else
                {
                    paths = Store.Load();
                    foreach (BlockedApp app in firewall.Blocked())
                    {
                        blocked.Add(app.Path);
                        if (!paths.Any(p => PathUtil.Same(p, app.Path)))
                            paths.Add(app.Path);
                    }
                }
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                return;
            }

            syncing = true;
            list.BeginUpdate();
            try
            {
                for (int i = list.Items.Count - 1; i >= 0; i--)
                    if (!paths.Any(p => PathUtil.Same(p, (string)list.Items[i].Tag)))
                        list.Items.RemoveAt(i);

                foreach (string path in paths)
                    UpdateRow(FindRow(path) ?? AddRow(path), blocked.Contains(path));
            }
            finally
            {
                list.EndUpdate();
                syncing = false;
            }

            emptyHint.Visible = list.Items.Count == 0;
            Ui.FillLastColumn(list);
            RefreshConnections();

            status.Text = string.Format(L.T("共 {0} 个程序，{1} 个已断网", "{0} programs, {1} blocked"), list.Items.Count, blocked.Count);
        }

        private ListViewItem FindRow(string path)
        {
            foreach (ListViewItem item in list.Items)
                if (PathUtil.Same((string)item.Tag, path))
                    return item;
            return null;
        }

        private ListViewItem AddRow(string path)
        {
            ListViewItem item = new ListViewItem(Path.GetFileName(path));
            item.Tag = path;
            item.UseItemStyleForSubItems = false;
            item.ImageKey = Ui.IconKey(icons, path);
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add(path);
            item.ToolTipText = path;
            list.Items.Add(item);
            return item;
        }

        private void UpdateRow(ListViewItem item, bool blocked)
        {
            item.Checked = blocked;
            ListViewItem.ListViewSubItem cell = item.SubItems[ColumnStatus];
            bool missing = firewall != null && !File.Exists((string)item.Tag);
            if (missing && !blocked)
            {
                cell.Text = L.T("文件不存在", "File missing");
                cell.ForeColor = SystemColors.GrayText;
            }
            else
            {
                cell.Text = blocked ? L.T("已断网", "Blocked") : L.T("可联网", "Allowed");
                cell.ForeColor = blocked ? Ui.BlockedColor : Ui.AllowedColor;
            }
        }

        private void RefreshConnections()
        {
            if (firewall == null)
            {
                DevSamples.FillConnections(list, ColumnConnections);
                return;
            }
            if (WindowState == FormWindowState.Minimized)
                return;

            Dictionary<string, RunningApp> running = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
            foreach (RunningApp app in ProcessInfo.Snapshot(false))
                running[app.Path] = app;

            foreach (ListViewItem item in list.Items)
            {
                RunningApp app;
                string text = running.TryGetValue((string)item.Tag, out app)
                    ? app.Connections.ToString()
                    : L.T("未运行", "Not running");
                ListViewItem.ListViewSubItem cell = item.SubItems[ColumnConnections];
                if (cell.Text != text)
                    cell.Text = text;
            }
        }
    }
}
