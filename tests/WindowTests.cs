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
            }
        }
    }
}
