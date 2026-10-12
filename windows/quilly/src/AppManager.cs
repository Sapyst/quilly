using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Quilly;

public sealed class AppInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string[] Formats { get; set; } = [];
    public bool Installed { get; set; }
    public bool UpdateAvailable { get; set; }
    public string InstalledPath { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public long InstalledSize { get; set; }
    public long SourceSize { get; set; }
    public string? InstalledDate { get; set; }
    public string? SourceDate { get; set; }
    public string InstalledVersion { get; set; } = "";
    public string SourceVersion { get; set; } = "";
}

public sealed class AppManager
{
    private readonly string _rootPath;
    private string _suitePath;
    private readonly string _sourcePath;

    public AppManager(string rootPath, string? customSuitePath = null)
    {
        _rootPath = rootPath;
        _suitePath = !string.IsNullOrWhiteSpace(customSuitePath) && Directory.Exists(customSuitePath)
            ? customSuitePath
            : Path.Combine(_rootPath, "Suite");
        _sourcePath = Path.Combine(_rootPath, "source_apps");

        Directory.CreateDirectory(_suitePath);
        Directory.CreateDirectory(_sourcePath);
    }

    public string SuitePath
    {
        get => _suitePath;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Directory.CreateDirectory(value);
                _suitePath = value;
            }
        }
    }

    public List<AppInfo> GetApps()
    {
        var apps = new List<AppInfo>
        {
            new()
            {
                Id = "artry",
                Name = "Artry",
                Subtitle = "Estúdio de texto elegante e documentos estruturados",
                Description = "Editor profissional com tipografia de alta precisão, suporte completo a .hyp, .artry, .docx, .rtf e código.",
                Icon = "assets/artry.png",
                Formats = [".hyp", ".artry", ".docx", ".rtf", ".txt"],
            },
            new()
            {
                Id = "noty",
                Name = "Noty",
                Subtitle = "Bloco de notas ágil em acrílico on-the-go",
                Description = "Escrita ultrarrápida, design em acrílico translúcido do Windows 11, Markdown ao vivo e suporte ao Hyper Doc (.hyp).",
                Icon = "assets/noty.png",
                Formats = [".hyp", ".md", ".txt", ".html"],
            },
            new()
            {
                Id = "volury",
                Name = "Volury",
                Subtitle = "Editor de e-books (EPUB) elegante e poderoso",
                Description = "Crie, edite e valide livros digitais com uma interface WYSIWYG focada na experiência de escrita e tipografia.",
                Icon = "assets/volury.png",
                Formats = [".epub", ".hyp"],
            }
        };

        foreach (var app in apps)
        {
            var exeName = app.Id switch
            {
                "artry" => "Artry.exe",
                "noty" => "Noty.exe",
                "volury" => "Volury.exe",
                _ => throw new InvalidOperationException()
            };
            var sourceExe = Path.Combine(_sourcePath, app.Name, exeName);
            var installedExe = Path.Combine(_suitePath, app.Name, exeName);

            app.SourcePath = sourceExe;
            app.InstalledPath = installedExe;

            if (File.Exists(sourceExe))
            {
                var sfi = new FileInfo(sourceExe);
                app.SourceSize = sfi.Length;
                app.SourceDate = sfi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                try
                {
                    var sfv = FileVersionInfo.GetVersionInfo(sourceExe);
                    app.SourceVersion = sfv.FileVersion ?? "";
                }
                catch { }
            }

            if (File.Exists(installedExe))
            {
                app.Installed = true;
                var ifi = new FileInfo(installedExe);
                app.InstalledSize = ifi.Length;
                app.InstalledDate = ifi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                try
                {
                    var ifv = FileVersionInfo.GetVersionInfo(installedExe);
                    app.InstalledVersion = ifv.FileVersion ?? "";
                }
                catch { }

                // Verifica se há atualização disponível
                if (File.Exists(sourceExe))
                {
                    // 1. Verificação primária por micro-versão de binário (FileVersionInfo)
                    if (!string.IsNullOrEmpty(app.SourceVersion) && !string.IsNullOrEmpty(app.InstalledVersion))
                    {
                        if (Version.TryParse(app.SourceVersion, out var sv) &&
                            Version.TryParse(app.InstalledVersion, out var iv))
                        {
                            if (sv > iv)
                            {
                                app.UpdateAvailable = true;
                            }
                        }
                    }

                    // 2. Verificação complementar por data mais recente ou alteração de tamanho
                    var sourceTime = File.GetLastWriteTimeUtc(sourceExe);
                    var installedTime = File.GetLastWriteTimeUtc(installedExe);
                    var sfi = new FileInfo(sourceExe);
                    var ifi2 = new FileInfo(installedExe);

                    if (sourceTime > installedTime.AddSeconds(2) || sfi.Length != ifi2.Length)
                    {
                        app.UpdateAvailable = true;
                    }
                }
            }
            else
            {
                app.Installed = false;
                app.UpdateAvailable = false;
            }
        }

        return apps;
    }

    public async Task<bool> InstallOrUpdateAppAsync(string appId, Action<int, string>? onProgress = null)
    {
        var exeName = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry.exe",
            "noty" => "Noty.exe",
            "volury" => "Volury.exe",
            _ => throw new ArgumentException("App desconhecido")
        };
        var appFolder = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry",
            "noty" => "Noty",
            "volury" => "Volury",
            _ => throw new ArgumentException("App desconhecido")
        };

        var sourceFolder = Path.Combine(_sourcePath, appFolder);
        var sourceExe = Path.Combine(sourceFolder, exeName);

        if (!File.Exists(sourceExe))
            throw new FileNotFoundException($"O pacote de origem de {appFolder} não foi encontrado em {sourceExe}");

        var targetFolder = Path.Combine(_suitePath, appFolder);
        var targetExe = Path.Combine(targetFolder, exeName);

        Directory.CreateDirectory(targetFolder);

        // Se o aplicativo estiver aberto, aguarda ou avisa
        var procName = Path.GetFileNameWithoutExtension(exeName);
        var running = Process.GetProcessesByName(procName);
        foreach (var p in running)
        {
            try
            {
                if (p.MainModule?.FileName != null &&
                    string.Equals(p.MainModule.FileName, targetExe, StringComparison.OrdinalIgnoreCase))
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000)) p.Kill();
                }
            }
            catch { }
        }

        onProgress?.Invoke(10, "Iniciando preparação de arquivos…");
        await Task.Delay(150);

        // Copia com stream assíncrono para reportar progresso
        var fi = new FileInfo(sourceExe);
        var totalBytes = fi.Length;
        var copiedBytes = 0L;

        var tempTarget = targetExe + ".tmp";

        using (var sourceStream = new FileStream(sourceExe, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
        using (var destStream = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destStream.WriteAsync(buffer, 0, read);
                copiedBytes += read;
                var pct = (int)((copiedBytes * 80.0) / totalBytes) + 10;
                onProgress?.Invoke(Math.Min(90, pct), $"Copiando binários… ({copiedBytes / (1024 * 1024)} MB de {totalBytes / (1024 * 1024)} MB)");
            }
        }

        File.Move(tempTarget, targetExe, true);
        File.SetLastWriteTimeUtc(targetExe, File.GetLastWriteTimeUtc(sourceExe));

        // Copia hyp.ico se disponível no pacote de origem
        var sourceHypIco = Path.Combine(sourceFolder, "hyp.ico");
        var targetHypIco = Path.Combine(targetFolder, "hyp.ico");
        if (File.Exists(sourceHypIco))
        {
            File.Copy(sourceHypIco, targetHypIco, true);
        }
        else
        {
            // Tenta achar em source_apps ou raiz do Quilly
            var rootHypIco = Path.Combine(_rootPath, "hyp.ico");
            if (File.Exists(rootHypIco)) File.Copy(rootHypIco, targetHypIco, true);
        }

        // Registra associação no sistema para .hyp
        RegisterHypAssociation(targetExe, File.Exists(targetHypIco) ? targetHypIco : targetExe);

        onProgress?.Invoke(95, "Finalizando configuração e registrando na suite…");
        await Task.Delay(200);

        onProgress?.Invoke(100, "Pronto! Aplicativo configurado com sucesso.");
        return true;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void RegisterHypAssociation(string targetExe, string iconPath)
    {
        try
        {
            var appName = Path.GetFileNameWithoutExtension(targetExe);

            using (var progIdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Hyp.Document"))
            {
                progIdKey.SetValue("", "Documento Hyper (.hyp)");
                progIdKey.SetValue("FriendlyTypeName", "Documento Hyper (.hyp)");

                if (File.Exists(iconPath))
                {
                    using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                    iconKey.SetValue("", iconPath);
                }

                using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{targetExe}\" \"%1\"");

                using var openKey = progIdKey.CreateSubKey(@"shell\open");
                openKey.SetValue("FriendlyAppName", appName);
            }

            using (var extKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\.hyp"))
            {
                if (File.Exists(iconPath))
                {
                    using var defIconKey = extKey.CreateSubKey("DefaultIcon");
                    defIconKey.SetValue("", iconPath);
                }

                using var openWith = extKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue("Hyp.Document", Array.Empty<byte>(), Microsoft.Win32.RegistryValueKind.None);

                using var openWithList = extKey.CreateSubKey($@"OpenWithList\{Path.GetFileName(targetExe)}");

                var current = extKey.GetValue("") as string;
                if (string.IsNullOrEmpty(current) || current == "Hyp.Document")
                {
                    extKey.SetValue("", "Hyp.Document");
                }
            }

            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    public bool UninstallApp(string appId)
    {
        var appFolder = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry",
            "noty" => "Noty",
            "volury" => "Volury",
            _ => throw new ArgumentException("App desconhecido")
        };
        var targetFolder = Path.Combine(_suitePath, appFolder);

        if (Directory.Exists(targetFolder))
        {
            Directory.Delete(targetFolder, true);
            return true;
        }

        return false;
    }

    public bool LaunchApp(string appId, string? filePath = null)
    {
        var exeName = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry.exe",
            "noty" => "Noty.exe",
            "volury" => "Volury.exe",
            _ => throw new ArgumentException("App desconhecido")
        };
        var appFolder = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry",
            "noty" => "Noty",
            "volury" => "Volury",
            _ => throw new ArgumentException("App desconhecido")
        };
        var targetExe = Path.Combine(_suitePath, appFolder, exeName);

        if (!File.Exists(targetExe))
        {
            // Fallback se estiver no source
            var sourceExe = Path.Combine(_sourcePath, appFolder, exeName);
            if (File.Exists(sourceExe)) targetExe = sourceExe;
            else throw new FileNotFoundException($"O aplicativo {appFolder} ainda não foi instalado.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = targetExe,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(targetExe)
        };

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            psi.ArgumentList.Add(filePath);
        }

        Process.Start(psi);
        return true;
    }

    public void OpenAppFolder(string appId)
    {
        var appFolder = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry",
            "noty" => "Noty",
            "volury" => "Volury",
            _ => throw new ArgumentException("App desconhecido")
        };
        var targetFolder = Path.Combine(_suitePath, appFolder);

        if (!Directory.Exists(targetFolder)) targetFolder = _suitePath;
        Process.Start(new ProcessStartInfo("explorer.exe", targetFolder) { UseShellExecute = true });
    }

    public void CreateDesktopShortcut(string appId)
    {
        var exeName = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry.exe",
            "noty" => "Noty.exe",
            "volury" => "Volury.exe",
            _ => throw new ArgumentException("App desconhecido")
        };
        var appFolder = appId.ToLowerInvariant() switch
        {
            "artry" => "Artry",
            "noty" => "Noty",
            "volury" => "Volury",
            _ => throw new ArgumentException("App desconhecido")
        };
        var targetExe = Path.Combine(_suitePath, appFolder, exeName);

        if (!File.Exists(targetExe)) throw new FileNotFoundException("O aplicativo precisa ser instalado primeiro.");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, $"{appFolder}.lnk");

        var psCmd = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetExe}'; $s.WorkingDirectory = '{Path.GetDirectoryName(targetExe)}'; $s.Save()";

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{psCmd}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        })?.WaitForExit();
    }
}
