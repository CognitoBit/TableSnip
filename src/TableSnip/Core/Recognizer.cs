using System.Drawing;
using Tesseract;
using Windows.Media.Ocr;

namespace TableSnip.Core;

/// <summary>
/// Runs every OCR engine we have and lets them vote.
///
/// Windows OCR is fast and good at short tokens but unreliable on numbers with thousands
/// separators; Tesseract is the reverse. Each engine also misses different words depending on
/// glyph size, so both are run at two scales. The readings are clustered by position and, per
/// cluster, the text with the strongest agreement wins. Agreement between different engines
/// counts for much more than agreement between two passes of the same engine, whose mistakes
/// are correlated.
/// </summary>
public sealed class Recognizer : IDisposable
{
    private const int EngineWindows = 0;
    private const int EngineTesseract = 1;

    private readonly Task _init;
    private OcrEngine? _win;
    private TesseractOcr? _tess1, _tess2;

    public Recognizer()
    {
        _init = Task.Run(() =>
        {
            _win = WindowsOcr.CreateEngine();
            _tess1 = TesseractOcr.TryCreate();
            _tess2 = _tess1 == null ? null : TesseractOcr.TryCreate();
        });
    }

    /// <summary>Completes once the engines have been created (a few hundred ms after start-up).</summary>
    public Task Ready => _init;

    private void EnsureReady() => _init.GetAwaiter().GetResult();

    public bool HasWindowsOcr { get { EnsureReady(); return _win != null; } }
    public bool HasTesseract { get { EnsureReady(); return _tess1 != null; } }
    public bool IsAvailable => HasWindowsOcr || HasTesseract;

    public string Description
    {
        get
        {
            EnsureReady();
            var parts = new List<string>();
            if (_win != null) parts.Add($"Windows OCR ({WindowsOcr.LanguageName(_win)})");
            if (_tess1 != null) parts.Add("Tesseract");
            return parts.Count == 0 ? "no OCR engine" : string.Join(" + ", parts);
        }
    }

    public sealed record Source(List<OcrWord> Words, int Engine);

    public async Task<List<OcrWord>> RecognizeAsync(Bitmap source)
    {
        await _init;
        if (_win == null && _tess1 == null) return new List<OcrWord>();

        var sources = new List<Source>();
        Bitmap? straightened = null;
        var copies = new List<Bitmap>();
        try
        {
            var work = source;
            double s1 = ImageUtil.AutoScale(source);
            double s2 = WindowsOcr.SecondScale(s1);
            Task<WindowsOcr.Pass>? winSecond = null;

            if (_win != null)
            {
                var (str, first) = await WindowsOcr.FirstPassAsync(_win, source, s1, grayscale: false);
                straightened = str;
                work = str ?? source;
                sources.Add(new Source(first.Words, EngineWindows));
                winSecond = WindowsOcr.PassAsync(_win, work, s2, grayscale: false);
            }

            // GDI+ bitmaps must not be read from two threads at once; each Tesseract pass gets a copy.
            var tessTasks = new List<Task<List<OcrWord>>>();
            foreach (var (engine, scale) in new[] { (_tess1, s1), (_tess2, s2) })
            {
                if (engine == null) continue;
                var copy = (Bitmap)work.Clone();
                copies.Add(copy);
                var e = engine;
                tessTasks.Add(Task.Run(() => e.Recognize(copy, scale, grayscale: true, PageSegMode.SingleBlock, minConfidence: 25)));
            }

            if (winSecond != null) sources.Add(new Source((await winSecond).Words, EngineWindows));
            foreach (var t in tessTasks) sources.Insert(0, new Source(await t, EngineTesseract));

            return VoteMerge(sources);
        }
        finally
        {
            straightened?.Dispose();
            foreach (var c in copies) c.Dispose();
        }
    }

    // ------------------------------------------------------------------ voting

    /// <summary>
    /// Merge readings from several sources (ordered by priority). Words are clustered by
    /// overlap; inside a cluster each source contributes one candidate (its words in that
    /// spot, left to right) and the best-supported candidate wins.
    /// </summary>
    public static List<OcrWord> VoteMerge(IReadOnlyList<Source> sources)
    {
        var all = new List<(OcrWord Word, int Src)>();
        for (int s = 0; s < sources.Count; s++)
            foreach (var w in sources[s].Words)
                if (IsUseful(w.Text)) all.Add((w, s));

        int n = all.Count;
        if (n == 0) return new List<OcrWord>();

        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }

