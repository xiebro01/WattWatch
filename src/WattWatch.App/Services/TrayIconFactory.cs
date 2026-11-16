using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WattWatch.App.Services;

/// <summary>Draws the tray glyph: a battery filled to the lowest device level.</summary>
internal static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(int? percent)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            Color accent = percent switch
            {
                null => Color.FromArgb(140, 150, 166),
                >= 50 => Color.FromArgb(61, 220, 151),
                >= 20 => Color.FromArgb(245, 185, 66),
                _ => Color.FromArgb(255, 98, 89),
            };
            Color outline = Color.FromArgb(228, 234, 243);

            using (GraphicsPath body = RoundedRectangle(new RectangleF(3f, 9f, 21f, 14f), 3.5f))
            using (var pen = new Pen(outline, 2f))
            {
                graphics.DrawPath(pen, body);
            }

            using (var nub = new SolidBrush(outline))
            {
                graphics.FillRectangle(nub, 25.5f, 13f, 3f, 6f);
            }

            if (percent is > 0)
            {
                float width = Math.Max(2.5f, 16f * (percent.Value / 100f));
                using GraphicsPath fill = RoundedRectangle(new RectangleF(6.2f, 12.2f, width, 7.6f), 1.8f);
                using var brush = new SolidBrush(accent);
                graphics.FillPath(brush, fill);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
