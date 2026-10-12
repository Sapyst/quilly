using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Artry;

internal static class FileAssociationService
{
    public const string AppId = "Artry";
    public const string AppName = "Artry";
    public const string ProgId = "Artry.Document";
    public const string DocDescription = "Documento do Artry";

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_FLUSH = 0x1000;

    public static readonly string[] TextExtensions = [".hyp", ".artry", ".txt", ".md", ".rtf", ".docx", ".log"];
    public static readonly string[] DataWebExtensions = [".json", ".xml", ".csv", ".html", ".htm", ".css"];
    public static readonly string[] CodeExtensions = [".cs", ".py", ".js", ".ts", ".sql", ".ps1", ".sh", ".yml", ".yaml"];

    public static readonly string[] AllSupportedExtensions =
    [
        ..TextExtensions,
        ..DataWebExtensions,
        ..CodeExtensions
    ];

    public static string EnsureHypIconOnDisk(string targetDir)
    {
        var icoPath = Path.Combine(targetDir, "hyp.ico");
        if (!File.Exists(icoPath))
        {
            try
            {
                using var stream = typeof(FileAssociationService).Assembly.GetManifestResourceStream("Artry.hyp.ico");
                if (stream != null)
                {
                    using var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                }
            }
            catch { }
        }
        return icoPath;
    }

