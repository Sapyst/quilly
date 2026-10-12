#nullable enable
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Artry;

internal static class IconArt
{
    private static Bitmap? _cachedSource;

    public static Bitmap GetSourceBitmap()
    {
        if (_cachedSource != null) return _cachedSource;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var pngName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase));
            if (pngName != null)
            {
                using var stream = assembly.GetManifestResourceStream(pngName);
                if (stream != null)
                {
                    _cachedSource = new Bitmap(stream);
                    return _cachedSource;
                }
            }
        }
        catch { }

        var localPath = Path.Combine(AppContext.BaseDirectory, "src", "icon.png");
        if (File.Exists(localPath))
        {
            _cachedSource = new Bitmap(localPath);
            return _cachedSource;
        }

        var localRoot = Path.Combine(AppContext.BaseDirectory, "icon-preview.png");
        if (File.Exists(localRoot))
        {
            _cachedSource = new Bitmap(localRoot);
            return _cachedSource;
        }

        return Fallback(256);
    }

    public static Bitmap Draw(int size)
    {
        var source = GetSourceBitmap();
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(0, 0, 0, 0));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return bitmap;
    }

    public static Icon GetAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var icoName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));
            if (icoName != null)
            {
                using var stream = assembly.GetManifestResourceStream(icoName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
        }
        catch { }

        try
        {
            using var bmp = Draw(128);
            var hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        catch { }

        return SystemIcons.Application;
    }

    private static Bitmap Fallback(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(83, 117, 102));
        return bmp;
    }
}
