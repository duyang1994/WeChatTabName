using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 生成真正的 32bpp（带 alpha）多尺寸 ICO。
// 不能用 Bitmap.GetHicon() + Icon.Save()：那条路会把图压成 16 色，绿色会变成调色板的青绿。
internal static class MakeIcon
{
    private static void Main()
    {
        int[] sizes = new int[] { 16, 32, 48 };
        var images = new byte[sizes.Length][];
        var masks = new byte[sizes.Length][];

        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bmp = Render(sizes[i]))
            {
                images[i] = ToBgraBottomUp(bmp);
                masks[i] = MakeMask(bmp);
            }
        }

        using (var fs = File.Create("app.ico"))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int side = sizes[i];
                bw.Write((byte)side);
                bw.Write((byte)side);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                int len = 40 + images[i].Length + masks[i].Length;
                bw.Write(len);
                bw.Write(offset);
                offset += len;
            }

            for (int i = 0; i < sizes.Length; i++)
            {
                int side = sizes[i];
                bw.Write(40);
                bw.Write(side);
                bw.Write(side * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write(0);
                bw.Write(images[i].Length + masks[i].Length);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(images[i]);
                bw.Write(masks[i]);
            }
        }

        Console.WriteLine("icon written: " + Path.GetFullPath("app.ico"));
    }

    private static Bitmap Render(int side)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float pad = side * 0.03f;
            using (var path = RoundRect(new RectangleF(pad, pad, side - 2 * pad, side - 2 * pad), side * 0.22f))
            using (var brush = new SolidBrush(Color.FromArgb(0x07, 0xC1, 0x60)))
                g.FillPath(brush, path);

            float bx = side * 0.17f;
            float by = side * 0.25f;
            float bw = side * 0.66f;
            float bh = side * 0.46f;
            using (var bubble = RoundRect(new RectangleF(bx, by, bw, bh), side * 0.13f))
            using (var white = new SolidBrush(Color.White))
            {
                g.FillPath(white, bubble);
                g.FillPolygon(white, new PointF[]
                {
                    new PointF(side * 0.26f, side * 0.67f),
                    new PointF(side * 0.26f, side * 0.86f),
                    new PointF(side * 0.45f, side * 0.68f)
                });
            }

            float dot = Math.Max(1.5f, side * 0.10f);
            float dy = side * 0.42f;
            using (var d = new SolidBrush(Color.FromArgb(0x07, 0xC1, 0x60)))
            {
                g.FillEllipse(d, side * 0.29f, dy, dot, dot);
                g.FillEllipse(d, side * 0.45f, dy, dot, dot);
                g.FillEllipse(d, side * 0.61f, dy, dot, dot);
            }
        }
        return bmp;
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

    private static byte[] ToBgraBottomUp(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        var data = new byte[w * h * 4];
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                IntPtr src = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                System.Runtime.InteropServices.Marshal.Copy(src, row, 0, row.Length);
                Buffer.BlockCopy(row, 0, data, (h - 1 - y) * row.Length, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return data;
    }

    private static byte[] MakeMask(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        int stride = ((w + 31) / 32) * 4;
        var mask = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bmp.GetPixel(x, y).A == 0)
                    mask[(h - 1 - y) * stride + (x / 8)] |= (byte)(0x80 >> (x % 8));
            }
        }
        return mask;
    }
}