        var order = Enumerable.Range(0, n).OrderBy(i => all[i].Word.Y).ToArray();
        for (int a = 0; a < n; a++)
        {
            var wa = all[order[a]].Word;
            for (int b = a + 1; b < n; b++)
            {
                var wb = all[order[b]].Word;
                if (wb.Y > wa.Bottom) break;
                if (all[order[a]].Src == all[order[b]].Src) continue;
                if (OverlapFraction(wa, wb) > 0.4) Union(order[a], order[b]);
            }
        }

        var result = new List<OcrWord>();
        foreach (var cluster in Enumerable.Range(0, n).GroupBy(Find))
        {
            var candidates = cluster
                .GroupBy(i => all[i].Src)
                .Select(g => new Candidate(g.Key, sources[g.Key].Engine, g.Select(i => all[i].Word).OrderBy(w => w.X).ToList()))
                .ToList();

            foreach (var c in candidates)
            {
                // Weighted agreement: first source per engine counts 1, further passes of the
                // same engine only 0.35 (their errors are correlated).
                double votes = 0;
                foreach (var grp in candidates.Where(o => o.Key == c.Key).GroupBy(o => o.Engine))
                    votes += 1 + 0.35 * (grp.Count() - 1);
                c.Score = votes + c.Plausibility / 20.0 + c.Confidence / 1000.0;
            }

            var best = candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Key.Length)
                .ThenBy(c => c.Words.Count)   // among equals prefer the un-fragmented reading
                .ThenBy(c => c.Src)
                .First();

            result.AddRange(best.Words);
        }
        return result;
    }

    private sealed class Candidate
    {
        public readonly int Src, Engine;
        public readonly List<OcrWord> Words;
        public readonly string Key;          // whitespace-insensitive text used to detect agreement
        public readonly double Plausibility;
        public readonly double Confidence;
        public double Score;

        public Candidate(int src, int engine, List<OcrWord> words)
        {
            Src = src;
            Engine = engine;
            Words = words;
            Key = string.Concat(words.Select(w => w.Text)).Replace(" ", string.Empty);
            Confidence = words.Average(w => w.Conf);
            Plausibility = Recognizer.Plausibility(string.Join(" ", words.Select(w => w.Text)));
        }
    }

    /// <summary>Penalties (in points, 0 = fine) for readings that look like classic OCR mistakes.</summary>
    internal static double Plausibility(string text)
    {
        double score = 0;
        int digits = text.Count(char.IsDigit);
        if (digits >= 2 && text.Any(ch => ch is 'o' or 'O' or 'l' or 'I' or '|')) score -= 15; // "1,ooo", "1O0"
        if (text.Contains(" ,") || text.Contains(" .")) score -= 10;                            // "1 ,200"
        if (digits >= 4 && text.Trim().All(ch => char.IsDigit(ch) || ch is ' ' or ',' or '.') && text.Trim().Contains(' '))
            score -= 8;                                                                          // "445 200": a comma read as a space
        if (ThousandsPattern.IsMatch(text.Trim())) score += 6;                                  // "445,200": well-formed separators
        if (text.Length == 2 && char.IsUpper(text[0]) && text[1] is 'I' or 'l') score -= 8;    // "QI" for "Q1"
        if (text.EndsWith('.') || text.EndsWith(',')) score -= 5;                               // "34."
        if (text.Length == 1 && !char.IsLetterOrDigit(text[0])) score -= 20;                   // stray "|"
        if (text.Any(ch => ch is '•' or '¢' or '©' or '®' or '°' or '§' or '¤')) score -= 10;
        return score;
    }

    private static readonly System.Text.RegularExpressions.Regex ThousandsPattern =
        new(@"^[-(]?[$€£¥]?\d{1,3}(,\d{3})+(\.\d+)?%?\)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsUseful(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Any(char.IsLetterOrDigit)) return true;
        return text is "-" or "–" or "—";   // placeholders for empty cells
    }

    public static double OverlapFraction(OcrWord p, OcrWord q)
    {
        double ix = Math.Min(p.Right, q.Right) - Math.Max(p.X, q.X);
        double iy = Math.Min(p.Bottom, q.Bottom) - Math.Max(p.Y, q.Y);
        if (ix <= 0 || iy <= 0) return 0;
        double minArea = Math.Min(p.Area, q.Area);
        return minArea <= 0 ? 0 : ix * iy / minArea;
    }

    public void Dispose()
    {
        try { _init.Wait(); } catch { }
        _tess1?.Dispose();
        _tess2?.Dispose();
    }
}
