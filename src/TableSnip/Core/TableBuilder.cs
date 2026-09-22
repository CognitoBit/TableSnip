namespace TableSnip.Core;

/// <summary>
/// Turns a bag of OCR words (with bounding boxes) into a rectangular grid of cells.
///
/// Pipeline:
///   1. Cluster words into rows by vertical centre.
///   2. Inside each row, merge neighbouring words into "chunks" unless the horizontal gap
///      is wide enough to look like a column boundary.
///   3. Assign chunks to columns by horizontal overlap. Rows whose chunk count matches the
///      most common count are used first so they define the column skeleton; other rows are
///      then fitted onto it. Chunks that straddle several columns are split at the gutters
///      when they contain a word gap there; otherwise they anchor to the left-most column.
///   4. Adjacent columns that never share a row are merged (typically a short header
///      sitting above wide right-aligned numbers).
/// </summary>
public static class TableBuilder
{
    /// <param name="gapFactor">1.0 = default. Larger merges more (fewer columns), smaller splits more.</param>
    public static string[][] Build(IReadOnlyList<OcrWord> words, double gapFactor = 1.0)
    {
        if (words.Count == 0) return Array.Empty<string[]>();

        double h = Median(words.Select(w => w.H));
        if (h <= 0) h = 1;

        var rows = ClusterRows(words, h);

        var chunks = new List<Chunk>();
        for (int r = 0; r < rows.Count; r++)
            chunks.AddRange(SplitRow(rows[r], r, h, gapFactor));

        var (columns, assigned) = AssignColumns(chunks, h);
        MergeLonelyColumns(columns, assigned);

        columns.Sort((a, b) => (a.L + a.R).CompareTo(b.L + b.R));
        var colIndex = new Dictionary<Column, int>();
        for (int i = 0; i < columns.Count; i++) colIndex[columns[i]] = i;

        var grid = new string[rows.Count][];
        for (int r = 0; r < rows.Count; r++)
        {
            grid[r] = new string[columns.Count];
            Array.Fill(grid[r], string.Empty);
        }

        foreach (var c in assigned.OrderBy(c => c.Row).ThenBy(c => c.L))
        {
            int ci = colIndex[c.Col!];
            var cell = grid[c.Row][ci];
            grid[c.Row][ci] = cell.Length == 0 ? c.Text : cell + " " + c.Text;
        }

        return grid;
    }

    // ---------------------------------------------------------------- rows

    private sealed class Row
    {
        public readonly List<OcrWord> Words = new();
        private double _sumCy, _sumH;
        public double CY => _sumCy / Words.Count;
        public double H => _sumH / Words.Count;
        public void Add(OcrWord w) { Words.Add(w); _sumCy += w.CY; _sumH += w.H; }
    }

    private static List<Row> ClusterRows(IReadOnlyList<OcrWord> words, double h)
    {
        var rows = new List<Row>();
        foreach (var w in words.OrderBy(w => w.CY))
        {
            Row? target = null;
            for (int i = rows.Count - 1; i >= Math.Max(0, rows.Count - 3); i--)
            {
                var r = rows[i];
                double tol = 0.6 * Math.Max(Math.Max(r.H, w.H), 0.5 * h);
                if (Math.Abs(w.CY - r.CY) <= tol) { target = r; break; }
            }
            if (target == null) { target = new Row(); rows.Add(target); }
            target.Add(w);
        }
        foreach (var r in rows) r.Words.Sort((a, b) => a.X.CompareTo(b.X));
        rows.Sort((a, b) => a.CY.CompareTo(b.CY));
        return rows;
    }

    // -------------------------------------------------------------- chunks

    private sealed class Chunk
    {
        public readonly int Row;
        public readonly List<OcrWord> Words = new();
        public double L = double.PositiveInfinity, R = double.NegativeInfinity;
        public Column? Col;
        public double W => R - L;
        public string Text => string.Join(" ", Words.Select(w => w.Text.Trim())).Trim();
        public Chunk(int row) { Row = row; }
        public void Add(OcrWord w) { Words.Add(w); L = Math.Min(L, w.X); R = Math.Max(R, w.Right); }
    }

    private static IEnumerable<Chunk> SplitRow(Row row, int rowIndex, double h, double gapFactor)
    {
        double rowH = Median(row.Words.Select(w => w.H));
        double gapT = 0.75 * gapFactor * Math.Max(h, rowH);
        Chunk? cur = null;
        foreach (var w in row.Words)
        {
            if (cur != null && w.X - cur.R > gapT) { yield return cur; cur = null; }
            cur ??= new Chunk(rowIndex);
            cur.Add(w);
        }
        if (cur != null) yield return cur;
    }

    // ------------------------------------------------------------- columns

