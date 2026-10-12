using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Noty;

public sealed class MainForm : Form
{
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string ProfileDir = Path.Combine(AppDir, "profile");
    private static readonly string StatePath = Path.Combine(ProfileDir, "window.json");
    private static readonly string SettingsPath = Path.Combine(ProfileDir, "settings.json");
    private static readonly string ThemePath = Path.Combine(ProfileDir, "theme.css");

    /// <summary>Icone do app (multi-resolucao), embutido no exe como recurso.</summary>
    private static readonly Icon? AppIcon = LoadAppIcon();

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Noty.icon.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch { /* cai no plano B */ }

        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null) return Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* fica no icone padrao */ }

        return null;
    }

    // Vidro: com o acrilico ligado, o form fica preto (o DWM troca preto pelo
    // material) e o WebView2 transparente deixa o desktop embacado atravessar.
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };

    private readonly string? _arquivoInicial;
    private bool _sujo;            // JS avisa quando ha alteracao nao salva
    private bool _uiPronta;        // editor.html carregado: dialogos sao do JS
    private bool _forcarSaida;     // o dialogo do JS ja confirmou: fecha sem perguntar
    private bool _ehMarkdown = true;

    private JsonObject _settings = new();
    private CoreWebView2Environment? _env;

    private sealed class AppState
    {
        public int X { get; set; } = int.MinValue;
        public int Y { get; set; } = int.MinValue;
        public int W { get; set; } = 980;
        public int H { get; set; } = 700;
        public bool Maximized { get; set; }
    }

    private AppState _state = new();

    public MainForm(string? arquivoInicial)
    {
        _arquivoInicial = arquivoInicial;

        Text = "Noty";
        MinimumSize = new Size(420, 320);
        ClientSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;

        if (AppIcon is not null) Icon = AppIcon;
        Controls.Add(_web);
    }

    // ---------------------------------------------------------------- lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AtivaVidro();
    }

    private void AtivaVidro()
    {
        try
        {
            var m = new Native.MARGINS { L = -1, R = -1, T = -1, B = -1 };
            Native.DwmExtendFrameIntoClientArea(Handle, ref m);
            int tipo = 1; // sem material do DWM: quem faz o vidro e o accent abaixo
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref tipo, sizeof(int));
            BackColor = Color.Black;

            // Barra de titulo sem cor chapada: o proprio acrilico pinta ela.
            int def = unchecked((int)0xFFFFFFFF);   // DWMWA_COLOR_DEFAULT
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CAPTION_COLOR, ref def, sizeof(int));
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref def, sizeof(int));
            int immersive = 1;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref immersive, sizeof(int));

            AplicaAcrilico(ativa: true);
        }
        catch { /* Windows 10: fica opaco, sem drama */ }
    }

    // Vidro via accent policy: nao some quando a janela perde o foco.
    // Tint AABBGGRR — ativa bem clara, em segundo plano mais escurinha.
    private void AplicaAcrilico(bool ativa)
    {
        if (!IsHandleCreated) return;
        try
        {
            // Fume escuro com um sussurro de verde (AABBGGRR).
            uint tint = ativa ? 0x400F140Du : 0x900F140Du;
            var pol = new Native.ACCENT_POLICY
            {
                State = Native.ACCENT_ACRYLIC,
                Flags = 0,
                GradientColor = tint,
            };
            var h = System.Runtime.InteropServices.GCHandle.Alloc(pol, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var data = new Native.WINCOMPATTRDATA
                {
                    Attribute = Native.WCA_ACCENT_POLICY,
                    Data = h.AddrOfPinnedObject(),
                    SizeOfData = System.Runtime.InteropServices.Marshal.SizeOf<Native.ACCENT_POLICY>(),
                };
                Native.SetWindowCompositionAttribute(Handle, ref data);
            }
            finally { h.Free(); }
        }
        catch { }
    }

    protected override void WndProc(ref Message m)
    {
        // Vidro: clarinho com a janela ativa, mais escurinho quando ela vai pra tras.
        if (m.Msg == Native.WM_ACTIVATE) AplicaAcrilico(ativa: m.WParam != IntPtr.Zero);
        base.WndProc(ref m);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadState();
        LoadSettings();

        try
        {
            await InitWebViewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Nao foi possivel iniciar o WebView2.\n\n" + ex.Message +
                "\n\nInstale o \"Microsoft Edge WebView2 Runtime\" e tente de novo.",
                "Noty", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_sujo && !_forcarSaida && e.CloseReason == CloseReason.UserClosing)
        {
            if (_uiPronta)
            {
                // A pergunta e feita pelo dialogo de vidro do proprio editor;
                // se a resposta for sair, o JS chama app.exitForce.
                e.Cancel = true;
                PostToJs(new { type = "action", action = "confirmExit" });
                return;
            }

            // WebView2 fora do ar: pergunta nativa mesmo, melhor que perder texto.
            var r = MessageBox.Show(this,
                "Tem alteracao que nao foi salva. Sair mesmo assim?",
                "Noty", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }

        SaveState();
        base.OnFormClosing(e);
    }

    // ---------------------------------------------------------------- webview

    private async Task InitWebViewAsync()
    {
        Directory.CreateDirectory(ProfileDir);
        GaranteTemaPadrao();

        // O idioma do ambiente e o idioma do corretor ortografico do Chromium.
        // O dicionario do idioma e baixado automaticamente (padrao Google) na
        // primeira vez que o corretor roda com internet.
        var lang = SpellLang();
        var options = new CoreWebView2EnvironmentOptions { Language = lang };

        _env = await CoreWebView2Environment.CreateAsync(null, ProfileDir, options);
        await _web.EnsureCoreWebView2Async(_env);

        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = true;    // e dele que vem a sugestao de ortografia
        s.AreDevToolsEnabled = true;
        s.IsStatusBarEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsWebMessageEnabled = true;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;   // atalhos sao do editor
        s.IsZoomControlEnabled = false;               // tamanho de fonte e do tema, nao zoom

        core.WebMessageReceived += OnWebMessageReceived;
        core.ContextMenuRequested += OnContextMenuRequested;
        core.NewWindowRequested += (_, ev) => { ev.Handled = true; OpenExternal(ev.Uri); };
        core.ProcessFailed += OnProcessFailed;

        core.NavigateToString(LoadEditorHtml());
        _uiPronta = true;
    }

    private static string LoadEditorHtml()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Noty.ui.editor.html")
            ?? throw new InvalidOperationException("recurso Noty.ui.editor.html nao encontrado");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            MessageBox.Show(this, "O processo do WebView2 encerrou. O Noty vai fechar.",
                "Noty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }
        try { _web.CoreWebView2.NavigateToString(LoadEditorHtml()); } catch { }
    }

    // ---------------------------------------------------------------- menu de contexto

    // Itens do Chromium que nao fazem sentido num editor local.
    private static readonly HashSet<string> MenuBloqueado = new(StringComparer.OrdinalIgnoreCase)
    {
        "back", "forward", "reload", "saveAs", "print", "share", "webCapture",
        "webSelect", "magnify", "createQrCode", "inspectElement",
        "openLinkInNewWindow", "saveLinkAs", "copyLinkLocation",
        "saveImageAs", "copyImage", "copyImageLocation", "openImageInNewWindow",
    };

    private CoreWebView2ContextMenuItem? _miSalvar, _miSalvarComo, _miAbrir, _miSep1, _miSep2;
    private CoreWebView2ContextMenuItem? _miNegrito, _miItalico, _miCodigo, _miPesquisar;

    private CoreWebView2ContextMenuItem MenuItem(string rotulo, string acao)
    {
        var item = _env!.CreateContextMenuItem(rotulo, null, CoreWebView2ContextMenuItemKind.Command);
        item.CustomItemSelected += (_, _) => PostToJs(new { type = "action", action = acao });
        return item;
    }

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        try
        {
            var itens = e.MenuItems;
            for (var i = itens.Count - 1; i >= 0; i--)
                if (MenuBloqueado.Contains(itens[i].Name)) itens.RemoveAt(i);

            // Tira separador sobrando nas pontas / duplicado.
            for (var i = itens.Count - 1; i >= 0; i--)
            {
                var sep = itens[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
                var vizinhoSep = i > 0 && itens[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator;
                if (sep && (i == 0 || i == itens.Count - 1 || vizinhoSep)) itens.RemoveAt(i);
            }

            _miSalvar ??= MenuItem("Salvar", "save");
            _miSalvarComo ??= MenuItem("Salvar como…", "saveAs");
            _miAbrir ??= MenuItem("Abrir…", "open");
            _miNegrito ??= MenuItem("Negrito", "md-bold");
            _miItalico ??= MenuItem("Itálico", "md-italic");
            _miCodigo ??= MenuItem("Código", "md-code");
            _miPesquisar ??= MenuItem("Pesquisar na web", "searchWeb");
            _miSep1 ??= _env!.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
            _miSep2 ??= _env!.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);

            // Com selecao: acoes sobre o trecho (formatar se for .md, pesquisar).
            // Sem selecao: acoes de arquivo.
            if (e.ContextMenuTarget.HasSelection)
            {
                var pos = 0;
                if (_ehMarkdown)
                {
                    itens.Insert(pos++, _miNegrito);
                    itens.Insert(pos++, _miItalico);
                    itens.Insert(pos++, _miCodigo);
                    itens.Insert(pos++, _miSep1);
                }
                itens.Add(_miSep2);
                itens.Add(_miPesquisar);
            }
            else
            {
                if (itens.Count > 0) itens.Add(_miSep1);
                itens.Add(_miAbrir);
                itens.Add(_miSalvar);
                itens.Add(_miSalvarComo);
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---------------------------------------------------------------- mensagens do JS

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        string type;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            msg = doc.RootElement.Clone();
            type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        }
        catch { return; }

        switch (type)
        {
            case "rpc":
                try { await HandleRpcAsync(msg); } catch (Exception ex) { Debug.WriteLine(ex); }
                return;

            case "dirty":
                _sujo = msg.TryGetProperty("value", out var d) && d.GetBoolean();
                return;

            case "title":
                Text = msg.TryGetProperty("text", out var tt) ? (tt.GetString() ?? "Noty") : "Noty";
                return;

            case "fileinfo":
                _ehMarkdown = !(msg.TryGetProperty("ext", out var ex2) && ex2.GetString() == ".txt");
                return;

            case "devtools":
                try { _web.CoreWebView2.OpenDevToolsWindow(); } catch { }
                return;
        }
    }

    private void PostToJs(object payload)
    {
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload)); }
        catch { /* pagina trocou */ }
    }

    // ---------------------------------------------------------------- rpc

    private async Task HandleRpcAsync(JsonElement msg)
    {
        var id = msg.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;
        var method = msg.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        var args = msg.TryGetProperty("args", out var a) ? a : default;

        try { Reply(id, true, await DispatchRpcAsync(method, args), null); }
        catch (Exception ex) { Reply(id, false, null, ex.Message); }

        void Reply(int rid, bool ok, object? data, string? error) => PostToJs(new Dictionary<string, object?>
        {
            ["type"] = "rpc-reply",
            ["id"] = rid,
            ["ok"] = ok,
            ["data"] = data,
            ["error"] = error
        });
    }

    private async Task<object?> DispatchRpcAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "file.openDialog":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Abrir",
                    Filter = "Documentos Hyper (*.hyp)|*.hyp|Todos os textos (*.hyp;*.md;*.txt;*.html;*.htm)|*.hyp;*.md;*.txt;*.html;*.htm|Markdown (*.md)|*.md|Página Web (*.html;*.htm)|*.html;*.htm|Texto Puro (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                return await LerArquivoAsync(dlg.FileName);
            }

            case "file.openPath":
            {
                var path = args.GetProperty("path").GetString() ?? throw new InvalidOperationException("sem caminho");
                return await LerArquivoAsync(path);
            }

            case "file.save":
            {
                var path = args.GetProperty("path").GetString() ?? throw new InvalidOperationException("sem caminho");
                var text = args.GetProperty("text").GetString() ?? "";
                await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
                return new { ok = true };
            }

            case "file.saveDialog":
            {
                var sugestao = args.TryGetProperty("name", out var n) ? n.GetString() ?? "sem-titulo.hyp" : "sem-titulo.hyp";
                using var dlg = new SaveFileDialog
                {
                    Title = "Salvar como",
                    FileName = sugestao,
                    Filter = "Documento Hyper (*.hyp)|*.hyp|Documento Markdown (*.md)|*.md|Página Web (*.html)|*.html|Texto Puro (*.txt)|*.txt",
                    FilterIndex = sugestao.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? 2
                                : sugestao.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || sugestao.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ? 3
                                : sugestao.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? 4 : 1,
                    AddExtension = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                return new { path = dlg.FileName, name = Path.GetFileName(dlg.FileName) };
            }

            case "theme.read":
                GaranteTemaPadrao();
                return new { text = await File.ReadAllTextAsync(ThemePath, Encoding.UTF8) };

            case "theme.write":
            {
                var text = args.GetProperty("text").GetString() ?? "";
                await File.WriteAllTextAsync(ThemePath, text, new UTF8Encoding(false));
                return new { ok = true };
            }

            case "theme.default":
                return new { text = TemaPadrao };

            case "settings.get":
                return _settings.DeepClone();

            case "settings.set":
            {
                // O JS manda o objeto inteiro de volta; o C# so preserva o que e dele.
                var novo = JsonNode.Parse(args.GetRawText()) as JsonObject ?? new JsonObject();
                _settings = novo;
                SaveSettings();
                return new { ok = true };
            }

            case "spell.set":
            {
                var lang = args.TryGetProperty("lang", out var l) ? l.GetString() ?? "pt-BR" : "pt-BR";
                var mudou = lang != SpellLang();
                _settings["spellLang"] = lang;
                SaveSettings();
                // O idioma do corretor e fixado na criacao do ambiente: so vale
                // depois de reiniciar o app.
                return new { restart = mudou };
            }

            case "app.info":
                return new
                {
                    version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                    appDir = AppDir,
                    profileDir = ProfileDir,
                    spellLang = SpellLang(),
                    initialFile = _arquivoInicial,
                };

            case "app.openExternal":
                OpenExternal(args.GetProperty("url").GetString() ?? "");
                return new { ok = true };

            case "app.openProfile":
                try { Process.Start(new ProcessStartInfo(ProfileDir) { UseShellExecute = true }); } catch { }
                return new { ok = true };

            case "app.restart":
                _sujo = false;
                Application.Restart();
                return new { ok = true };

            case "app.exit":
                BeginInvoke(Close);   // passa pelo OnFormClosing (pergunta se sujo)
                return new { ok = true };

            case "app.exitForce":
                _forcarSaida = true;
                BeginInvoke(Close);
                return new { ok = true };

            default:
                throw new InvalidOperationException("método desconhecido: " + method);
        }
    }

    private static async Task<object> LerArquivoAsync(string path)
    {
        var full = Path.GetFullPath(path);
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".md" or ".txt" or ".markdown" or ".text" or ".hyp" or ".html" or ".htm" or ""))
            throw new InvalidOperationException("o Noty só abre .hyp, .md, .txt e .html");

        var fi = new FileInfo(full);
        if (fi.Length > 20 * 1024 * 1024)
            throw new InvalidOperationException("arquivo grande demais (limite 20 MB)");

        var text = await File.ReadAllTextAsync(full, Encoding.UTF8);
        return new { path = full, name = Path.GetFileName(full), text };
    }

    // ---------------------------------------------------------------- settings / tema

    private string SpellLang() =>
        _settings.TryGetPropertyValue("spellLang", out var v) && v is JsonValue jv &&
        jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : "pt-BR";

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonNode.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8)) as JsonObject ?? new JsonObject();
        }
        catch { _settings = new JsonObject(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            File.WriteAllText(SettingsPath,
                _settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private static void GaranteTemaPadrao()
    {
        try
        {
            if (!File.Exists(ThemePath))
                File.WriteAllText(ThemePath, TemaPadrao, new UTF8Encoding(false));
        }
        catch { }
    }

    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;

        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---------------------------------------------------------------- window state

    private void LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                _state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath)) ?? new AppState();
        }
        catch { _state = new AppState(); }

        var bounds = new Rectangle(_state.X, _state.Y, Math.Max(_state.W, MinimumSize.Width),
                                                       Math.Max(_state.H, MinimumSize.Height));

        var visible = _state.X != int.MinValue &&
                      Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds));

        if (visible)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            Size = bounds.Size;
        }

        if (_state.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveState()
    {
        try
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _state.X = b.X;
            _state.Y = b.Y;
            _state.W = b.Width;
            _state.H = b.Height;
            _state.Maximized = WindowState == FormWindowState.Maximized;

            Directory.CreateDirectory(ProfileDir);
            File.WriteAllText(StatePath,
                JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---------------------------------------------------------------- tema padrao

    /// <summary>
    /// Escrito em profile\theme.css na primeira execucao. O usuario edita este
    /// CSS livremente pela aba "Tema" — e o arquivo tambem pode ser mexido por fora.
    /// </summary>
    private const string TemaPadrao = """
        /* ============================================================
           Tema do Noty — edite a vontade, isto e CSS puro.
           Salvou, aplicou. Estragou tudo? Botao "Restaurar padrao".
           ============================================================ */

        :root {
          /* O vidro: este fundo fica POR CIMA do acrilico da janela.
             Quanto mais transparente, mais desktop atravessa.
             Fume escuro, um verde bem sutil, quase incolor. */
          --fundo: rgba(13, 20, 15, 0.35);

          /* Texto do editor */
          --texto: rgba(238, 244, 240, 0.96);
          --texto-suave: rgba(238, 244, 240, 0.45);

          /* Fonte e ritmo (a estetica maquina-de-escrever da tela inicial) */
          --fonte: "Cascadia Mono", Consolas, "JetBrains Mono", monospace;
          --tamanho: 15px;
          --altura-linha: 1.75;
          --largura-texto: 68ch;   /* coluna central; use 100% pra ocupar tudo */

          /* Cursor e selecao */
          --selecao: rgba(126, 214, 158, 0.30);
          --cursor: #8fd6a8;

          /* Menus, paineis e barra de status */
          --painel: rgba(17, 26, 20, 0.92);
          --painel-borda: rgba(255, 255, 255, 0.10);
          --realce: rgba(130, 220, 165, 0.16);

          /* Barra de rolagem (o "polegar" e o hover dele). Pra ir alem das
             cores, sobrescreva os seletores ::-webkit-scrollbar-* aqui. */
          --rolagem: rgba(238, 244, 240, 0.16);
          --rolagem-hover: rgba(143, 214, 168, 0.45);
        }

        /* Os dialogos do app (sair sem salvar, avisos...) tambem obedecem as
           variaveis acima — e podem ser re-estilizados aqui por seletor:
           .dlg-fundo (o embacado), .dlg (a caixa), .dlg-msg (o texto),
           .dlg-btn e .dlg-btn.primario (os botoes). */
        """;
}
