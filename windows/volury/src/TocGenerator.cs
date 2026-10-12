using System.Text;
using System.Xml.Linq;

namespace Volury;

/// <summary>Gerador automático de Table of Contents (TOC) a partir dos headings dos capítulos.</summary>
public static class TocGenerator
{
    public static void Gerar(EpubManager epub)
    {
        // Re-gerar o nav.xhtml e toc.ncx já é feito automaticamente no Salvar.
        // Aqui apenas atualizamos os títulos de cada capítulo baseando-se nos h1/h2/h3.
        foreach (var cap in epub.ListarCapitulos())
        {
            var titulo = ExtrairPrimeiroHeading(cap.Conteudo);
            if (!string.IsNullOrEmpty(titulo))
                cap.Titulo = titulo;
        }
    }

    private static string? ExtrairPrimeiroHeading(string xhtml)
    {
        try
        {
            var doc = XDocument.Parse(xhtml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var tag in new[] { "h1", "h2", "h3" })
            {
                var el = doc.Descendants(ns + tag).FirstOrDefault();
                if (el != null && !string.IsNullOrWhiteSpace(el.Value))
                    return el.Value.Trim();
            }
        }
        catch { }
        return null;
    }
}
