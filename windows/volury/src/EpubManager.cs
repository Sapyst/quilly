using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Volury;

/// <summary>Metadados do livro EPUB.</summary>
public sealed class EpubMetadata
{
    public string Titulo { get; set; } = "Sem Título";
    public string Autor { get; set; } = "Autor";
    public string Idioma { get; set; } = "pt";
    public string Descricao { get; set; } = "";
    public string Isbn { get; set; } = "";
    public string Identificador { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>Um item no manifest do EPUB (capítulo, imagem, CSS, etc.).</summary>
public sealed class EpubItem
{
    public string Id { get; set; } = "";
    public string NomeArquivo { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Tipo { get; set; } = ""; // "chapter", "image", "css", "nav", "ncx"
    public string Conteudo { get; set; } = "";
    public byte[]? DadosBinarios { get; set; }
    public string Titulo { get; set; } = "";
}

/// <summary>Motor de manipulação de arquivos EPUB 2 & 3.</summary>
public sealed class EpubManager
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Ncx = "http://www.daisy.org/z3986/2005/ncx/";
    private static readonly XNamespace XhtmlNs = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace EpubNs = "http://www.idpf.org/2007/ops";

    public EpubMetadata Metadata { get; } = new();
    public string? CaminhoAtual { get; private set; }

    private readonly List<EpubItem> _itens = [];
    private readonly List<string> _spine = []; // IDs na ordem de leitura
    private int _chapterCounter;

    // ──────────────────────────────────── Criar Novo
    public void CriarNovo(string titulo, string autor)
    {
        Metadata.Titulo = titulo;
        Metadata.Autor = autor;
        Metadata.Identificador = Guid.NewGuid().ToString();
        _itens.Clear();
        _spine.Clear();
        _chapterCounter = 0;
        CaminhoAtual = null;

        // Adiciona stylesheet padrão
        _itens.Add(new EpubItem
        {
            Id = "style",
            NomeArquivo = "OEBPS/Styles/style.css",
            MediaType = "text/css",
            Tipo = "css",
            Conteudo = CssPadrao()
        });
    }

    // ──────────────────────────────────── Abrir EPUB
    public void Abrir(string path)
    {
        _itens.Clear();
        _spine.Clear();
        _chapterCounter = 0;
        CaminhoAtual = path;

        using var zip = ZipFile.OpenRead(path);

        // Lê container.xml para encontrar o rootfile
        var containerEntry = zip.GetEntry("META-INF/container.xml");
        if (containerEntry == null) throw new InvalidOperationException("EPUB inválido: META-INF/container.xml não encontrado.");

        string opfPath;
        using (var stream = containerEntry.Open())
        {
            var doc = XDocument.Load(stream);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            opfPath = doc.Descendants(ns + "rootfile").FirstOrDefault()
                ?.Attribute("full-path")?.Value
                ?? "OEBPS/content.opf";
        }

        // Lê content.opf
        var opfEntry = zip.GetEntry(opfPath);
        if (opfEntry == null) throw new InvalidOperationException($"EPUB inválido: {opfPath} não encontrado.");

        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";
        if (opfDir.Length > 0) opfDir += "/";

        XDocument opfDoc;
        using (var stream = opfEntry.Open())
            opfDoc = XDocument.Load(stream);

        // Metadata
        var metaEl = opfDoc.Root?.Element(Opf + "metadata");
        if (metaEl != null)
        {
            Metadata.Titulo = metaEl.Element(Dc + "title")?.Value ?? "Sem Título";
            Metadata.Autor = metaEl.Element(Dc + "creator")?.Value ?? "Autor";
            Metadata.Idioma = metaEl.Element(Dc + "language")?.Value ?? "pt";
            Metadata.Descricao = metaEl.Element(Dc + "description")?.Value ?? "";
            Metadata.Identificador = metaEl.Element(Dc + "identifier")?.Value ?? Guid.NewGuid().ToString();
        }

        // Manifest
        var manifestEl = opfDoc.Root?.Element(Opf + "manifest");
        if (manifestEl != null)
        {
            foreach (var item in manifestEl.Elements(Opf + "item"))
            {
                var id = item.Attribute("id")?.Value ?? "";
                var href = item.Attribute("href")?.Value ?? "";
                var media = item.Attribute("media-type")?.Value ?? "";
                var fullPath = opfDir + href;

                var tipo = ClassificarTipo(media, item.Attribute("properties")?.Value);
                var epubItem = new EpubItem
                {
                    Id = id,
                    NomeArquivo = fullPath,
                    MediaType = media,
                    Tipo = tipo
                };

                // Lê conteúdo
                var entry = zip.GetEntry(fullPath);
                if (entry != null)
                {
                    if (tipo is "chapter" or "css" or "nav" or "ncx")
                    {
                        using var r = new StreamReader(entry.Open(), Encoding.UTF8);
                        epubItem.Conteudo = r.ReadToEnd();

                        // Extrai título do capítulo
                        if (tipo == "chapter")
                        {
                            epubItem.Titulo = ExtrairTituloXhtml(epubItem.Conteudo) ?? Path.GetFileNameWithoutExtension(href);
                        }
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        entry.Open().CopyTo(ms);
                        epubItem.DadosBinarios = ms.ToArray();
                    }
                }

                _itens.Add(epubItem);
            }
        }

        // Spine
        var spineEl = opfDoc.Root?.Element(Opf + "spine");
        if (spineEl != null)
        {
            foreach (var itemref in spineEl.Elements(Opf + "itemref"))
            {
                var idref = itemref.Attribute("idref")?.Value;
                if (idref != null) _spine.Add(idref);
            }
        }

        // Contador de capítulos
        _chapterCounter = _itens.Count(i => i.Tipo == "chapter");
    }

    // ──────────────────────────────────── Salvar EPUB
    public void Salvar(string path)
    {
        CaminhoAtual = path;
        if (File.Exists(path)) File.Delete(path);

        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);

        // 1. mimetype (sem compressão, primeiro arquivo)
        var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimeEntry.Open(), new UTF8Encoding(false)))
            w.Write("application/epub+zip");

