using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerTray
{
    // Draws the tray icon: a bolt in the active plan's colour on a dark tile, with a
    // hairline that flips with the taskbar so the tile reads as a deliberate chip
    // rather than a faintly mismatched square against a dark taskbar.
    static class TrayIcons
    {
        static readonly Color TileColor = Color.FromArgb(31, 42, 55);
        static readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>();

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        // Windows requests tray icons at the small-icon metric, which grows with display
        // scaling. Drawing a fixed 16x16 and letting the shell stretch it is exactly what
        // makes tray icons look soft on a 150% laptop.
        static int PreferredSize()
        {
            try
            {
                int size = SystemInformation.SmallIconSize.Width;
                if (size < 16) return 16;
                if (size > 64) return 64;
                return size;
            }
            catch { return 16; }
        }

        public static Icon For(Color accent)
        {
            int size = PreferredSize();
            Color edge = Theme.TrayContrast;
            string key = accent.ToArgb() + "|" + size + "|" + edge.ToArgb();

            Icon hit;
            if (cache.TryGetValue(key, out hit)) return hit;

            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    // Without this the circle in v2.0 had visible stair-step edges.
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    float k = size / 64f;
                    float inset = 1.0f * k;
                    float span = (64f * k) - (inset * 2f);

                    using (GraphicsPath tile = Rounded(inset, inset, span, span, 10f * k))
                    {
                        using (var fill = new SolidBrush(TileColor))
                            g.FillPath(fill, tile);

                        // Semi-transparent: at full strength the hairline competes with
                        // the bolt for attention in a 16px square, which reads as noise.
                        using (var pen = new Pen(Color.FromArgb(120, edge), Math.Max(1f, 1.2f * k)))
                            g.DrawPath(pen, tile);
                    }

                    using (var brush = new SolidBrush(accent))
                        g.FillPolygon(brush, Bolt(k));
                }

                // Icon.FromHandle does not take ownership, so clone into a managed copy
                // and destroy the original rather than leaking a handle per variant.
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(handle))
                    {
                        var made = (Icon)temp.Clone();
                        cache[key] = made;
                        return made;
                    }
                }
                finally { DestroyIcon(handle); }
            }
        }

        // Called when the system theme flips: the hairline colour is baked into each
        // cached bitmap, so they all have to go.
        public static void Invalidate()
        {
            foreach (Icon icon in cache.Values)
            {
                try { icon.Dispose(); }
                catch { }
            }
            cache.Clear();
        }

        // Slightly larger than the app-icon bolt: the tray version has 16 pixels to
        // work with and needs every one of them to stay legible.
        static PointF[] Bolt(float k)
        {
            return new PointF[]
            {
                new PointF(37.6f * k, 8.5f * k),
                new PointF(14.1f * k, 34.2f * k),
                new PointF(28.6f * k, 34.2f * k),
                new PointF(24.2f * k, 55.5f * k),
                new PointF(49.9f * k, 28.6f * k),
                new PointF(34.2f * k, 28.6f * k)
            };
        }

        static GraphicsPath Rounded(float x, float y, float w, float h, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
