using System.Diagnostics;
using System.Drawing;

namespace Artry;

internal sealed class DefaultAppForm : Form
{
    private readonly Settings _settings;
    private readonly Dictionary<string, CheckBox> _checkBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _statusLabel;
    private readonly AppPalette _palette;

    public DefaultAppForm(Settings settings)
    {
        _settings = settings;
        _palette = _settings.Dark ? AppPalette.Dark : AppPalette.Light;

        Text = "Aplicativo Padrão e Formatos — Artry";
        Icon = IconArt.GetAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 720);
        MinimumSize = new Size(560, 560);
        Font = new Font("Segoe UI", 9.25f);
        BackColor = _palette.Background;
        ForeColor = _palette.Text;

        // Top Header
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            Padding = new Padding(22, 16, 22, 12),
            BackColor = _palette.Surface
        };

        var titleLabel = new Label
        {
            Text = "Associação de Arquivos no Windows",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = _palette.Text,
            Dock = DockStyle.Top,
            Height = 26
        };

        var descLabel = new Label
        {
            Text = "Escolha quais formatos de arquivo você deseja associar ao Artry. Ele será registrado no sistema para o usuário atual (HKCU), aparecendo no menu 'Abrir com...', no comando 'artry' (Win+R) e na lista oficial de Aplicativos Padrão do Windows.",
            ForeColor = _palette.Muted,
            Dock = DockStyle.Fill
        };

        header.Controls.Add(descLabel);
        header.Controls.Add(titleLabel);

        // Quick toolbar
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(22, 6, 22, 6),
            BackColor = _palette.Surface
        };

        var btnOnlyText = CreateToolButton("Apenas textos comuns (.artry, .txt, .md, .rtf, .docx, .log)");
        btnOnlyText.Click += (_, _) => SelectSubset(FileAssociationService.TextExtensions);

        var btnAll = CreateToolButton("Selecionar todos");
        btnAll.Click += (_, _) => SelectAll(true);

        var btnNone = CreateToolButton("Desmarcar todos");
        btnNone.Click += (_, _) => SelectAll(false);

        toolbar.Controls.AddRange([btnOnlyText, btnAll, btnNone]);

        // Status Card
        var statusCard = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(22, 12, 22, 10),
            BackColor = _palette.Surface
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Muted
        };
        statusCard.Controls.Add(_statusLabel);

        // Bottom Actions Bar
        var bottomBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(18, 12, 18, 12),
            BackColor = _palette.Background
        };

        var btnClose = CreateActionButton("Fechar", 88);
        btnClose.Click += (_, _) => Close();

        var btnWindowsSettings = CreateActionButton("Abrir Configurações do Windows", 220);
        btnWindowsSettings.Click += (_, _) => FileAssociationService.OpenWindowsDefaultAppsSettings();

        var btnUnregister = CreateActionButton("Remover Registro", 136);
        btnUnregister.Click += (_, _) => Unregister();

        var btnApply = CreateActionButton("Definir como Padrão no Windows", 230, isPrimary: true);
        btnApply.Click += (_, _) => ApplyAssociations();

        bottomBar.Controls.AddRange([btnClose, btnWindowsSettings, btnUnregister, btnApply]);

        // Main Scrollable Panel for Format Groups
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(22, 12, 22, 12)
        };

        var formatList = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1
        };

        var currentRegistered = FileAssociationService.GetRegisteredExtensions();
        var savedExtensions = _settings.AssociatedExtensions?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? currentRegistered;

        // Group 1: Textos e Documentos
        var textItems = new (string Ext, string Desc)[]
        {
            (".artry", "Documento Nativo Artry (.artry)"),
            (".txt", "Arquivo de Texto comum (.txt)"),
            (".md", "Documento Markdown (.md)"),
            (".rtf", "Texto Rico Formatado (.rtf)"),
            (".docx", "Documento Microsoft Word (.docx)"),
            (".log", "Arquivo de Log / Relatório (.log)")
        };
        formatList.Controls.Add(CreateGroup("Documentos e Textos", textItems, savedExtensions));

        // Group 2: Web e Dados
        var dataItems = new (string Ext, string Desc)[]
        {
            (".json", "JSON — JavaScript Object Notation (.json)"),
            (".xml", "XML — Extensible Markup Language (.xml)"),
            (".csv", "CSV — Dados e Planilhas em Texto (.csv)"),
            (".html", "Documento HTML (.html)"),
            (".htm", "Documento HTM (.htm)"),
            (".css", "Folha de Estilos Cascata (.css)")
        };
        formatList.Controls.Add(CreateGroup("Web e Dados Estruturados", dataItems, savedExtensions));

        // Group 3: Código e Scripts
        var codeItems = new (string Ext, string Desc)[]
        {
            (".cs", "Código-fonte C# (.cs)"),
            (".py", "Scripts Python (.py)"),
            (".js", "JavaScript (.js)"),
            (".ts", "TypeScript (.ts)"),
            (".sql", "Scripts de Banco de Dados SQL (.sql)"),
            (".ps1", "Scripts PowerShell (.ps1)"),
            (".sh", "Scripts Shell / Bash (.sh)"),
            (".yml", "Configuração YAML (.yml)"),
            (".yaml", "Configuração YAML (.yaml)")
        };
        formatList.Controls.Add(CreateGroup("Código e Scripts de Programação", codeItems, savedExtensions));

        content.Controls.Add(formatList);

        Controls.Add(content);
        Controls.Add(statusCard);
        Controls.Add(bottomBar);
        Controls.Add(toolbar);
        Controls.Add(header);

        UpdateStatusText();
    }

    private GroupBox CreateGroup(string title, (string Ext, string Desc)[] items, HashSet<string> selected)
    {
        var box = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 14),
            ForeColor = _palette.Accent
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        foreach (var (ext, desc) in items)
        {
            var cb = new CheckBox
            {
                Text = desc,
                Tag = ext,
                Checked = selected.Contains(ext),
                AutoSize = true,
                Margin = new Padding(4, 4, 4, 4),
                ForeColor = _palette.Text
            };
            _checkBoxes[ext] = cb;
            panel.Controls.Add(cb);
        }

        box.Controls.Add(panel);
        return box;
    }

    private Button CreateToolButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = _palette.Background,
            ForeColor = _palette.Text
        };
        btn.FlatAppearance.BorderColor = _palette.Border;
        btn.MouseEnter += (_, _) =>
        {
            btn.BackColor = _palette.AccentSoft;
            btn.ForeColor = _palette.HoverText;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BackColor = _palette.Background;
            btn.ForeColor = _palette.Text;
        };
        return btn;
    }

    private Button CreateActionButton(string text, int width, bool isPrimary = false)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(4, 0, 4, 0),
            BackColor = isPrimary ? _palette.Accent : _palette.Surface,
            ForeColor = isPrimary ? _palette.HoverText : _palette.Text,
            Font = isPrimary ? new Font(Font, FontStyle.Bold) : Font
        };
        btn.FlatAppearance.BorderColor = isPrimary ? _palette.Accent : _palette.Border;
        btn.MouseEnter += (_, _) =>
        {
            btn.BackColor = _palette.AccentSoft;
            btn.ForeColor = _palette.HoverText;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BackColor = isPrimary ? _palette.Accent : _palette.Surface;
            btn.ForeColor = isPrimary ? _palette.HoverText : _palette.Text;
        };
        return btn;
    }

    private void SelectSubset(IEnumerable<string> extensions)
    {
        var set = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (ext, cb) in _checkBoxes)
        {
            cb.Checked = set.Contains(ext);
        }
    }

    private void SelectAll(bool check)
    {
        foreach (var cb in _checkBoxes.Values)
        {
            cb.Checked = check;
        }
    }

    private void UpdateStatusText()
    {
        var isRegistered = FileAssociationService.IsAppRegistered();
        var count = FileAssociationService.GetRegisteredExtensions().Count;
        if (isRegistered)
        {
            _statusLabel.Text = $"Status: Registrado no Windows ({count} formato{(count == 1 ? "" : "s")} com capacidade definida).";
            _statusLabel.ForeColor = _palette.Accent;
        }
        else
        {
            _statusLabel.Text = "Status: O Artry ainda não foi registrado nas configurações do Windows.";
            _statusLabel.ForeColor = _palette.Muted;
        }
    }

    private void ApplyAssociations()
    {
        var selected = _checkBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
        if (selected.Count == 0)
        {
            var confirmEmpty = MessageBox.Show(
                this,
                "Nenhum formato foi selecionado. Deseja remover as associações existentes do Artry no Windows?",
                "Associações",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmEmpty == DialogResult.Yes)
            {
                Unregister();
            }
            return;
        }

        var success = FileAssociationService.RegisterApp(selected);
        if (success)
        {
            _settings.AssociatedExtensions = selected;
            _settings.Save();
            UpdateStatusText();

            var openWinSettings = MessageBox.Show(
                this,
                $"O Artry foi registrado no Windows com sucesso para {selected.Count} formato(s)!\n\n" +
                "Ele agora está disponível no menu 'Abrir com...', no atalho 'artry' (Win+R) e na lista de Aplicativos Padrão.\n\n" +
                "Deseja abrir as Configurações de 'Aplicativos Padrão' do Windows agora para confirmar a preferência no sistema?",
                "Sucesso — Artry",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (openWinSettings == DialogResult.Yes)
            {
                FileAssociationService.OpenWindowsDefaultAppsSettings();
            }
        }
        else
        {
            MessageBox.Show(
                this,
                "Não foi possível salvar o registro de associações no Windows. Verifique se há permissões de escrita em HKCU.",
                "Erro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void Unregister()
    {
        var confirm = MessageBox.Show(
            this,
            "Tem certeza que deseja desregistrar o Artry do Windows? Suas associações de arquivo serão removidas.",
            "Desregistrar",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        var success = FileAssociationService.UnregisterApp();
        if (success)
        {
            _settings.AssociatedExtensions = new();
            _settings.Save();
            SelectAll(false);
            UpdateStatusText();
            MessageBox.Show(this, "O registro do Artry foi removido do Windows com sucesso.", "Artry", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, "Não foi possível remover as entradas do registro.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
