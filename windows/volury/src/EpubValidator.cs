using System.Xml.Linq;

namespace Volury;

/// <summary>Validador básico de conformidade EPUB.</summary>
public static class EpubValidator
{
    public sealed record ValidationError(string Severity, string Message);

    public static List<ValidationError> Validar(EpubManager epub)
    {
        var erros = new List<ValidationError>();

        // 1. Verifica se há capítulos
        var caps = epub.ListarCapitulos();
        if (caps.Count == 0)
            erros.Add(new("warning", "O livro não contém nenhum capítulo."));

        // 2. Verifica XHTML bem-formado em cada capítulo
        foreach (var cap in caps)
        {
            if (string.IsNullOrWhiteSpace(cap.Conteudo))
            {
                erros.Add(new("warning", $"O capítulo '{cap.Titulo}' ({cap.Id}) está vazio."));
                continue;
            }

            try
            {
                XDocument.Parse(cap.Conteudo);
            }
            catch (Exception ex)
            {
                erros.Add(new("error", $"XHTML inválido no capítulo '{cap.Titulo}' ({cap.Id}): {ex.Message}"));
            }
        }

        // 3. Verifica que todos os itens do spine existem no manifest
        foreach (var spineId in epub.SpineOrder)
        {
            if (!epub.TodosOsItens.Any(i => i.Id == spineId))
                erros.Add(new("error", $"Item do spine '{spineId}' não encontrado no manifest."));
        }

        // 4. Verifica metadados obrigatórios
        if (string.IsNullOrWhiteSpace(epub.Metadata.Titulo))
            erros.Add(new("error", "Metadado obrigatório 'título' está vazio."));

        if (string.IsNullOrWhiteSpace(epub.Metadata.Idioma))
            erros.Add(new("warning", "Metadado 'idioma' não definido. Usando 'pt' como padrão."));

        if (string.IsNullOrWhiteSpace(epub.Metadata.Identificador))
            erros.Add(new("error", "Metadado obrigatório 'identificador' (UUID) está vazio."));

        // 5. Verifica se imagens referenciadas nos capítulos existem
        var imagensDisponiveis = epub.ListarRecursos()
            .Where(r => r.Tipo == "image")
            .Select(r => Path.GetFileName(r.NomeArquivo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var cap in caps)
        {
            // Procura tags <img src="..."> simples
            var idx = 0;
            while ((idx = cap.Conteudo.IndexOf("src=\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                idx += 5;
                var end = cap.Conteudo.IndexOf('"', idx);
                if (end < 0) break;
                var src = cap.Conteudo[idx..end];
                var filename = Path.GetFileName(src);
                if (filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                    filename.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    if (!imagensDisponiveis.Contains(filename))
                        erros.Add(new("warning", $"Imagem '{filename}' referenciada no capítulo '{cap.Titulo}' não está no EPUB."));
                }
                idx = end;
            }
        }

        if (erros.Count == 0)
            erros.Add(new("info", "✓ EPUB válido! Nenhum problema encontrado."));

        return erros;
    }
}
