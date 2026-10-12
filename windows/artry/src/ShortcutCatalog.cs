namespace Artry;

internal static class ShortcutCatalog
{
    public static readonly (string Command, string Chord)[] Defaults =
    [
        ("Novo", "Ctrl+N"), ("Abrir", "Ctrl+O"), ("Salvar", "Ctrl+S"),
        ("Salvar como", "Ctrl+Shift+S"), ("Imprimir", "Ctrl+P"),
        ("Localizar", "Ctrl+F"), ("Substituir", "Ctrl+H"), ("Ir para linha", "Ctrl+G"),
        ("Quebra de linha", "Ctrl+W"), ("Régua", "Ctrl+R"), ("Números de linha", "Ctrl+L"),
        ("Gaveta texto", "Alt+1"), ("Gaveta código", "Alt+2"), ("Tema", "Ctrl+Alt+T"),
        ("Tela cheia", "F11"), ("Negrito", "Ctrl+B"), ("Itálico", "Ctrl+I"), ("Sublinhado", "Ctrl+U")
    ];

    public static bool Matches(string? chord, KeyEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(chord)) return false;
        var actual = Format(e.KeyCode, e.Modifiers);
        return string.Equals(chord, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static string Format(Keys key, Keys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key switch { Keys.D1 => "1", Keys.D2 => "2", Keys.D3 => "3", Keys.D4 => "4", Keys.D5 => "5", _ => key.ToString().Replace("Oem", "") });
        return string.Join('+', parts);
    }
}
