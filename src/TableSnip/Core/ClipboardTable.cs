using System.Text;
using System.Windows;

namespace TableSnip.Core;

/// <summary>
/// Puts a table on the clipboard in the formats spreadsheet and document apps expect:
/// tab-separated text (Excel, Google Sheets, Numbers, any text editor) and an HTML table
/// (Word, Outlook, Gmail, Notion...).
/// </summary>
public static class ClipboardTable
{
    public static void Copy(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var data = new DataObject();
        data.SetText(ToTsv(rows), TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Html, ToCfHtml(rows));
        Clipboard.SetDataObject(data, copy: true);
    }

    public static string ToTsv(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(string.Join('\t', row.Select(CleanCell)));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static string ToCsv(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(c =>
            {
                var s = c ?? string.Empty;
                bool quote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || s.StartsWith(' ') || s.EndsWith(' ');
                return quote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            })));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CleanCell(string? cell) =>
        (cell ?? string.Empty).Replace('\t', ' ').Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Build a CF_HTML payload (the header offsets are UTF-8 byte offsets).</summary>
    public static string ToCfHtml(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var body = new StringBuilder();
        body.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"3\" style=\"border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:10pt\">\r\n");
        foreach (var row in rows)
        {
            body.Append("<tr>");
            foreach (var cell in row)
                body.Append("<td>").Append(Escape(cell)).Append("</td>");
            body.Append("</tr>\r\n");
        }
        body.Append("</table>");

        const string header =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";
        const string pre = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";

        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int preLen = Encoding.UTF8.GetByteCount(pre);
        int bodyLen = Encoding.UTF8.GetByteCount(body.ToString());
        int postLen = Encoding.UTF8.GetByteCount(post);

        int startHtml = headerLen;
        int startFragment = startHtml + preLen;
        int endFragment = startFragment + bodyLen;
        int endHtml = endFragment + postLen;

        return string.Format(header, startHtml, endHtml, startFragment, endFragment) + pre + body + post;
    }

    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
