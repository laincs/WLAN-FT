using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WlanFt
{
    public static class WindowTests
    {
        public static void CheckWindow()
        {
            using (MainForm form = new MainForm())
            {
                if (form.FormBorderStyle != FormBorderStyle.None)
                    throw new Exception("The default window frame is still present.");

                form.Size = new Size(900, 650);
                if (form.Region == null || form.Region.IsVisible(0, 0) || !form.Region.IsVisible(450, 325))
                    throw new Exception("The window region is not rounded.");

                MethodInfo getEdges = typeof(MainForm).GetMethod("GetResizeEdges", BindingFlags.Instance | BindingFlags.NonPublic);
                Point[] points = { new Point(1, 200), new Point(899, 200), new Point(1, 1), new Point(200, 649) };
                int[] expected = { 1, 2, 5, 8 };
                for (int i = 0; i < points.Length; i++)
                {
                    if ((int)getEdges.Invoke(form, new object[] { points[i] }) != expected[i])
                        throw new Exception("A window resize edge is missing.");
                }

                MethodInfo dragArea = typeof(MainForm).GetMethod("IsTopDragArea", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!(bool)dragArea.Invoke(form, new object[] { new Point(200, 1) }) ||
                    !(bool)dragArea.Invoke(form, new object[] { new Point(200, 15) }) ||
                    (bool)dragArea.Invoke(form, new object[] { new Point(1, 1) }) ||
                    (bool)dragArea.Invoke(form, new object[] { new Point(200, 21) }))
                    throw new Exception("The top drag area is incorrect.");

                MethodInfo drawHeader = typeof(MainForm).GetMethod("DrawColumnHeader", BindingFlags.Instance | BindingFlags.NonPublic);
                using (Bitmap image = new Bitmap(120, 26))
                using (Graphics graphics = Graphics.FromImage(image))
                using (ListView list = new ListView())
                {
                    ColumnHeader header = new ColumnHeader { Text = "Progress" };
                    DrawListViewColumnHeaderEventArgs args = new DrawListViewColumnHeaderEventArgs(
                        graphics, new Rectangle(0, 0, 120, 26), 0, header, ListViewItemStates.Default,
                        Color.White, Color.Black, list.Font);
                    drawHeader.Invoke(form, new object[] { list, args });
                    if (args.DrawDefault || image.GetPixel(2, 2).GetBrightness() > 0.3)
                        throw new Exception("The column header does not use the dark theme.");

                    args = new DrawListViewColumnHeaderEventArgs(graphics, new Rectangle(0, 0, 6, 26), 0,
                        header, ListViewItemStates.Default, Color.White, Color.Black, list.Font);
                    drawHeader.Invoke(form, new object[] { list, args });
                }

                MethodInfo resizeColumn = typeof(MainForm).GetMethod("ResizeActionColumn", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo buttonBounds = typeof(MainForm).GetMethod("GetDownloadButtonBounds", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (string fieldName in new string[] { "sharedList", "remoteList" })
                {
                    ListView list = (ListView)typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    list.Size = new Size(1000, 100);
                    resizeColumn.Invoke(form, new object[] { list, EventArgs.Empty });
                    int width = 0;
                    foreach (ColumnHeader column in list.Columns) width += column.Width;
                    if (width != list.ClientSize.Width) throw new Exception("The column headers do not fill the list.");

                    Rectangle cell = new Rectangle(500, 0, list.Columns[list.Columns.Count - 1].Width, 28);
                    Rectangle button = (Rectangle)buttonBounds.Invoke(null, new object[] { cell });
                    if (button.Width > 104 || button.Right != cell.Right - 3)
                        throw new Exception("The action button does not fit at the right of its cell.");
                }
            }
        }
    }
}
