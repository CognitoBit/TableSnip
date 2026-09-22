using System.Drawing;
using Tesseract;

namespace TableSnip.Core;

/// <summary>Tesseract LSTM engine (bundled language data), used alongside Windows OCR.</summary>
public sealed class TesseractOcr : IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly object _gate = new();

    public string DataPath { get; }

    private TesseractOcr(string dataPath, string language)
    {
        DataPath = dataPath;
        // In a single-file publish Assembly.Location is empty, so the native loader cannot work out
        // where x64\tesseract50.dll lives on its own. Point it at the app folder explicitly.
        TesseractEnviornment.CustomSearchPath ??= AppContext.BaseDirectory.TrimEnd('\\', '/');
        _engine = new TesseractEngine(dataPath, language, EngineMode.LstmOnly);
        _engine.SetVariable("user_defined_dpi", "300");
        _engine.SetVariable("preserve_interword_spaces", "1");
    }

    /// <summary>Why the last <see cref="TryCreate"/> returned null (diagnostics only).</summary>
    public static string? LastError { get; private set; }

    /// <summary>Creates the engine if bundled language data can be found; otherwise null.</summary>
    public static TesseractOcr? TryCreate(string? dataPath = null, string language = "eng")
    {
        try
        {
            dataPath ??= FindDataPath(language);
            if (dataPath == null)
            {
                LastError = $"{language}.traineddata not found next to the app (looked under {AppContext.BaseDirectory})";
                return null;
            }
            return new TesseractOcr(dataPath, language);
        }
        catch (Exception ex)
        {
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            LastError = inner.GetType().Name + ": " + inner.Message;
            return null;
        }
    }

    public static string? FindDataPath(string language = "eng")
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty, "tessdata"),
        };
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, language + ".traineddata")));
    }

    public List<OcrWord> Recognize(Bitmap source, double? scale = null, bool grayscale = true,
        PageSegMode mode = PageSegMode.SparseText, float minConfidence = 30)
    {
        using var prepared = ImageUtil.PrepareForOcr(source, double.MaxValue, scale ?? ImageUtil.AutoScale(source), grayscale);
        using var ms = new MemoryStream();
        prepared.Bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

        var words = new List<OcrWord>();
        lock (_gate)
        {
            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var page = _engine.Process(pix, mode);
            using var it = page.GetIterator();
            it.Begin();
            do
            {
                if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;
                var text = it.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                float conf = it.GetConfidence(PageIteratorLevel.Word);
                if (conf < minConfidence) continue;
                words.Add(prepared.ToSource(new OcrWord(text, r.X1, r.Y1, r.Width, r.Height, conf)));
            } while (it.Next(PageIteratorLevel.Word));
        }
        return words;
    }

    public void Dispose() => _engine.Dispose();
}
