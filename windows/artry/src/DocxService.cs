using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Artry;

/// <summary>Leitor/escritor DOCX deliberadamente pequeno: texto e formatação essencial.</summary>
internal static class DocxService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX sem document.xml.");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var output = new StringBuilder();
        foreach (var p in doc.Descendants(W + "p"))
        {
            foreach (var node in p.Descendants())
            {
                if (node.Name == W + "t") output.Append(node.Value);
                else if (node.Name == W + "tab") output.Append('\t');
                else if (node.Name == W + "br") output.AppendLine();
            }
            output.AppendLine();
        }
        return output.ToString().TrimEnd();
    }

    public static void Write(string path, RichTextBox editor)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
        Add(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
        var body = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        foreach (var line in editor.Text.Replace("\r\n", "\n").Split('\n'))
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(line) ?? "").Append("</w:t></w:r></w:p>");
        body.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:body></w:document>");
        Add(zip, "word/document.xml", body.ToString());
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
