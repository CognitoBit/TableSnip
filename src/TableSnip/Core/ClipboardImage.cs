using System.Drawing;
using System.Windows;

namespace TableSnip.Core;

/// <summary>Reads an image off the clipboard (or a drag-and-drop payload), coping with the various ways apps supply one.</summary>
public static class ClipboardImage
{
    public static bool HasImage()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            return data != null && HasImage(data);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasImage(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib))
                return true;
            return FirstImageFile(data) != null;
        }
        catch
        {
            return false;
        }
    }

    public static string? FirstImageFile(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            return (data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault(ImageUtil.IsImageFile);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Image currently on the clipboard, flattened onto white, or null.</summary>
    public static Bitmap? Get()
    {
        IDataObject? data;
        try { data = Clipboard.GetDataObject(); }
        catch { return null; }
        return data == null ? null : Get(data, fromClipboard: true);
    }

    public static Bitmap? Get(IDataObject data, bool fromClipboard)
    {
        // PNG is lossless and carries a correct alpha channel (Snipping Tool, browsers, Office).
        if (data.GetDataPresent("PNG"))
        {
            try
            {
                if (data.GetData("PNG") is Stream s)
                {
                    s.Position = 0;
                    using var tmp = new Bitmap(s);
                    return ImageUtil.ToOpaque(tmp);
                }
            }
            catch
            {
                // fall through to the other formats
            }
        }

        if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib))
        {
            if (fromClipboard)
            {
                // WinForms understands CF_DIB / CF_DIBV5 better than WPF does.
                try
                {
                    using var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img is Bitmap b) return ImageUtil.ToOpaque(b);
                }
                catch
                {
                }
            }
            try
            {
                if (data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource src)
                    return FromBitmapSource(src);
            }
            catch
            {
            }
        }

        var file = FirstImageFile(data);
        if (file != null)
        {
            try { return ImageUtil.LoadFile(file); }
            catch { }
        }

        return null;
    }

    public static Bitmap FromBitmapSource(System.Windows.Media.Imaging.BitmapSource src)
    {
        using var ms = new MemoryStream();
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        enc.Save(ms);
        ms.Position = 0;
        using var tmp = new Bitmap(ms);
        return ImageUtil.ToOpaque(tmp);
    }
}
