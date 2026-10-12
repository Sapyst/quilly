using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quilly;

public sealed class FileItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";
    public string SizeFormatted { get; set; } = "";
    public string ModifiedFormatted { get; set; } = "";
    public string RecommendedApp { get; set; } = "";
    public string RecommendedAppId { get; set; } = "";
}

public sealed class FileManager
{
    private readonly string _rootPath;
    private readonly string _profileDir;
    private readonly string _historyFile;
    private readonly string _notySettingsFile;
    private readonly AppManager _appManager;

    public FileManager(string rootPath, AppManager appManager)
    {
        _rootPath = rootPath;
        _appManager = appManager;
        _profileDir = Path.Combine(rootPath, "profile");
        _historyFile = Path.Combine(_profileDir, "recent_files.json");
        _notySettingsFile = Path.Combine(rootPath, "..", "Noty", "profile", "settings.json");

        Directory.CreateDirectory(_profileDir);
    }

    public List<FileItem> GetRecentFiles()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Arquivos no histórico do Quilly
        if (File.Exists(_historyFile))
        {
            try
            {
                var json = File.ReadAllText(_historyFile);
                var arr = JsonSerializer.Deserialize<List<string>>(json);
                if (arr != null)
                {
                    foreach (var p in arr) if (!string.IsNullOrWhiteSpace(p)) paths.Add(p);
                }
            }
            catch { }
        }

        // 2. Histórico do Noty (standalone ou instalado na Suite)
        var notySettingsFiles = new[]
        {
            _notySettingsFile,
            Path.Combine(_appManager.SuitePath, "Noty", "profile", "settings.json")
        };
        foreach (var nFile in notySettingsFiles)
        {
            if (File.Exists(nFile))
            {
                try
                {
                    var json = File.ReadAllText(nFile);
                    var doc = JsonNode.Parse(json);
                    var recentNode = doc?["ui"]?["recent"]?.AsArray();
                    if (recentNode != null)
                    {
                        foreach (var item in recentNode)
                        {
                            var p = item?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(p)) paths.Add(p);
                        }
                    }
                }
                catch { }
            }
        }

        // 3. Histórico do Artry (standalone ou instalado na Suite)
        var artryConfigFiles = new[]
        {
            Path.Combine(_rootPath, "..", "Artry", "dados", "config.json"),
            Path.Combine(_appManager.SuitePath, "Artry", "dados", "config.json")
        };
        foreach (var aFile in artryConfigFiles)
        {
            if (File.Exists(aFile))
            {
                try
                {
                    var json = File.ReadAllText(aFile);
                    var doc = JsonNode.Parse(json);
                    var recentNode = doc?["RecentFiles"]?.AsArray();
                    if (recentNode != null)
                    {
                        foreach (var item in recentNode)
                        {
                            var p = item?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(p)) paths.Add(p);
                        }
                    }
                }
                catch { }
            }
        }

        // 4. Documentos padrão na pasta Padroes se existirem
        var padroesDir = Path.GetFullPath(Path.Combine(_profileDir, "..", "..", "..", "Padroes"));
        if (Directory.Exists(padroesDir))
        {
            foreach (var f in Directory.GetFiles(padroesDir, "*.hyp")) paths.Add(f);
        }

        var result = new List<FileItem>();
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var fi = new FileInfo(p);
                var ext = fi.Extension.ToLowerInvariant();
                var (recApp, recId) = ext switch
                {
                    ".hyp" => ("Noty / Artry", "noty"),
                    ".artry" or ".docx" or ".rtf" => ("Artry", "artry"),
                    ".md" or ".txt" or ".html" => ("Noty", "noty"),
                    _ => ("Padrão do Sistema", "")
                };

                result.Add(new FileItem
                {
                    Path = fi.FullName,
                    Name = fi.Name,
                    Extension = ext,
                    SizeFormatted = FormatFileSize(fi.Length),
                    ModifiedFormatted = fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                    RecommendedApp = recApp,
                    RecommendedAppId = recId
                });
            }
            catch { }
        }

        // Ordena pelos mais recentes
        return result.OrderByDescending(f => File.GetLastWriteTime(f.Path)).Take(30).ToList();
    }

    public void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(_historyFile))
        {
            try
            {
                var json = File.ReadAllText(_historyFile);
                var arr = JsonSerializer.Deserialize<List<string>>(json);
                if (arr != null) foreach (var p in arr) existing.Add(p);
            }
            catch { }
        }

        existing.Add(path);
        try
        {
            var list = existing.Where(File.Exists).Take(50).ToList();
            File.WriteAllText(_historyFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void RemoveRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(_historyFile)) return;
        try
        {
            var json = File.ReadAllText(_historyFile);
            var arr = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            arr.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(_historyFile, JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void OpenWith(string filePath, string appId)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Arquivo não encontrado.");
        AddRecentFile(filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (string.Equals(appId, "noty", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(appId, "artry", StringComparison.OrdinalIgnoreCase))
        {
            _appManager.LaunchApp(appId, filePath);
            return;
        }

        // Se nenhum app específico foi passado e o formato for .hyp:
        if (ext == ".hyp")
        {
            var apps = _appManager.GetApps();
            var noty = apps.FirstOrDefault(a => a.Id == "noty");
            var artry = apps.FirstOrDefault(a => a.Id == "artry");

            if (noty?.Installed == true)
            {
                _appManager.LaunchApp("noty", filePath);
                return;
            }
            if (artry?.Installed == true)
            {
                _appManager.LaunchApp("artry", filePath);
                return;
            }
            if (File.Exists(noty?.SourcePath))
            {
                _appManager.LaunchApp("noty", filePath);
                return;
            }
            if (File.Exists(artry?.SourcePath))
            {
                _appManager.LaunchApp("artry", filePath);
                return;
            }
        }

        // Padrão do sistema
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch { }
    }

    public void ShowInExplorer(string filePath)
    {
        if (!File.Exists(filePath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
