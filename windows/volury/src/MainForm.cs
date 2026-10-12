using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Volury;

public sealed class MainForm : Form
{
    // ──────────────────────────────────────── DWM P/Invoke
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    // ──────────────────────────────────────── Paths
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string ProfileDir = Path.Combine(AppDir, "profile");

    // ──────────────────────────────────────── Fields
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(20, 23, 26) };
    private readonly string? _initialFile;
    private EpubManager? _epub;
    private CoreWebView2Environment? _env;

    // ──────────────────────────────────────── Ctor
    public MainForm(string? initialFile = null)
    {
        _initialFile = initialFile;
        Text = "Volury";
        MinimumSize = new Size(960, 620);
        ClientSize = new Size(1200, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 23, 26);

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Volury.icon.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        Controls.Add(_web);
    }

    // ──────────────────────────────────────── DWM Theming
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AplicaTemaBarraTitulo();
    }

    private void AplicaTemaBarraTitulo()
    {
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

            // Cor da barra de titulo: #14171A → COLORREF 0x00BBGGRR = 0x001A1714
            int captionColor = 0x001A1714;
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            // Cor do texto: #F8F5F2 → 0x00F2F5F8
            int textColor = 0x00F2F5F8;
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

            // Cor da borda: #2D333D → 0x003D332D
            int borderColor = 0x003D332D;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { /* Windows 10: fica com o padrão */ }
    }

    // ──────────────────────────────────────── Lifecycle
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AplicaTemaBarraTitulo();

        Directory.CreateDirectory(ProfileDir);
        _env = await CoreWebView2Environment.CreateAsync(null, ProfileDir);
        await _web.EnsureCoreWebView2Async(_env);

        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;

        // Carrega editor.html de embedded resource
        var asm = Assembly.GetExecutingAssembly();
        var html = LoadResource(asm, "Volury.ui.editor.html");
        var css = LoadResource(asm, "Volury.ui.style.css");
        var js = LoadResource(asm, "Volury.ui.app.js");

        // Injeta CSS e JS inline no HTML
        html = html.Replace("/* __INLINE_CSS__ */", css);
        html = html.Replace("/* __INLINE_JS__ */", js);

        _web.CoreWebView2.NavigateToString(html);

        // Se recebeu um arquivo, abre depois que a UI carregar
        if (_initialFile != null)
        {
            _web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                await Task.Delay(300);
                await AbrirArquivo(_initialFile);
            };
        }
    }

    private static string LoadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return "";
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ──────────────────────────────────────── C# ↔ JS Bridge
    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.WebMessageAsJson;
            var msg = JsonSerializer.Deserialize<JsonObject>(raw);
            if (msg == null) return;

            var action = msg["action"]?.GetValue<string>() ?? "";
            var payload = msg["payload"];

            switch (action)
            {
                case "new-epub":
                    NovoEpub();
                    break;

                case "open-epub":
                    await AbrirEpubDialog();
                    break;

                case "save-epub":
                    SalvarEpub();
                    break;

                case "save-epub-as":
                    SalvarEpubComo();
                    break;

                case "import-hyp":
                    await ImportarHypDialog();
                    break;

                case "add-chapter":
                    AdicionarCapitulo(payload?["title"]?.GetValue<string>());
                    break;

                case "delete-chapter":
                    DeletarCapitulo(payload?["id"]?.GetValue<string>());
                    break;

                case "get-chapter-content":
                    EnviarConteudoCapitulo(payload?["id"]?.GetValue<string>());
                    break;

                case "save-chapter-content":
                    SalvarConteudoCapitulo(
                        payload?["id"]?.GetValue<string>(),
                        payload?["content"]?.GetValue<string>());
                    break;

                case "reorder-chapters":
                    ReordenarCapitulos(payload?["order"]);
                    break;

                case "update-metadata":
                    AtualizarMetadata(payload);
                    break;

                case "get-metadata":
                    EnviarMetadata();
                    break;

                case "add-image":
                    await AdicionarImagemDialog();
                    break;

                case "add-stylesheet":
                    await AdicionarCssDialog();
                    break;

                case "generate-toc":
                    GerarToc();
                    break;

                case "validate":
                    Validar();
                    break;

                case "export-epub":
                    ExportarEpub();
                    break;
            }
        }
        catch (Exception ex)
        {
            EnviarParaJS("error", new { message = ex.Message });
        }
    }

    // ──────────────────────────────────────── Actions

    private void NovoEpub()
    {
        _epub = new EpubManager();
        _epub.CriarNovo("Sem Título", "Autor");
        Text = "Volury — Sem Título";
        AtualizarUI();
    }

    private async Task AbrirEpubDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Abrir EPUB",
            Filter = "EPUB (*.epub)|*.epub|Documento Hyper (*.hyp)|*.hyp|Todos os arquivos (*.*)|*.*",
            FilterIndex = 1
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await AbrirArquivo(dlg.FileName);
    }

    private async Task AbrirArquivo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".hyp")
        {
            _epub = new EpubManager();
            _epub.CriarNovo(Path.GetFileNameWithoutExtension(path), "Autor");
            var html = HypImporter.ImportarComoXhtml(path);
            _epub.AdicionarCapitulo(Path.GetFileNameWithoutExtension(path), html);
        }
        else
        {
            _epub = new EpubManager();
            await Task.Run(() => _epub.Abrir(path));
        }
        Text = $"Volury — {Path.GetFileName(path)}";
        AtualizarUI();
    }

    private void SalvarEpub()
    {
        if (_epub == null) return;
        if (string.IsNullOrEmpty(_epub.CaminhoAtual))
        {
            SalvarEpubComo();
            return;
        }
        _epub.Salvar(_epub.CaminhoAtual);
        EnviarParaJS("saved", new { path = _epub.CaminhoAtual });
    }

    private void SalvarEpubComo()
    {
        if (_epub == null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Salvar EPUB como",
            Filter = "EPUB (*.epub)|*.epub",
            FileName = _epub.Metadata.Titulo + ".epub"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _epub.Salvar(dlg.FileName);
        Text = $"Volury — {Path.GetFileName(dlg.FileName)}";
        EnviarParaJS("saved", new { path = dlg.FileName });
    }

    private async Task ImportarHypDialog()
    {
        if (_epub == null) { NovoEpub(); }
        using var dlg = new OpenFileDialog
        {
            Title = "Importar .hyp",
            Filter = "Documento Hyper (*.hyp)|*.hyp"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var html = await Task.Run(() => HypImporter.ImportarComoXhtml(dlg.FileName));
        _epub!.AdicionarCapitulo(Path.GetFileNameWithoutExtension(dlg.FileName), html);
        AtualizarUI();
    }

    private void AdicionarCapitulo(string? titulo)
    {
        if (_epub == null) NovoEpub();
        _epub!.AdicionarCapitulo(titulo ?? "Novo Capítulo");
        AtualizarUI();
    }

    private void DeletarCapitulo(string? id)
    {
        if (_epub == null || id == null) return;
        _epub.RemoverCapitulo(id);
        AtualizarUI();
    }

    private void EnviarConteudoCapitulo(string? id)
    {
        if (_epub == null || id == null) return;
        var content = _epub.ObterConteudoCapitulo(id);
        EnviarParaJS("chapter-content", new { id, content });
    }

    private void SalvarConteudoCapitulo(string? id, string? content)
    {
        if (_epub == null || id == null || content == null) return;
        _epub.SalvarConteudoCapitulo(id, content);
    }

    private void ReordenarCapitulos(JsonNode? orderNode)
    {
        if (_epub == null || orderNode == null) return;
        var order = orderNode.Deserialize<List<string>>() ?? [];
        _epub.ReordenarCapitulos(order);
    }

    private void AtualizarMetadata(JsonNode? data)
    {
        if (_epub == null || data == null) return;
        var meta = _epub.Metadata;
        meta.Titulo = data["title"]?.GetValue<string>() ?? meta.Titulo;
        meta.Autor = data["author"]?.GetValue<string>() ?? meta.Autor;
        meta.Idioma = data["language"]?.GetValue<string>() ?? meta.Idioma;
        meta.Descricao = data["description"]?.GetValue<string>() ?? meta.Descricao;
        Text = $"Volury — {meta.Titulo}";
    }

    private void EnviarMetadata()
    {
        if (_epub == null) return;
        var m = _epub.Metadata;
        EnviarParaJS("metadata", new { title = m.Titulo, author = m.Autor, language = m.Idioma, description = m.Descricao });
    }

    private async Task AdicionarImagemDialog()
    {
        if (_epub == null) return;
        using var dlg = new OpenFileDialog
        {
            Title = "Adicionar imagem",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.gif;*.svg)|*.png;*.jpg;*.jpeg;*.gif;*.svg"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await Task.Run(() => _epub.AdicionarImagem(dlg.FileName));
        AtualizarUI();
    }

    private async Task AdicionarCssDialog()
    {
        if (_epub == null) return;
        using var dlg = new OpenFileDialog
        {
            Title = "Adicionar folha de estilos",
            Filter = "CSS (*.css)|*.css"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await Task.Run(() => _epub.AdicionarCss(dlg.FileName));
        AtualizarUI();
    }

    private void GerarToc()
    {
        if (_epub == null) return;
        TocGenerator.Gerar(_epub);
        EnviarParaJS("toc-generated", new { success = true });
    }

    private void Validar()
    {
        if (_epub == null) return;
        var erros = EpubValidator.Validar(_epub);
        EnviarParaJS("validation-result", new { errors = erros });
    }

    private void ExportarEpub()
    {
        SalvarEpubComo();
    }

    // ──────────────────────────────────────── Helpers

    private void AtualizarUI()
    {
        if (_epub == null) return;
        var caps = _epub.ListarCapitulos().Select(c => new { c.Id, c.Titulo }).ToArray();
        var recursos = _epub.ListarRecursos().Select(r => new { r.Id, r.Tipo, r.NomeArquivo }).ToArray();
        EnviarParaJS("update-ui", new { chapters = caps, resources = recursos, metadata = new { title = _epub.Metadata.Titulo, author = _epub.Metadata.Autor } });
    }

    private void EnviarParaJS(string action, object payload)
    {
        if (!_web.IsHandleCreated || _web.CoreWebView2 == null) return;
        var json = JsonSerializer.Serialize(new { action, payload });
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }
}
