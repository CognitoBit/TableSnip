using System.Drawing;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace TableSnip.Core;

/// <summary>Thin wrapper over the OCR engine that ships with Windows 10/11.</summary>
public static class WindowsOcr
{
    /// <summary>Windows OCR reports no per-word confidence; this neutral value is used for voting.</summary>
    public const double DefaultConfidence = 75;

    public sealed record Pass(List<OcrWord> Words, double? Angle);

    public static OcrEngine? CreateEngine()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine != null) return engine;
            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return engine;
            }
        }
        catch
        {
            // WinRT not available (very old Windows) - caller shows a friendly message.
        }
        return null;
    }

    public static string LanguageName(OcrEngine engine)
    {
        try { return engine.RecognizerLanguage.DisplayName; }
        catch { return "unknown"; }
    }

    /// <summary>Second-pass scale: far enough from the first that different words get caught.</summary>
    public static double SecondScale(double s1) => s1 >= 2 ? s1 * 0.6 : Math.Min(s1 * 1.6, 3);

    /// <summary>
    /// Convenience for single-engine use: two passes at different scales, merged by voting.
    /// </summary>
    public static async Task<List<OcrWord>> RecognizeAsync(OcrEngine engine, Bitmap source,
        double? scale = null, bool grayscale = false, bool multiPass = true)
    {
        double s1 = scale ?? ImageUtil.AutoScale(source);
        var (straightened, pass1) = await FirstPassAsync(engine, source, s1, grayscale);
        try
        {
            if (!multiPass) return pass1.Words;
            var pass2 = await PassAsync(engine, straightened ?? source, SecondScale(s1), grayscale);
            return Recognizer.VoteMerge(new[] { new Recognizer.Source(pass1.Words, 0), new Recognizer.Source(pass2.Words, 0) });
        }
        finally
        {
            straightened?.Dispose();
        }
    }

    /// <summary>
    /// First pass, with de-skew: scanned pages are often slightly rotated. If the engine reports
    /// an angle the image is straightened and read again; the straightened bitmap (owned by the
    /// caller) is returned so later passes can use it too.
    /// </summary>
    public static async Task<(Bitmap? Straightened, Pass Pass)> FirstPassAsync(OcrEngine engine, Bitmap source, double scale, bool grayscale)
    {
        var pass = await PassAsync(engine, source, scale, grayscale);
        if (pass.Angle is not double angle || Math.Abs(angle) <= 0.5 || Math.Abs(angle) >= 45)
            return (null, pass);

        Bitmap? best = null;
        double bestAbs = Math.Abs(angle);
        foreach (var sign in new[] { -1, 1 })
        {
            var rotated = ImageUtil.Rotate(source, sign * angle);
            var attempt = await PassAsync(engine, rotated, scale, grayscale);
            double abs = Math.Abs(attempt.Angle ?? 0);
            if (abs < bestAbs && attempt.Words.Count > 0)
            {
                best?.Dispose();
                best = rotated;
                pass = attempt;
                bestAbs = abs;
                if (abs < 0.3) break;
            }
            else
            {
                rotated.Dispose();
            }
        }
        return (best, pass);
    }

    public static async Task<Pass> PassAsync(OcrEngine engine, Bitmap source, double scale, bool grayscale)
    {
        using var prepared = ImageUtil.PrepareForOcr(source, OcrEngine.MaxImageDimension, scale, grayscale);
        using var sb = ToSoftwareBitmap(prepared.Bitmap);
        var result = await engine.RecognizeAsync(sb);
        var words = result.Lines
            .SelectMany(l => l.Words)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => prepared.ToSource(new OcrWord(w.Text, w.BoundingRect.X, w.BoundingRect.Y,
                w.BoundingRect.Width, w.BoundingRect.Height, DefaultConfidence)))
            .ToList();
        return new Pass(words, result.TextAngle);
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var bytes = ImageUtil.GetBgraBytes(bmp);
        var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
    }
}
