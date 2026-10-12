using System.Text.Json;

namespace Artry;

internal sealed class Settings
{
    public string FontName { get; set; } = "Consolas";
    public float FontSize { get; set; } = 12;
    public bool WordWrap { get; set; } = true;
    public bool Dark { get; set; } = true;
    public bool ShowLines { get; set; } = true;
    public bool ShowRuler { get; set; } = true;
    public bool PageViewMode { get; set; } = true;
    public Dictionary<string, string> Shortcuts { get; set; } = new();
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 780;
    public List<string> AssociatedExtensions { get; set; } = new() { ".artry", ".txt", ".md", ".rtf", ".docx", ".log" };
    public List<string> RecentFiles { get; set; } = new();

    public void AddRecentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        RecentFiles ??= new();
        RecentFiles.RemoveAll(p => p.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, filePath);
        if (RecentFiles.Count > 16) RecentFiles.RemoveRange(16, RecentFiles.Count - 16);
        Save();
    }

    public void RemoveRecentFile(string filePath)
    {
        if (RecentFiles == null) return;
        RecentFiles.RemoveAll(p => p.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "dados", "config.json");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new();
            settings.EnsureDefaults();
            return settings;
        }
        catch { var settings = new Settings(); settings.EnsureDefaults(); return settings; }
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void EnsureDefaults()
    {
        EnsureShortcuts();
        AssociatedExtensions ??= new() { ".txt", ".md", ".rtf", ".log" };
    }

    public void EnsureShortcuts()
    {
        Shortcuts ??= new();
        foreach (var (command, chord) in ShortcutCatalog.Defaults)
            if (!Shortcuts.ContainsKey(command)) Shortcuts[command] = chord;
    }
}
