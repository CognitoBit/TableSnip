using System.Globalization;
using System.Text;
using System.Text.Json;
using TableSnip.Core;
using TableSnip.Interop;
using Tesseract;

namespace TableSnip;

/// <summary>
/// Headless mode, handy for scripting and for testing the table logic:
///   TableSnip --file image.png            prints tab-separated table to stdout
///   TableSnip --file image.png --out t.tsv
///   TableSnip --file image.png --json     dumps raw OCR words with bounding boxes
///   TableSnip --file image.png --copy     puts the table on the clipboard
///   --gap 1.4                             column gap sensitivity (1.0 = default)
///   --engine both|win|tess                engines to use (default: both, voting)
///   --scale 2 --gray --single --psm 6     tuning knobs for single-engine experiments
/// </summary>
internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        NativeMethods.AttachConsole(-1);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        using var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };

        try
        {
            string? file = Arg(args, "--file");
            string? outPath = Arg(args, "--out");
            bool json = args.Contains("--json");
            bool copy = args.Contains("--copy");
            double gap = ParseDouble(Arg(args, "--gap")) ?? 1.0;
            double? scale = ParseDouble(Arg(args, "--scale"));
            bool gray = args.Contains("--gray");
            bool single = args.Contains("--single");
            string engineName = Arg(args, "--engine") ?? "both";
            int psm = int.TryParse(Arg(args, "--psm"), out var p) ? p : 6;
            float minConf = (float)(ParseDouble(Arg(args, "--minconf")) ?? 25);
            string? tessdata = Arg(args, "--tessdata");

            if (file == null || !File.Exists(file))
            {
                stderr.WriteLine("Usage: TableSnip --file <image> [--out <file>] [--gap 1.0] [--json] [--copy] [--engine both|win|tess]");
                return 2;
            }

            using var bmp = ImageUtil.LoadFile(file);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<OcrWord> words;

            switch (engineName)
            {
                case "win":
                {
                    var engine = WindowsOcr.CreateEngine();
                    if (engine == null)
                    {
                        stderr.WriteLine("Windows OCR is not available. Install a language pack with OCR support.");
                        return 3;
                    }
                    words = await WindowsOcr.RecognizeAsync(engine, bmp, scale, gray, multiPass: !single);
                    break;
                }
                case "tess":
                {
                    using var tess = TesseractOcr.TryCreate(tessdata);
                    if (tess == null)
                    {
                        stderr.WriteLine("Tesseract language data not found (tessdata/eng.traineddata).");
                        return 4;
                    }
                    words = tess.Recognize(bmp, scale, grayscale: true, (PageSegMode)psm, minConf);
                    break;
                }
                default:
                {
                    using var recognizer = new Recognizer();
                    if (!recognizer.IsAvailable)
                    {
                        stderr.WriteLine("No OCR engine available.");
                        return 3;
                    }
                    words = await recognizer.RecognizeAsync(bmp);
                    engineName = recognizer.Description;
                    if (!recognizer.HasTesseract) stderr.WriteLine("Tesseract unavailable: " + TesseractOcr.LastError);
                    break;
                }
            }
            stderr.WriteLine($"[{engineName}] {words.Count} words in {sw.ElapsedMilliseconds} ms");

            var rows = TableBuilder.Build(words, gap);

            string output = json
                ? JsonSerializer.Serialize(new { words, rows }, new JsonSerializerOptions { WriteIndented = true })
                : ClipboardTable.ToTsv(rows);

            if (copy) ClipboardTable.Copy(rows);
            if (outPath != null) File.WriteAllText(outPath, output, new UTF8Encoding(false));
            else stdout.Write(output);
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
