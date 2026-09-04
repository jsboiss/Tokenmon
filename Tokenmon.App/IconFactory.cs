using System.Drawing;
using System.Runtime.InteropServices;

namespace Tokenmon.App;

public static class IconFactory
{
    public static Icon CreatePokeball()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var outline = new Pen(Color.FromArgb(24, 27, 37), 2.5f);
        using var red = new SolidBrush(Color.FromArgb(239, 76, 76));
        using var white = new SolidBrush(Color.FromArgb(245, 246, 250));
        using var dark = new SolidBrush(Color.FromArgb(24, 27, 37));
        graphics.FillPie(red, 3, 3, 26, 26, 180, 180);
        graphics.FillPie(white, 3, 3, 26, 26, 0, 180);
        graphics.FillRectangle(dark, 3, 14, 26, 4);
        graphics.DrawEllipse(outline, 3, 3, 26, 26);
        graphics.FillEllipse(dark, 11, 11, 10, 10);
        graphics.FillEllipse(white, 14, 14, 4, 4);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
