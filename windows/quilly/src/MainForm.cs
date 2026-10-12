using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Quilly;

public sealed class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string ProfileDir = Path.Combine(AppDir, "profile");
    private static readonly string SettingsFile = Path.Combine(ProfileDir, "settings.json");

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(20, 23, 26) };
    private readonly AppManager _appManager;
    private readonly FileManager _fileManager;
    private readonly string? _initialFile;
    private JsonObject _settings = new();

    private CoreWebView2Environment? _env;

    public MainForm(string? initialFile = null)
    {
        _initialFile = initialFile;
        Text = "Quilly Suite";
        MinimumSize = new Size(960, 620);
        ClientSize = new Size(1120, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 23, 26);

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Quilly.icon.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        Controls.Add(_web);

        LoadSettings();
        var customSuite = _settings["suitePath"]?.GetValue<string>();
        _appManager = new AppManager(AppDir, customSuite);
        _fileManager = new FileManager(AppDir, _appManager);
    }

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

            // Cor da barra de titulo: #14171A (0x001A1714 em formato COLORREF 0x00BBGGRR)
            int captionColor = 0x001A1714;
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            // Cor do texto do titulo: #F2F5F8 (0x00F8F5F2)
            int textColor = 0x00F8F5F2;
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

            // Cor da borda da janela: #2D333D (0x003D332D)
            int borderColor = 0x003D332D;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { /* Fallback limpo caso o Windows nao suporte */ }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AplicaTemaBarraTitulo();

        var webDataDir = Path.Combine(ProfileDir, "web");
        Directory.CreateDirectory(webDataDir);

        _env = await CoreWebView2Environment.CreateAsync(null, webDataDir);
        await _web.EnsureCoreWebView2Async(_env);

        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = true;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;

        // Procura a pasta ui local em desenvolvimento ou na pasta ao lado
        var uiPath = Path.Combine(AppDir, "src", "ui");
        if (!Directory.Exists(uiPath)) uiPath = Path.Combine(AppDir, "ui");
        if (!Directory.Exists(uiPath)) uiPath = Path.GetFullPath(Path.Combine(AppDir, "..", "src", "ui"));

        core.SetVirtualHostNameToFolderMapping("quilly.suite", uiPath, CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate("https://quilly.suite/index.html");

        if (!string.IsNullOrEmpty(_initialFile) && File.Exists(_initialFile))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                Invoke(() => _fileManager.OpenWith(_initialFile, ""));
            });
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            msg = doc.RootElement.Clone();
        }
        catch { return; }

        var type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        if (type == "rpc")
        {
            var id = msg.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;
            var method = msg.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            var args = msg.TryGetProperty("args", out var a) ? a : default;

            try
            {
                var result = await DispatchRpcAsync(method, args);
                ReplyRpc(id, true, result, null);
            }
            catch (Exception ex)
            {
                ReplyRpc(id, false, null, ex.Message);
            }
        }
    }

    private void ReplyRpc(int id, bool ok, object? data, string? error)
    {
        PostToJs(new
        {
            type = "rpc-reply",
            id,
            ok,
            data,
            error
        });
    }

    private void PostToJs(object payload)
    {
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload)); }
        catch { }
    }

    private async Task<object?> DispatchRpcAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "apps.list":
                return _appManager.GetApps();

            case "apps.install":
            {
                var appId = args.GetProperty("appId").GetString() ?? "";
                var success = await _appManager.InstallOrUpdateAppAsync(appId, (percent, status) =>
                {
                    PostToJs(new
                    {
                        type = "install_progress",
                        appId,
                        percent,
                        status
                    });
                });
                return new { success, apps = _appManager.GetApps() };
            }

            case "apps.uninstall":
            {
                var appId = args.GetProperty("appId").GetString() ?? "";
                var success = _appManager.UninstallApp(appId);
                return new { success, apps = _appManager.GetApps() };
            }

            case "apps.launch":
            {
                var appId = args.GetProperty("appId").GetString() ?? "";
                var filePath = args.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
                var success = _appManager.LaunchApp(appId, filePath);
                return new { success };
            }

            case "apps.openFolder":
            {
                var appId = args.GetProperty("appId").GetString() ?? "";
                _appManager.OpenAppFolder(appId);
                return new { success = true };
            }

            case "apps.createShortcut":
            {
                var appId = args.GetProperty("appId").GetString() ?? "";
                _appManager.CreateDesktopShortcut(appId);
                return new { success = true };
            }

            case "files.list":
                return _fileManager.GetRecentFiles();

            case "files.open":
            {
                var path = args.GetProperty("path").GetString() ?? "";
                var appId = args.TryGetProperty("appId", out var a) ? a.GetString() ?? "" : "";
                _fileManager.OpenWith(path, appId);
                return new { success = true };
            }

            case "files.showInExplorer":
            {
                var path = args.GetProperty("path").GetString() ?? "";
                _fileManager.ShowInExplorer(path);
                return new { success = true };
            }

            case "files.remove":
            {
                var path = args.GetProperty("path").GetString() ?? "";
                _fileManager.RemoveRecentFile(path);
                return new { success = true, files = _fileManager.GetRecentFiles() };
            }

            case "suite.getFolder":
                return new { path = _appManager.SuitePath };

            case "suite.openFolder":
                Process.Start(new ProcessStartInfo("explorer.exe", _appManager.SuitePath) { UseShellExecute = true });
                return new { success = true };

            case "suite.chooseFolder":
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Escolha onde instalar os aplicativos da Quilly Suite",
                    InitialDirectory = _appManager.SuitePath,
                    UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    _appManager.SuitePath = dlg.SelectedPath;
                    _settings["suitePath"] = dlg.SelectedPath;
                    SaveSettings();
                    return new { path = dlg.SelectedPath };
                }
                return null;
            }

            case "system.devtools":
                _web.CoreWebView2.OpenDevToolsWindow();
                return new { ok = true };

            default:
                throw new InvalidOperationException("Método não implementado: " + method);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _settings = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
        }
        catch { _settings = new JsonObject(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            File.WriteAllText(SettingsFile, _settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
