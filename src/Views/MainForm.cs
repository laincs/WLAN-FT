using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WlanFt
{
    internal sealed class MainForm : Form
    {
        private readonly Color background = Color.FromArgb(17, 22, 34);
        private readonly Color panelColor = Color.FromArgb(27, 35, 51);
        private readonly Color muted = Color.FromArgb(159, 174, 193);
        private readonly Color accent = Color.FromArgb(90, 158, 255);
        private readonly ListView sharedList = new ListView();
        private readonly ListView remoteList = new ListView();
        private readonly Label status = new Label();
        private readonly Label drop = new Label();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private ShareController controller;

        public MainForm()
        {
            Text = "WLAN-FT";
            Font = new Font("Segoe UI", 10F);
            BackColor = background;
            ForeColor = Color.White;
            MinimumSize = new Size(800, 620);
            Size = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 20, 28, 18),
                ColumnCount = 1,
                RowCount = 5,
                BackColor = background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill };
            Label title = new Label
            {
                Text = "WLAN-FT",
                Font = new Font("Segoe UI Semibold", 22F),
                AutoSize = true,
                Location = new Point(0, 0)
            };
            Label subtitle = new Label
            {
                Text = "File sharing, made simple.",
                ForeColor = muted,
                AutoSize = true,
                Location = new Point(2, 45)
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            layout.Controls.Add(header, 0, 0);

            drop.Text = "Drag files here to share them";
            drop.Dock = DockStyle.Fill;
            drop.Margin = new Padding(0, 4, 0, 12);
            drop.TextAlign = ContentAlignment.MiddleCenter;
            drop.BackColor = Color.FromArgb(37, 52, 74);
            drop.ForeColor = Color.FromArgb(195, 220, 255);
            drop.Font = new Font("Segoe UI Semibold", 12F);
            drop.AllowDrop = true;
            drop.DragEnter += DragEnterFiles;
            drop.DragDrop += DropFiles;
            layout.Controls.Add(drop, 0, 1);

            AllowDrop = true;
            DragEnter += DragEnterFiles;
            DragDrop += DropFiles;

            layout.Controls.Add(BuildSection("Your files", "Available while this window is open", sharedList,
                "Add files", AddFiles, "Stop sharing", RemoveFiles), 0, 2);
            layout.Controls.Add(BuildSection("On your network", "Files shared by other devices", remoteList,
                "Download", DownloadSelected, null, null), 0, 3);

            status.Text = "Searching for devices on the local network...";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = muted;
            status.AutoEllipsis = true;
            layout.Controls.Add(status, 0, 4);

            Shown += StartService;
            FormClosing += delegate
            {
                refresh.Stop();
                if (controller != null) controller.Dispose();
            };
        }

        private Control BuildSection(string title, string description, ListView list, string action, EventHandler click, string secondAction, EventHandler secondClick)
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = panelColor,
                Padding = new Padding(16),
                Margin = new Padding(0, 0, 0, 10)
            };
            TableLayoutPanel inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = panelColor
            };

            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(inner);

            Panel heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 13F),
                AutoSize = true,
                Location = new Point(0, 0),
                ForeColor = Color.White
            });
            heading.Controls.Add(new Label { Text = description, ForeColor = muted, AutoSize = true, Location = new Point(1, 28) });
            inner.Controls.Add(heading, 0, 0);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            if (secondAction != null) buttons.Controls.Add(MakeButton(secondAction, secondClick, false));
            buttons.Controls.Add(MakeButton(action, click, true));
            inner.Controls.Add(buttons, 1, 0);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = true;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.FromArgb(32, 41, 58);
            list.ForeColor = Color.White;
            list.Columns.Add("File", 310);
            list.Columns.Add("Size", 105);
            list.Columns.Add(list == remoteList ? "Device" : "Location", 320);
            if (list == remoteList) list.DoubleClick += DownloadSelected;
            inner.SetColumnSpan(list, 2);
            inner.Controls.Add(list, 0, 1);

            return card;
        }

        private Button MakeButton(string text, EventHandler click, bool primary)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 36,
                Padding = new Padding(10, 0, 10, 0),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? accent : Color.FromArgb(54, 65, 83),
                ForeColor = primary ? background : Color.White,
                Cursor = Cursors.Hand
            };

            button.FlatAppearance.BorderSize = 0;
            button.Click += click;
            return button;
        }

        private void StartService(object sender, EventArgs e)
        {
            try
            {
                controller = new ShareController(new ShareService());
                controller.StatusChanged += SetStatus;
                controller.DownloadFailed += ShowDownloadError;
                refresh.Interval = 1000;
                refresh.Tick += delegate { RefreshRemoteFiles(); };
                refresh.Start();

                SetStatus("Ready. Searching for devices on the local network...");
            }
            catch (Exception ex)
            {
                status.Text = "Could not start the service.";
                MessageBox.Show(this, "Could not start the network service:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(SetStatus), message);
                }
                catch (InvalidOperationException) { }

                return;
            }

            status.Text = message;
        }

        private void DragEnterFiles(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void DropFiles(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) ShareFiles(paths);
        }

        private void AddFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to share" })
                if (dialog.ShowDialog(this) == DialogResult.OK) ShareFiles(dialog.FileNames);
        }

        private void ShareFiles(string[] paths)
        {
            if (controller == null)
            {
                MessageBox.Show(this, "The network service is not available.", Text);
                return;
            }

            foreach (string path in paths)
            {
                try
                {
                    FileRecord file = controller.Share(path);
                    ListViewItem item = new ListViewItem(file.Name);
                    item.SubItems.Add(FormatSize(file.Size));
                    item.SubItems.Add(path);
                    item.Tag = file;
                    sharedList.Items.Add(item);
                    SetStatus("Sharing " + file.Name + ".");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not share " + path + ":\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void RemoveFiles(object sender, EventArgs e)
        {
            if (controller == null) return;

            foreach (ListViewItem item in sharedList.SelectedItems)
            {
                controller.Remove(((FileRecord)item.Tag).Id);
                sharedList.Items.Remove(item);
            }

            SetStatus("The selected files are no longer shared.");
        }

        private void RefreshRemoteFiles()
        {
            if (controller == null) return;

            HashSet<string> selected = new HashSet<string>();
            foreach (ListViewItem item in remoteList.SelectedItems)
            {
                RemoteFile remote = (RemoteFile)item.Tag;
                selected.Add(remote.Address + ":" + remote.Port + ":" + remote.File.Id);
            }

            remoteList.BeginUpdate();
            remoteList.Items.Clear();

            foreach (RemoteFile remote in controller.GetRemoteFiles())
            {
                ListViewItem item = new ListViewItem(remote.File.Name);
                item.SubItems.Add(FormatSize(remote.File.Size));
                item.SubItems.Add(String.IsNullOrEmpty(remote.Computer) ? remote.Address.ToString() : remote.Computer);
                item.Tag = remote;
                remoteList.Items.Add(item);
                item.Selected = selected.Contains(remote.Address + ":" + remote.Port + ":" + remote.File.Id);
            }

            remoteList.EndUpdate();
        }

        private static string FormatSize(long size)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = size;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(unit == 0 ? "0" : "0.##") + " " + units[unit];
        }

        private void DownloadSelected(object sender, EventArgs e)
        {
            if (controller == null)
            {
                MessageBox.Show(this, "The network service is not available.", Text);
                return;
            }

            if (controller.IsDownloading)
            {
                MessageBox.Show(this, "Wait for the current download to finish.", Text);
                return;
            }

            if (remoteList.SelectedItems.Count != 1)
            {
                MessageBox.Show(this, "Select a file to download.", Text);
                return;
            }

            RemoteFile remote = (RemoteFile)remoteList.SelectedItems[0].Tag;
            string name = Path.GetFileName(remote.File.Name);
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            if (String.IsNullOrWhiteSpace(name)) name = "file";

            using (SaveFileDialog dialog = new SaveFileDialog { FileName = name, Title = "Save file", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                controller.Download(remote, dialog.FileName);
            }
        }

        private void ShowDownloadError(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(ShowDownloadError), message);
                }
                catch (InvalidOperationException) { }

                return;
            }

            status.Text = "Could not complete the download.";
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
