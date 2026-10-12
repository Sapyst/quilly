namespace Artry;

internal sealed class ShortcutSettingsForm : Form
{
    private readonly Settings _settings;
    private readonly Dictionary<string, ShortcutBox> _boxes = [];

    public ShortcutSettingsForm(Settings settings)
    {
        _settings = settings;
        var p = _settings.Dark ? AppPalette.Dark : AppPalette.Light;
        Text = "Atalhos — Artry";
        Icon = IconArt.GetAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(570, 620);
        MinimumSize = new Size(500, 440);
        BackColor = p.Background;
        ForeColor = p.Text;
        var intro = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(18, 14, 18, 0), Text = "Clique em um campo e aperte até três teclas: Ctrl + Shift + S, por exemplo.", ForeColor = p.Muted };
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18, 8, 18, 12), ColumnCount = 2 };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var row = 0; row < ShortcutCatalog.Defaults.Length; row++)
        {
            var (command, defaultChord) = ShortcutCatalog.Defaults[row];
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            list.Controls.Add(new Label { Text = command, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            var box = new ShortcutBox { Dock = DockStyle.Fill, Text = _settings.Shortcuts.GetValueOrDefault(command, defaultChord), BackColor = p.Paper, ForeColor = p.Text };
            _boxes[command] = box; list.Controls.Add(box, 1, row);
        }
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        var save = new Button { Text = "Salvar", Width = 92, BackColor = p.Accent, ForeColor = p.HoverText, FlatStyle = FlatStyle.Flat };
        save.FlatAppearance.BorderColor = p.Accent;
        save.Click += (_, _) => { if (Save()) DialogResult = DialogResult.OK; };
        var reset = new Button { Text = "Restaurar padrões", Width = 142, BackColor = p.Surface, ForeColor = p.Text, FlatStyle = FlatStyle.Flat };
        reset.FlatAppearance.BorderColor = p.Border;
        reset.Click += (_, _) => { foreach (var (command, chord) in ShortcutCatalog.Defaults) _boxes[command].Text = chord; };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 92, BackColor = p.Surface, ForeColor = p.Text, FlatStyle = FlatStyle.Flat };
        cancel.FlatAppearance.BorderColor = p.Border;
        bottom.Controls.Add(save); bottom.Controls.Add(cancel); bottom.Controls.Add(reset);
        Controls.Add(list); Controls.Add(intro); Controls.Add(bottom); AcceptButton = save;
    }

    private bool Save()
    {
        var duplicates = _boxes.GroupBy(x => x.Value.Text, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1);
        if (duplicates is not null) { MessageBox.Show(this, $"O atalho {duplicates.Key} está repetido. Cada comando precisa ter uma combinação própria.", "Atalhos", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        foreach (var (command, box) in _boxes) _settings.Shortcuts[command] = box.Text;
        _settings.Save();
        return true;
    }
}

internal sealed class ShortcutBox : TextBox
{
    public ShortcutBox() { ReadOnly = true; ShortcutsEnabled = false; BorderStyle = BorderStyle.FixedSingle; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) { e.SuppressKeyPress = true; return; }
        var modifiers = e.Modifiers;
        var modifierCount = (modifiers.HasFlag(Keys.Control) ? 1 : 0) + (modifiers.HasFlag(Keys.Alt) ? 1 : 0) + (modifiers.HasFlag(Keys.Shift) ? 1 : 0);
        if (modifierCount > 2) { MessageBox.Show(FindForm(), "Use no máximo três teclas simultâneas.", "Atalhos"); e.SuppressKeyPress = true; return; }
        Text = ShortcutCatalog.Format(e.KeyCode, modifiers);
        e.SuppressKeyPress = true;
    }
}
