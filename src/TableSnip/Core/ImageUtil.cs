using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace TableSnip.Core;

public static class ImageUtil
{
    /// <summary>
    /// Flatten onto white, add a margin and upscale small captures. Windows OCR is far more
    /// accurate when glyphs are roughly 25-50 px tall, and screen text is usually 10-16 px.
    /// </summary>
    /// <summary>A bitmap ready for OCR plus the transform that maps its pixels back to the source.</summary>
    public sealed record PreparedImage(Bitmap Bitmap, double Scale, int Pad) : IDisposable
    {
        public OcrWord ToSource(OcrWord w) =>
            new(w.Text, (w.X - Pad) / Scale, (w.Y - Pad) / Scale, w.W / Scale, w.H / Scale, w.Conf);
        public void Dispose() => Bitmap.Dispose();
    }

    /// <summary>Default upscale: aim for ~2200 px on the long side, between 1x and 3x.</summary>
    public static double AutoScale(Bitmap src) =>
        Math.Clamp(2200.0 / Math.Max(src.Width, src.Height), 1.0, 3.0);

    public static PreparedImage PrepareForOcr(Bitmap src, double maxDimension, double? scaleOverride = null, bool grayscale = false)
    {
        const int pad = 24;
        double longest = Math.Max(src.Width, src.Height);
        double scale = scaleOverride ?? AutoScale(src);
        if (longest * scale + 2 * pad > maxDimension)
            scale = (maxDimension - 2 * pad) / longest;

        int w = (int)Math.Round(src.Width * scale);
        int h = (int)Math.Round(src.Height * scale);
        var dst = new Bitmap(w + 2 * pad, h + 2 * pad, PixelFormat.Format32bppPArgb);
        dst.SetResolution(96, 96);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.White);
        g.InterpolationMode = scale > 1.01 ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        if (grayscale)
        {
            // Rec.601 luma; removes ClearType colour fringes that confuse the recogniser.
            attrs.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            }));
        }
        g.DrawImage(src, new Rectangle(pad, pad, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        return new PreparedImage(dst, scale, pad);
    }

    /// <summary>Rotate around the centre onto a white canvas large enough to hold the result.</summary>
    public static Bitmap Rotate(Bitmap src, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        double cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
        int w = (int)Math.Ceiling(src.Width * cos + src.Height * sin);
        int h = (int)Math.Ceiling(src.Width * sin + src.Height * cos);
        var dst = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        dst.SetResolution(96, 96);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform(w / 2f, h / 2f);
        g.RotateTransform((float)degrees);
        g.TranslateTransform(-src.Width / 2f, -src.Height / 2f);
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        return dst;
    }

    /// <summary>
    /// Returns an opaque 32bpp copy. Some clipboard sources (notably the Snipping Tool via
    /// CF_DIB) hand out 32bpp images whose alpha channel is all zero; those are treated as opaque
    /// instead of vanishing into the white background.
    /// </summary>
    public static Bitmap ToOpaque(Bitmap src)
    {
        Bitmap? fixedAlpha = null;
        try
        {
            if (Image.IsAlphaPixelFormat(src.PixelFormat) && AllAlphaZero(src))
                fixedAlpha = DropAlpha(src);

            var source = fixedAlpha ?? src;
            var dst = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            dst.SetResolution(96, 96);
            using var g = Graphics.FromImage(dst);
            g.Clear(Color.White);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return dst;
        }
        finally
        {
            fixedAlpha?.Dispose();
        }
    }

    private static bool AllAlphaZero(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var row = new byte[stride];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                for (int x = 3; x < bmp.Width * 4; x += 4)
                    if (row[x] != 0) return false;
            }
            return true;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static Bitmap DropAlpha(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var dst = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppRgb);
        var s = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var d = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int bytes = Math.Min(s.Stride, d.Stride);
            var row = new byte[bytes];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(s.Scan0 + y * s.Stride, row, 0, bytes);
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, bytes);
            }
        }
        finally
        {
            bmp.UnlockBits(s);
            dst.UnlockBits(d);
        }
        return dst;
    }

    /// <summary>Copy raw BGRA pixels out of a bitmap (tightly packed, stride == width * 4).</summary>
    public static byte[] GetBgraBytes(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var clone = bmp.Clone(rect, PixelFormat.Format32bppPArgb);
        var data = clone.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[bmp.Width * 4 * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bmp.Width * 4, bmp.Width * 4);
            return bytes;
        }
        finally { clone.UnlockBits(data); }
    }

    /// <summary>Load an image file without keeping the file locked.</summary>
    public static Bitmap LoadFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var tmp = new Bitmap(fs);
        return ToOpaque(tmp);
    }

    /// <summary>Convert to a frozen WPF bitmap for display.</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    public static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
