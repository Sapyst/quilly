using System.Text;
using System.Text.RegularExpressions;

namespace Volury;

/// <summary>Importa arquivos .hyp (Noty/Artry) para XHTML compatível com EPUB.</summary>
public static partial class HypImporter
{
    /// <summary>Lê um arquivo .hyp e retorna XHTML válido para inserção como capítulo.</summary>
    public static string ImportarComoXhtml(string path)
    {
        var raw = File.ReadAllText(path, Encoding.UTF8);
        var titulo = Path.GetFileNameWithoutExtension(path);

        // .hyp é essencialmente um documento de texto rico.
        // Tenta detectar se é HTML/RTF ou texto puro.
        if (raw.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            raw.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            // Já é HTML — extrai o body e re-embala como XHTML
            return ReembalarComoXhtml(raw, titulo);
        }

        if (raw.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
        {
            // RTF — converte para texto puro (simplificado)
            var texto = StripRtf(raw);
            return TextoParaXhtml(texto, titulo);
        }

        // Texto puro ou Markdown-like
        return TextoParaXhtml(raw, titulo);
    }

    private static string TextoParaXhtml(string texto, string titulo)
    {
        var sb = new StringBuilder();
        var linhas = texto.Split('\n');
        bool primeiroParagrafo = true;

        foreach (var linhaRaw in linhas)
        {
            var linha = linhaRaw.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(linha))
            {
                primeiroParagrafo = true;
                continue;
            }

            // Detecta headings Markdown
            if (linha.StartsWith("### "))
            {
                sb.AppendLine($"  <h3>{Escape(linha[4..])}</h3>");
                primeiroParagrafo = true;
            }
            else if (linha.StartsWith("## "))
            {
                sb.AppendLine($"  <h2>{Escape(linha[3..])}</h2>");
                primeiroParagrafo = true;
            }
            else if (linha.StartsWith("# "))
            {
                sb.AppendLine($"  <h1>{Escape(linha[2..])}</h1>");
                primeiroParagrafo = true;
            }
            else
            {
                // Aplica negrito/itálico inline
                var processada = ProcessarInline(Escape(linha));
                sb.AppendLine($"  <p>{processada}</p>");
                primeiroParagrafo = false;
            }
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
              <title>{Escape(titulo)}</title>
              <link rel="stylesheet" type="text/css" href="../Styles/style.css"/>
            </head>
            <body>
              <h1>{Escape(titulo)}</h1>
            {sb}
            </body>
            </html>
            """;
    }

    private static string ReembalarComoXhtml(string html, string titulo)
    {
        // Extrai conteúdo do <body> se existir
        var bodyMatch = BodyRegex().Match(html);
        var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
              <title>{Escape(titulo)}</title>
              <link rel="stylesheet" type="text/css" href="../Styles/style.css"/>
            </head>
            <body>
            {body.Trim()}
            </body>
            </html>
            """;
    }

    private static string StripRtf(string rtf)
    {
        // Remoção simplificada de tags RTF
        var sb = new StringBuilder();
        int i = 0;
        int depth = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; continue; }
            if (c == '\\')
            {
                i++;
                // Pula palavras-de-controle
                if (i < rtf.Length && rtf[i] == '\'')
                {
                    i += 3; // skip hex char
                    continue;
                }
                while (i < rtf.Length && char.IsLetter(rtf[i])) i++;
                if (i < rtf.Length && (rtf[i] == ' ' || char.IsDigit(rtf[i]) || rtf[i] == '-'))
                {
                    while (i < rtf.Length && (char.IsDigit(rtf[i]) || rtf[i] == '-')) i++;
                    if (i < rtf.Length && rtf[i] == ' ') i++;
                }
                continue;
            }
            if (depth <= 1) sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string ProcessarInline(string text)
    {
        // **bold** → <strong>bold</strong>
        text = BoldRegex().Replace(text, "<strong>$1</strong>");
        // *italic* → <em>italic</em>
        text = ItalicRegex().Replace(text, "<em>$1</em>");
        return text;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex(@"<body[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex ItalicRegex();
}
