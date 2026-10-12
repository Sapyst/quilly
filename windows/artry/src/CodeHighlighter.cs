using System.Text.RegularExpressions;

namespace Artry;

internal static class CodeHighlighter
{
    private static readonly Color Keyword = Color.FromArgb(172, 76, 136);
    private static readonly Color String = Color.FromArgb(38, 126, 94);
    private static readonly Color Comment = Color.FromArgb(112, 120, 112);
    private static readonly Color Number = Color.FromArgb(43, 104, 168);

    public static bool IsCode(string? path)
    {
        var ext = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".cs" or ".csx" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".h" or ".css" or ".sql" or ".sh" or ".ps1" or ".json" or ".xml" or ".html" or ".yml" or ".yaml";
    }

    /// <param name="force">Permite realçar um rascunho ainda sem extensão de arquivo.</param>
    public static void Apply(RichTextBox box, string? path, Color foreground, bool force = false)
    {
        if ((!force && !IsCode(path)) || box.TextLength > 250_000) return;
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        box.SuspendLayout();
        box.SelectAll();
        box.SelectionColor = foreground;

        // Processamento linha a linha para evitar desvio de índice entre \n e \r\n
        var lines = box.Lines;
        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var lineText = lines[lineIdx];
            if (string.IsNullOrWhiteSpace(lineText)) continue;
            var lineStart = box.GetFirstCharIndexFromLine(lineIdx);
            if (lineStart < 0) continue;

            PaintLine(box, lineStart, lineText, @"//.*$|#.*$", Comment);
            PaintLine(box, lineStart, lineText, "\\\"(?:\\\\.|[^\\\"])*\\\"|'(?:\\\\.|[^'])*'", String);
            PaintLine(box, lineStart, lineText, @"\b\d+(?:\.\d+)?\b", Number);
            PaintLine(box, lineStart, lineText, @"\b(class|public|private|internal|static|void|string|int|bool|new|return|if|else|for|foreach|while|using|namespace|const|var|function|let|import|from|def|async|await|true|false|null|SELECT|FROM|WHERE|CREATE|INSERT|UPDATE|DELETE)\b", Keyword, RegexOptions.IgnoreCase);
        }

        box.Select(start, length);
        box.ResumeLayout();
    }

    private static void PaintLine(RichTextBox box, int lineStart, string text, string pattern, Color color, RegexOptions options = RegexOptions.None)
    {
        foreach (Match m in Regex.Matches(text, pattern, options))
        {
            box.Select(lineStart + m.Index, m.Length);
            box.SelectionColor = color;
        }
    }
}