        // 2. META-INF/container.xml
        var containerEntry = zip.CreateEntry("META-INF/container.xml");
        using (var w = new StreamWriter(containerEntry.Open(), Encoding.UTF8))
            w.Write(GerarContainerXml());

        // 3. content.opf
        var opfEntry = zip.CreateEntry("OEBPS/content.opf");
        using (var w = new StreamWriter(opfEntry.Open(), Encoding.UTF8))
            w.Write(GerarContentOpf());

        // 4. toc.ncx
        var ncxEntry = zip.CreateEntry("OEBPS/toc.ncx");
        using (var w = new StreamWriter(ncxEntry.Open(), Encoding.UTF8))
            w.Write(GerarTocNcx());

        // 5. nav.xhtml
        var navEntry = zip.CreateEntry("OEBPS/nav.xhtml");
        using (var w = new StreamWriter(navEntry.Open(), Encoding.UTF8))
            w.Write(GerarNavXhtml());

        // 6. Itens (capítulos, CSS, imagens, etc.)
        foreach (var item in _itens)
        {
            if (item.Tipo is "ncx" or "nav") continue; // já gerados acima
            var entryPath = item.NomeArquivo.StartsWith("OEBPS/") ? item.NomeArquivo : "OEBPS/" + item.NomeArquivo;
            var entry = zip.CreateEntry(entryPath);
            using var stream = entry.Open();
            if (item.DadosBinarios != null)
            {
                stream.Write(item.DadosBinarios);
            }
            else
            {
                using var w = new StreamWriter(stream, Encoding.UTF8);
                w.Write(item.Conteudo);
            }
        }
    }

    // ──────────────────────────────────── Capítulos
    public void AdicionarCapitulo(string titulo, string? conteudoXhtml = null)
    {
        _chapterCounter++;
        var id = $"chapter_{_chapterCounter}";
        var filename = $"OEBPS/Text/{id}.xhtml";

        var content = conteudoXhtml ?? TemplateCapitulo(titulo);

        _itens.Add(new EpubItem
        {
            Id = id,
            NomeArquivo = filename,
            MediaType = "application/xhtml+xml",
            Tipo = "chapter",
            Conteudo = content,
            Titulo = titulo
        });
        _spine.Add(id);
    }

    public void RemoverCapitulo(string id)
    {
        _itens.RemoveAll(i => i.Id == id);
        _spine.Remove(id);
    }

    public string ObterConteudoCapitulo(string id)
    {
        return _itens.FirstOrDefault(i => i.Id == id)?.Conteudo ?? "";
    }

    public void SalvarConteudoCapitulo(string id, string content)
    {
        var item = _itens.FirstOrDefault(i => i.Id == id);
        if (item != null)
        {
            item.Conteudo = content;
            item.Titulo = ExtrairTituloXhtml(content) ?? item.Titulo;
        }
    }

    public void ReordenarCapitulos(List<string> novaOrdem)
    {
        _spine.Clear();
        _spine.AddRange(novaOrdem);
    }

    public List<EpubItem> ListarCapitulos()
    {
        return _spine
            .Select(id => _itens.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null)
            .ToList()!;
    }

    // ──────────────────────────────────── Recursos
    public void AdicionarImagem(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var media = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
        var name = Path.GetFileName(filePath);
        var id = "img_" + Path.GetFileNameWithoutExtension(filePath).Replace(' ', '_');

        _itens.Add(new EpubItem
        {
            Id = id,
            NomeArquivo = $"OEBPS/Images/{name}",
            MediaType = media,
            Tipo = "image",
            DadosBinarios = File.ReadAllBytes(filePath)
        });
    }

    public void AdicionarCss(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var id = "css_" + Path.GetFileNameWithoutExtension(filePath).Replace(' ', '_');

        _itens.Add(new EpubItem
        {
            Id = id,
            NomeArquivo = $"OEBPS/Styles/{name}",
            MediaType = "text/css",
            Tipo = "css",
            Conteudo = File.ReadAllText(filePath, Encoding.UTF8)
        });
    }

    public List<EpubItem> ListarRecursos()
    {
        return _itens.Where(i => i.Tipo is "image" or "css").ToList();
    }

    // ──────────────────────────────────── XML generators

    private string GerarContainerXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private string GerarContentOpf()
    {
        var manifest = new StringBuilder();
        manifest.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        manifest.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");

        foreach (var item in _itens.Where(i => i.Tipo is not "ncx" and not "nav"))
        {
            var href = item.NomeArquivo.StartsWith("OEBPS/")
                ? item.NomeArquivo["OEBPS/".Length..]
                : item.NomeArquivo;
            manifest.AppendLine($"    <item id=\"{Escape(item.Id)}\" href=\"{Escape(href)}\" media-type=\"{item.MediaType}\"/>");
        }

        var spine = new StringBuilder();
        foreach (var id in _spine)
            spine.AppendLine($"    <itemref idref=\"{Escape(id)}\"/>");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="bookid" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:{Metadata.Identificador}</dc:identifier>
                <dc:title>{Escape(Metadata.Titulo)}</dc:title>
                <dc:creator>{Escape(Metadata.Autor)}</dc:creator>
                <dc:language>{Escape(Metadata.Idioma)}</dc:language>
                <dc:description>{Escape(Metadata.Descricao)}</dc:description>
                <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
              </metadata>
              <manifest>
            {manifest}  </manifest>
              <spine toc="ncx">
            {spine}  </spine>
            </package>
            """;
    }

    private string GerarTocNcx()
    {
        var points = new StringBuilder();
        int order = 1;
        foreach (var id in _spine)
        {
            var item = _itens.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            var href = item.NomeArquivo.StartsWith("OEBPS/") ? item.NomeArquivo["OEBPS/".Length..] : item.NomeArquivo;
            points.AppendLine($"""
                    <navPoint id="navPoint-{order}" playOrder="{order}">
                      <navLabel><text>{Escape(item.Titulo)}</text></navLabel>
                      <content src="{Escape(href)}"/>
                    </navPoint>
                """);
            order++;
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="urn:uuid:{Metadata.Identificador}"/>
              </head>
              <docTitle><text>{Escape(Metadata.Titulo)}</text></docTitle>
              <navMap>
            {points}  </navMap>
            </ncx>
            """;
    }

    private string GerarNavXhtml()
    {
        var items = new StringBuilder();
        foreach (var id in _spine)
        {
            var item = _itens.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            var href = item.NomeArquivo.StartsWith("OEBPS/") ? item.NomeArquivo["OEBPS/".Length..] : item.NomeArquivo;
            items.AppendLine($"        <li><a href=\"{Escape(href)}\">{Escape(item.Titulo)}</a></li>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Sumário</title></head>
            <body>
              <nav epub:type="toc" id="toc">
                <h1>Sumário</h1>
                <ol>
            {items}    </ol>
              </nav>
            </body>
            </html>
            """;
    }

    // ──────────────────────────────────── Helpers

    internal List<EpubItem> TodosOsItens => _itens;
    internal List<string> SpineOrder => _spine;

    private static string ClassificarTipo(string mediaType, string? properties)
    {
        if (properties?.Contains("nav") == true) return "nav";
        return mediaType switch
        {
            "application/xhtml+xml" => "chapter",
            "text/css" => "css",
            "application/x-dtbncx+xml" => "ncx",
            _ when mediaType.StartsWith("image/") => "image",
            _ when mediaType.StartsWith("font/") || mediaType == "application/font-woff" => "font",
            _ => "other"
        };
    }

    private static string? ExtrairTituloXhtml(string xhtml)
    {
        try
        {
            var doc = XDocument.Parse(xhtml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            // Procura <title>, <h1>, <h2>
            var title = doc.Descendants(ns + "title").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
            title = doc.Descendants(ns + "h1").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
            title = doc.Descendants(ns + "h2").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        }
        catch { }
        return null;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string TemplateCapitulo(string titulo) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <head>
          <title>{Escape(titulo)}</title>
          <link rel="stylesheet" type="text/css" href="../Styles/style.css"/>
        </head>
        <body>
          <h1>{Escape(titulo)}</h1>
          <p></p>
        </body>
        </html>
        """;

    private static string CssPadrao() => """
        /* Volury — Estilo padrão do e-book */
        body {
            font-family: Georgia, "Times New Roman", serif;
            font-size: 1em;
            line-height: 1.6;
            margin: 2em;
            color: #333;
        }
        h1, h2, h3 {
            font-family: Georgia, "Times New Roman", serif;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
        }
        h1 { font-size: 2em; text-align: center; margin-top: 3em; }
        h2 { font-size: 1.5em; }
        h3 { font-size: 1.2em; }
        p { text-indent: 1.5em; margin: 0.3em 0; }
        p:first-of-type { text-indent: 0; }
        blockquote {
            margin: 1em 2em;
            font-style: italic;
            border-left: 3px solid #ccc;
            padding-left: 1em;
        }
        """;
}