    public static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        return Application.ExecutablePath;
    }

    public static bool IsAppRegistered()
    {
        try
        {
            using var regAppsKey = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            if (regAppsKey?.GetValue(AppId) != null) return true;

            using var progIdKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
            if (progIdKey != null) return true;
        }
        catch { }
        return false;
    }

    public static HashSet<string> GetRegisteredExtensions()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var capKey = Registry.CurrentUser.OpenSubKey(@"Software\Artry\Capabilities\FileAssociations");
            if (capKey != null)
            {
                foreach (var name in capKey.GetValueNames())
                {
                    if (capKey.GetValue(name) is string val && val.Equals(ProgId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name);
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public static bool RegisterApp(IEnumerable<string> extensions)
    {
        try
        {
            var exePath = GetExecutablePath();
            var exeDir = Path.GetDirectoryName(exePath) ?? "";
            var exts = extensions
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hypIcon = EnsureHypIconOnDisk(exeDir);
            if (!File.Exists(hypIcon)) hypIcon = $"{exePath},0";

            // 1. App Paths: permite abrir via Win+R digitando "artry"
            using (var appPathsKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Artry.exe"))
            {
                appPathsKey.SetValue("", exePath);
                appPathsKey.SetValue("Path", exeDir);
            }

            // 2. ProgID: HKCU\Software\Classes\Artry.Document
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progIdKey.SetValue("", DocDescription);
                progIdKey.SetValue("FriendlyTypeName", DocDescription);

                using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", $"{exePath},0");

                using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");

                using var openKey = progIdKey.CreateSubKey(@"shell\open");
                openKey.SetValue("FriendlyAppName", AppName);
            }

            // 2.1 ProgID dedicado para .hyp com seu ícone oficial: HKCU\Software\Classes\Hyp.Document
            using (var hypProgIdKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Hyp.Document"))
            {
                hypProgIdKey.SetValue("", "Documento Hyper (.hyp)");
                hypProgIdKey.SetValue("FriendlyTypeName", "Documento Hyper (.hyp)");

                using var iconKey = hypProgIdKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", hypIcon);

                using var cmdKey = hypProgIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");

                using var openKey = hypProgIdKey.CreateSubKey(@"shell\open");
                openKey.SetValue("FriendlyAppName", AppName);
            }

            // 3. Applications: HKCU\Software\Classes\Applications\Artry.exe
            using (var appKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Artry.exe"))
            {
                appKey.SetValue("FriendlyAppName", AppName);

                using var iconKey = appKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", $"{exePath},0");

                using var cmdKey = appKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");

                using var supportedKey = appKey.CreateSubKey("SupportedTypes");
                foreach (var ext in AllSupportedExtensions)
                {
                    supportedKey.SetValue(ext, "");
                }
            }

            // 4. Capabilities e RegisteredApplications (Windows Default Apps)
            using (var capKey = Registry.CurrentUser.CreateSubKey(@"Software\Artry\Capabilities"))
            {
                capKey.SetValue("ApplicationName", AppName);
                capKey.SetValue("ApplicationDescription", "Artry — Editor de texto limpo, rápido e focado.");
                capKey.SetValue("ApplicationIcon", $"{exePath},0");

                using var fileAssocKey = capKey.CreateSubKey("FileAssociations");
                // Remove antigas que foram desmarcadas
                foreach (var oldExt in fileAssocKey.GetValueNames())
                {
                    if (!exts.Contains(oldExt))
                        fileAssocKey.DeleteValue(oldExt, false);
                }
                foreach (var ext in exts)
                {
                    fileAssocKey.SetValue(ext, ext.Equals(".hyp", StringComparison.OrdinalIgnoreCase) ? "Hyp.Document" : ProgId);
                }
            }

            using (var regAppsKey = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                regAppsKey.SetValue(AppId, @"Software\Artry\Capabilities");
            }

            // 5. OpenWithProgids & OpenWithList para cada extensão suportada
            foreach (var ext in AllSupportedExtensions)
            {
                var isSelected = exts.Contains(ext);
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");

                if (isSelected)
                {
                    var isHyp = ext.Equals(".hyp", StringComparison.OrdinalIgnoreCase);
                    var assignedProgId = isHyp ? "Hyp.Document" : ProgId;

                    using var openWithProgids = extKey.CreateSubKey("OpenWithProgids");
                    openWithProgids.SetValue(assignedProgId, Array.Empty<byte>(), RegistryValueKind.None);
                    if (isHyp) openWithProgids.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

                    using var openWithList = extKey.CreateSubKey(@"OpenWithList\Artry.exe");

                    if (isHyp)
                    {
                        using var defIcon = extKey.CreateSubKey("DefaultIcon");
                        defIcon.SetValue("", hypIcon);
                    }

                    // No Windows sem UserChoice ativo para esta extensão, define imediatamente
                    var currentDefault = extKey.GetValue("") as string;
                    if (string.IsNullOrEmpty(currentDefault) || currentDefault == ProgId || currentDefault == "Hyp.Document")
                    {
                        extKey.SetValue("", assignedProgId);
                    }
                }
                else
                {
                    using var openWithProgids = extKey.OpenSubKey("OpenWithProgids", true);
                    openWithProgids?.DeleteValue(ProgId, false);
                    openWithProgids?.DeleteValue("Hyp.Document", false);
                }
            }

            // 6. Notifica o Windows Shell em tempo real
            NotifyShell();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao registrar no Windows: {ex}");
            return false;
        }
    }

    public static bool UnregisterApp()
    {
        try
        {
            using (var regAppsKey = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            {
                regAppsKey?.DeleteValue(AppId, false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Artry", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\Artry.exe", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Artry.exe", false);

            foreach (var ext in AllSupportedExtensions)
            {
                using var owpKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                owpKey?.DeleteValue(ProgId, false);
            }

            NotifyShell();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao remover registro no Windows: {ex}");
            return false;
        }
    }

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    public static bool OpenWindowsDefaultAppsSettings()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void CreateShortcut(string targetExePath, string shortcutPath, string description)
    {
        try
        {
            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetExePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
                    shortcut.Description = description;
                    shortcut.IconLocation = $"{targetExePath},0";
                    shortcut.Save();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao criar atalho: {ex}");
        }
    }

    public static void RegisterUninstallEntry(string installDir, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Artry");
            var ver = typeof(FileAssociationService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            key.SetValue("DisplayName", "Artry");
            key.SetValue("DisplayVersion", ver);
            key.SetValue("Publisher", "Artry");
            key.SetValue("DisplayIcon", $"{exePath},0");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao registrar desinstalação: {ex}");
        }
    }

    public static void RemoveShortcutsAndUninstallEntry()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var desktopLnk = Path.Combine(desktop, "Artry.lnk");
            if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            var startLnk = Path.Combine(startMenu, "Artry.lnk");
            if (File.Exists(startLnk)) File.Delete(startLnk);

            using var uninstallKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true);
            uninstallKey?.DeleteSubKeyTree("Artry", false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao remover atalhos/desinstalação: {ex}");
        }
    }
}
