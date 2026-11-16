using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WlanFt
{
    internal sealed class MainForm : Form
    {
        private const int TopPadding = 20;
        private readonly Color background = Color.FromArgb(17, 22, 34);
        private readonly Color panelColor = Color.FromArgb(27, 35, 51);
        private readonly Color muted = Color.FromArgb(159, 174, 193);
        private readonly Color accent = Color.FromArgb(90, 158, 255);
        private readonly ListView sharedList = new ListView();
        private readonly ListView remoteList = new ListView();
        private readonly ImageList actionImages = new ImageList { ImageSize = new Size(1, 28) };
        private readonly Label status = new Label();
        private readonly Label drop = new Label();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private readonly ToolTip windowToolTip = new ToolTip();
        private Button maximizeButton;
        private bool resizing;
        private int resizeEdges;
        private Point resizeStart;
        private Rectangle resizeBounds;
        private ShareController controller;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public MainForm()
        {
            Text = "WLAN-FT";
            Font = new Font("Segoe UI", 10F);
            BackColor = background;
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(800, 620);
            Size = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, TopPadding, 28, 18),
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
            layout.MouseDown += ResizeMouseDown;
            layout.MouseMove += ResizeMouseMove;
            layout.MouseUp += ResizeMouseUp;
            layout.MouseCaptureChanged += ResizeCaptureChanged;
            MouseDown += ResizeMouseDown;
            MouseMove += ResizeMouseMove;
            MouseUp += ResizeMouseUp;
            MouseCaptureChanged += ResizeCaptureChanged;

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
                Text = "Local network file sharing",
                ForeColor = muted,
                AutoSize = true,
                Location = new Point(2, 45)
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            FlowLayoutPanel windowButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            windowButtons.Controls.Add(MakeWindowButton("\u2013", "Minimize", delegate { WindowState = FormWindowState.Minimized; }));
            maximizeButton = MakeWindowButton("\u25a1", "Maximize", ToggleMaximize);
            windowButtons.Controls.Add(maximizeButton);
            windowButtons.Controls.Add(MakeWindowButton("\u00d7", "Close", delegate { Close(); }));
            header.Controls.Add(windowButtons);
            header.MouseDown += MoveWindow;
            title.MouseDown += MoveWindow;
            subtitle.MouseDown += MoveWindow;
            header.DoubleClick += ToggleMaximize;
            title.DoubleClick += ToggleMaximize;
            subtitle.DoubleClick += ToggleMaximize;
            layout.Controls.Add(header, 0, 0);

            drop.Text = "Drop files to share";
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

            layout.Controls.Add(BuildSection("Your files", sharedList,
                "Add files", AddFiles, "Stop sharing", RemoveFiles), 0, 2);
            layout.Controls.Add(BuildSection("On your network", remoteList,
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
            FormClosed += delegate { actionImages.Dispose(); windowToolTip.Dispose(); };
        }

        private Button MakeWindowButton(string text, string name, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                AccessibleName = name,
                Size = new Size(40, 32),
                Margin = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = name == "Close" ? Color.FromArgb(190, 45, 50) : panelColor;
            button.Click += click;
            windowToolTip.SetToolTip(button, name);
            return button;
        }

        private void ToggleMaximize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Maximized)
                WindowState = FormWindowState.Normal;
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
        }

        private void MoveWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            Region previous = Region;
            if (WindowState == FormWindowState.Maximized)
                Region = null;
            else
            {
                int diameter = Math.Min(24, Math.Min(ClientSize.Width, ClientSize.Height));
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(0, 0, diameter, diameter, 180, 90);
                    path.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 90);
                    path.AddArc(ClientSize.Width - diameter, ClientSize.Height - diameter, diameter, diameter, 0, 90);
                    path.AddArc(0, ClientSize.Height - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure();
                    Region = new Region(path);
                }
            }
            if (previous != null) previous.Dispose();

            if (maximizeButton != null)
            {
                bool maximized = WindowState == FormWindowState.Maximized;
                maximizeButton.Text = maximized ? "\u2750" : "\u25a1";
                maximizeButton.AccessibleName = maximized ? "Restore" : "Maximize";
                windowToolTip.SetToolTip(maximizeButton, maximizeButton.AccessibleName);
            }
        }

        private int GetResizeEdges(Point point)
        {
            if (WindowState != FormWindowState.Normal) return 0;
            const int border = 8;
            int edges = 0;
            if (point.X < border) edges |= 1;
            else if (point.X >= ClientSize.Width - border) edges |= 2;
            if (point.Y < border && edges != 0) edges |= 4;
            else if (point.Y >= ClientSize.Height - border) edges |= 8;
            return edges;
        }

        private bool IsTopDragArea(Point point)
        {
            return point.X >= 0 && point.X < ClientSize.Width && point.Y >= 0 &&
                point.Y < TopPadding && GetResizeEdges(point) == 0;
        }

        private void ResizeMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Control control = (Control)sender;
            Point point = PointToClient(control.PointToScreen(e.Location));
            int edges = GetResizeEdges(point);
            if (edges == 0)
            {
                if (IsTopDragArea(point)) MoveWindow(sender, e);
                return;
            }
            resizing = true;
            resizeEdges = edges;
            resizeStart = Cursor.Position;
            resizeBounds = Bounds;
            control.Capture = true;
        }

        private void ResizeMouseMove(object sender, MouseEventArgs e)
        {
            Control control = (Control)sender;
            if (!resizing)
            {
                int edges = GetResizeEdges(PointToClient(control.PointToScreen(e.Location)));
                if (edges == 5 || edges == 10) control.Cursor = Cursors.SizeNWSE;
                else if (edges == 6 || edges == 9) control.Cursor = Cursors.SizeNESW;
                else if ((edges & 3) != 0) control.Cursor = Cursors.SizeWE;
                else if ((edges & 12) != 0) control.Cursor = Cursors.SizeNS;
                else control.Cursor = Cursors.Default;
                return;
            }

            Point current = Cursor.Position;
            int dx = current.X - resizeStart.X;
            int dy = current.Y - resizeStart.Y;
            int left = resizeBounds.Left;
            int top = resizeBounds.Top;
            int right = resizeBounds.Right;
            int bottom = resizeBounds.Bottom;
            if ((resizeEdges & 1) != 0) left = Math.Min(left + dx, right - MinimumSize.Width);
            if ((resizeEdges & 2) != 0) right = Math.Max(right + dx, left + MinimumSize.Width);
            if ((resizeEdges & 4) != 0) top = Math.Min(top + dy, bottom - MinimumSize.Height);
            if ((resizeEdges & 8) != 0) bottom = Math.Max(bottom + dy, top + MinimumSize.Height);
            Bounds = Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void ResizeMouseUp(object sender, MouseEventArgs e)
        {
            if (!resizing) return;
            resizing = false;
            ((Control)sender).Capture = false;
        }

        private void ResizeCaptureChanged(object sender, EventArgs e)
        {
            if (!((Control)sender).Capture) resizing = false;
        }

        private Control BuildSection(string title, ListView list, string action, EventHandler click, string secondAction, EventHandler secondClick)
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
            list.Columns.Add("File", list == remoteList ? 185 : 240);
            list.Columns.Add("Size", 80);
            list.Columns.Add(list == remoteList ? "Device" : "Location", list == remoteList ? 150 : 200);
            if (list == remoteList) list.Columns.Add("Progress", 115);
            list.Columns.Add("", 110);
            list.SmallImageList = actionImages;
            list.OwnerDraw = true;
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e) { e.DrawDefault = true; };
            list.DrawSubItem += DrawActionSubItem;
            if (list == remoteList)
            {
                list.MouseClick += RemoteFileClicked;
                list.MouseDoubleClick += RemoteFileDoubleClicked;
            }
            else
            {
                list.MouseClick += SharedFileClicked;
            }
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
                controller.DownloadFinished += DownloadFinished;
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

        private void DownloadFinished(RemoteFile remote)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<RemoteFile>(DownloadFinished), remote);
                }
                catch (InvalidOperationException) { }
                return;
            }

            RefreshRemoteFiles();
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
                    item.SubItems.Add("Remove");
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

        private void SharedFileClicked(object sender, MouseEventArgs e)
        {
            if (controller == null || e.Button != MouseButtons.Left) return;
            ListViewHitTestInfo hit = sharedList.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem != hit.Item.SubItems[3]) return;
            if (!GetDownloadButtonBounds(hit.SubItem.Bounds).Contains(e.Location)) return;

            controller.Remove(((FileRecord)hit.Item.Tag).Id);
            sharedList.Items.Remove(hit.Item);
            SetStatus("The file is no longer shared.");
        }

        private void RefreshRemoteFiles()
        {
            if (controller == null) return;

            HashSet<string> selected = new HashSet<string>();
            foreach (ListViewItem item in remoteList.SelectedItems)
            {
                RemoteFile remote = (RemoteFile)item.Tag;
                selected.Add(ShareController.DownloadKey(remote));
            }

            Dictionary<string, DownloadTransfer> active = new Dictionary<string, DownloadTransfer>();
            foreach (DownloadTransfer transfer in controller.GetDownloads())
                active.Add(ShareController.DownloadKey(transfer.Remote), transfer);

            remoteList.BeginUpdate();
            remoteList.Items.Clear();

            HashSet<string> shown = new HashSet<string>();
            foreach (RemoteFile remote in controller.GetRemoteFiles())
            {
                string key = ShareController.DownloadKey(remote);
                shown.Add(key);
                DownloadTransfer transfer;
                active.TryGetValue(key, out transfer);
                AddRemoteFile(remote, selected, transfer);
            }
            foreach (KeyValuePair<string, DownloadTransfer> entry in active)
                if (!shown.Contains(entry.Key)) AddRemoteFile(entry.Value.Remote, selected, entry.Value);

            remoteList.EndUpdate();
        }

        private void AddRemoteFile(RemoteFile remote, HashSet<string> selected, DownloadTransfer transfer)
        {
            ListViewItem item = new ListViewItem(remote.File.Name);
            item.SubItems.Add(FormatSize(remote.File.Size));
            item.SubItems.Add(String.IsNullOrEmpty(remote.Computer) ? remote.Address.ToString() : remote.Computer);
            item.SubItems.Add(transfer == null ? "" : transfer.Progress + "%");
            item.SubItems.Add(transfer == null ? "Download" : "Cancel");
            item.Tag = remote;
            remoteList.Items.Add(item);
            item.Selected = selected.Contains(ShareController.DownloadKey(remote));
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
            if (remoteList.SelectedItems.Count != 1)
            {
                MessageBox.Show(this, "Select a file to download.", Text);
                return;
            }

            DownloadFile((RemoteFile)remoteList.SelectedItems[0].Tag);
        }

        private void DrawActionSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView list = (ListView)sender;
            if (list == remoteList && e.ColumnIndex == 3)
            {
                using (Brush backgroundBrush = new SolidBrush(e.Item.Selected ? SystemColors.Highlight : list.BackColor))
                    e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                int percent;
                if (Int32.TryParse(e.SubItem.Text.TrimEnd('%'), out percent))
                {
                    Rectangle bar = Rectangle.Inflate(e.Bounds, -3, -5);
                    using (Brush track = new SolidBrush(Color.FromArgb(54, 65, 83)))
                        e.Graphics.FillRectangle(track, bar);
                    Rectangle fill = new Rectangle(bar.X, bar.Y, bar.Width * percent / 100, bar.Height);
                    using (Brush fillBrush = new SolidBrush(accent))
                        e.Graphics.FillRectangle(fillBrush, fill);
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, bar, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                return;
            }

            if (e.ColumnIndex != (list == remoteList ? 4 : 3))
            {
                e.DrawDefault = true;
                return;
            }

            using (Brush backgroundBrush = new SolidBrush(e.Item.Selected ? SystemColors.Highlight : list.BackColor))
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            Rectangle button = GetDownloadButtonBounds(e.Bounds);
            using (Brush buttonBrush = new SolidBrush(accent))
                e.Graphics.FillRectangle(buttonBrush, button);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, button, background,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Rectangle GetDownloadButtonBounds(Rectangle cell)
        {
            return Rectangle.Inflate(cell, -3, -2);
        }

        private void RemoteFileClicked(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ListViewHitTestInfo hit = remoteList.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem != hit.Item.SubItems[4]) return;
            if (!GetDownloadButtonBounds(hit.SubItem.Bounds).Contains(e.Location)) return;

            RemoteFile remote = (RemoteFile)hit.Item.Tag;
            if (hit.SubItem.Text == "Cancel")
            {
                if (controller != null && controller.CancelDownload(remote)) SetStatus("Cancelling " + remote.File.Name + "...");
                return;
            }
            DownloadFile(remote);
        }

        private void RemoteFileDoubleClicked(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ListViewHitTestInfo hit = remoteList.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem == hit.Item.SubItems[4]) return;

            DownloadFile((RemoteFile)hit.Item.Tag);
        }

        private void DownloadFile(RemoteFile remote)
        {
            if (controller == null)
            {
                MessageBox.Show(this, "The network service is not available.", Text);
                return;
            }

            foreach (DownloadTransfer transfer in controller.GetDownloads())
            {
                if (ShareController.DownloadKey(transfer.Remote) == ShareController.DownloadKey(remote))
                {
                    MessageBox.Show(this, "This file is already downloading.", Text);
                    return;
                }
            }

            string name = Path.GetFileName(remote.File.Name);
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            if (String.IsNullOrWhiteSpace(name)) name = "file";

            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Choose a download folder", ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string destination = Path.Combine(dialog.SelectedPath, name);
                if (File.Exists(destination) && MessageBox.Show(this,
                    "A file named \"" + name + "\" already exists in this folder. Replace it?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                try
                {
                    controller.Download(remote, destination);
                    RefreshRemoteFiles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not start the download: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