    private sealed class Column
    {
        public double L, R;
        public double W => R - L;
    }

    private static (List<Column>, List<Chunk>) AssignColumns(List<Chunk> chunks, double h)
    {
        var byRow = chunks.GroupBy(c => c.Row)
                          .ToDictionary(g => g.Key, g => g.OrderBy(c => c.L).ToList());
        if (byRow.Count == 0) return (new List<Column>(), new List<Chunk>());

        // The most common chunk count is our best guess at the real column count.
        int k = byRow.Values.Select(l => l.Count)
                     .GroupBy(n => n)
                     .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                     .First().Key;

        var rowOrder = byRow.Keys
            .OrderBy(r => byRow[r].Count == k ? 0 : 1)
            .ThenByDescending(r => byRow[r].Count)
            .ThenBy(r => r)
            .ToList();

        var columns = new List<Column>();
        var assigned = new List<Chunk>();
        foreach (var r in rowOrder)
            foreach (var c in byRow[r])
                assigned.AddRange(Assign(c, columns, h, allowSplit: true));

        return (columns, assigned);
    }

    private static List<Chunk> Assign(Chunk c, List<Column> columns, double h, bool allowSplit)
    {
        var cands = columns.Where(col => Overlaps(c, col)).OrderBy(col => col.L).ToList();

        if (cands.Count == 0)
        {
            var col = new Column { L = c.L, R = c.R };
            columns.Add(col);
            c.Col = col;
            return new List<Chunk> { c };
        }

        if (cands.Count == 1)
        {
            var col = cands[0];
            c.Col = col;
            col.L = Math.Min(col.L, c.L);
            col.R = Math.Max(col.R, c.R);
            return new List<Chunk> { c };
        }

        if (allowSplit)
        {
            var pieces = SplitAtGutters(c, cands, h);
            if (pieces.Count > 1)
                return pieces.SelectMany(p => Assign(p, columns, h, allowSplit: false)).ToList();
        }

        // A chunk spanning several columns (title, merged cell): anchor it on its left edge.
        double anchor = c.L + Math.Min(0.5 * h, c.W / 2);
        var target = cands.FirstOrDefault(col => anchor >= col.L && anchor <= col.R)
                     ?? cands.OrderByDescending(col => OverlapLen(c, col)).First();
        c.Col = target;
        return new List<Chunk> { c };
    }

    private static List<Chunk> SplitAtGutters(Chunk c, List<Column> cands, double h)
    {
        var gutters = new List<(double a, double b)>();
        for (int i = 0; i + 1 < cands.Count; i++)
        {
            double a = cands[i].R, b = cands[i + 1].L;
            if (b > a) gutters.Add((a, b));
        }
        if (gutters.Count == 0) return new List<Chunk> { c };

        var pieces = new List<Chunk>();
        var cur = new Chunk(c.Row);
        foreach (var w in c.Words)
        {
            if (cur.Words.Count > 0)
            {
                double gL = cur.R, gR = w.X;
                bool split = gR - gL >= 0.15 * h && gutters.Any(g => gL < g.b && gR > g.a);
                if (split) { pieces.Add(cur); cur = new Chunk(c.Row); }
            }
            cur.Add(w);
        }
        pieces.Add(cur);
        return pieces;
    }

    /// <summary>Merge adjacent columns that never both have content in the same row.</summary>
    private static void MergeLonelyColumns(List<Column> columns, List<Chunk> chunks)
    {
        bool changed = true;
        while (changed && columns.Count > 1)
        {
            changed = false;
            columns.Sort((a, b) => (a.L + a.R).CompareTo(b.L + b.R));
            for (int i = 0; i + 1 < columns.Count; i++)
            {
                var a = columns[i];
                var b = columns[i + 1];
                var rowsA = chunks.Where(c => c.Col == a).Select(c => c.Row).ToHashSet();
                var rowsB = chunks.Where(c => c.Col == b).Select(c => c.Row).ToHashSet();
                if (rowsA.Count == 0 || rowsB.Count == 0 || rowsA.Overlaps(rowsB)) continue;

                foreach (var c in chunks) if (c.Col == b) c.Col = a;
                a.L = Math.Min(a.L, b.L);
                a.R = Math.Max(a.R, b.R);
                columns.RemoveAt(i + 1);
                changed = true;
                break;
            }
        }
    }

    // ------------------------------------------------------------- helpers

    private static double OverlapLen(Chunk c, Column col) =>
        Math.Min(c.R, col.R) - Math.Max(c.L, col.L);

    private static bool Overlaps(Chunk c, Column col)
    {
        double ov = OverlapLen(c, col);
        if (ov <= 0) return false;
        double m = Math.Min(c.W, col.W);
        return m <= 0 || ov >= 0.3 * m;
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
