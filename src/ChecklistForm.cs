using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NetMute
{
    // A list of programs with check boxes, for choosing which of them to act on.
    internal sealed class ChecklistForm : Form
    {
        private readonly ListView list = new ListView();
        private readonly ImageList icons = Ui.NewIconList();

        public ChecklistForm(string title, string message, IEnumerable<KeyValuePair<string, bool>> entries, string acceptText)
        {
            SuspendLayout();
            Ui.PrepareWindow(this, 640, 400);
            Text = title;
            MinimumSize = new Size(460, 280);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label label = new Label();
            label.AutoSize = true;
            label.Margin = new Padding(0, 0, 0, 8);
            label.MaximumSize = new Size(610, 0);
            label.Text = message;
            root.Controls.Add(label, 0, 0);

            list.Dock = DockStyle.Fill;
            list.Margin = new Padding(0);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.CheckBoxes = true;
            list.SmallImageList = icons;
            list.Columns.Add(L.T("程序", "Program"), Ui.Scaled(190));
            list.Columns.Add(L.T("位置", "Location"), Ui.Scaled(410));
            list.HandleCreated += delegate { Ui.UseExplorerTheme(list); };
            list.Resize += delegate { Ui.FillLastColumn(list); };
            foreach (KeyValuePair<string, bool> entry in entries)
            {
                ListViewItem item = new ListViewItem(Path.GetFileName(entry.Key));
                item.Tag = entry.Key;
                item.Checked = entry.Value;
                item.ImageKey = Ui.IconKey(icons, entry.Key);
                item.SubItems.Add(entry.Key);
                list.Items.Add(item);
            }
            root.Controls.Add(list, 0, 1);
            root.Controls.Add(BuildButtons(acceptText), 0, 2);
            Controls.Add(root);

            ResumeLayout(false);
            PerformLayout();
        }

        public List<string> CheckedPaths
        {
            get
            {
                List<string> paths = new List<string>();
                foreach (ListViewItem item in list.CheckedItems)
                    paths.Add((string)item.Tag);
                return paths;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                icons.Dispose();
            base.Dispose(disposing);
        }

        private Control BuildButtons(string acceptText)
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

            FlowLayoutPanel left = Ui.NewButtonRow();
            left.Controls.Add(Ui.NewButton(L.T("全选", "Select all"), delegate { SetAll(true); }));
            left.Controls.Add(Ui.NewButton(L.T("全不选", "Select none"), delegate { SetAll(false); }));
            bar.Controls.Add(left, 0, 0);

            FlowLayoutPanel right = Ui.NewButtonRow();
            Button accept = Ui.NewButton(acceptText, null);
            accept.DialogResult = DialogResult.OK;
            AcceptButton = accept;
            right.Controls.Add(accept);
            Button cancel = Ui.NewButton(L.T("取消", "Cancel"), null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Margin = new Padding(0);
            CancelButton = cancel;
            right.Controls.Add(cancel);
            bar.Controls.Add(right, 1, 0);
            return bar;
        }

        private void SetAll(bool value)
        {
            foreach (ListViewItem item in list.Items)
                item.Checked = value;
        }
    }
}
