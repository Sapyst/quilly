using System.Text;

namespace Artry;

/// <summary>Exportador PDF textual simples, para não depender de driver ou biblioteca externa.</summary>
internal static class Pdf
{
    public static void Write(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').SelectMany(l => Wrap(l, 92)).ToArray();
        var pages = lines.Chunk(52).ToArray();
        if (pages.Length == 0) pages = [Array.Empty<string>()];
        var fontId = 3 + pages.Length * 2;
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "" };
        var pageIds = new List<int>();
        foreach (var page in pages)
        {
            var pageId = objects.Count + 1;
            var contentId = pageId + 1;
            pageIds.Add(pageId);
            var content = "BT /F1 10 Tf 52 790 Td 13 TL " + string.Join(" T* ", page.Select(l => "(" + Escape(l) + ") Tj")) + " ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        objects[1] = "<< /Type /Pages /Kids [" + string.Join(" ", pageIds.Select(i => i + " 0 R")) + $"] /Count {pageIds.Count} >>";
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write); using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true); writer.Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { writer.Flush(); offsets.Add(stream.Position); writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        writer.Flush(); var xref = stream.Position; writer.Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) writer.Write($"{offset:0000000000} 00000 n \n"); writer.Write($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }
    private static IEnumerable<string> Wrap(string s, int n) { if (s.Length == 0) return [""]; return Enumerable.Range(0, (s.Length + n - 1) / n).Select(i => s.Substring(i * n, Math.Min(n, s.Length - i * n))); }
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\\') sb.Append(@"\\");
            else if (c == '(') sb.Append(@"\(");
            else if (c == ')') sb.Append(@"\)");
            else if (c <= 255) sb.Append(c);
            else sb.Append('?');
        }
        return sb.ToString();
    }
}
