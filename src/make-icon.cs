using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class MakeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static void Main()
    {
        const int size = 64;
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var path = RoundRect(new RectangleF(2, 2, size - 4, size - 4), 14f))
                using (var brush = new SolidBrush(Color.FromArgb(0x07, 0xC1, 0x60)))
                {
                    g.FillPath(brush, path);
                }

                using (var bubble = RoundRect(new RectangleF(11, 15, 42, 31), 8f))
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillPath(white, bubble);
                    g.FillPolygon(white, new PointF[] { new PointF(17, 44), new PointF(17, 55), new PointF(29, 45) });
                }

                using (var dot = new SolidBrush(Color.FromArgb(0x07, 0xC1, 0x60)))
                {
                    g.FillEllipse(dot, 20, 28, 6, 6);
                    g.FillEllipse(dot, 29, 28, 6, 6);
                    g.FillEllipse(dot, 38, 28, 6, 6);
                }
            }

            IntPtr h = bmp.GetHicon();
            try
            {
                using (var ico = Icon.FromHandle(h))
                using (var fs = File.Create("app.ico"))
                {
                    ico.Save(fs);
                }
            }
            finally
            {
                DestroyIcon(h);
            }
        }

        Console.WriteLine("icon written: " + Path.GetFullPath("app.ico"));
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
