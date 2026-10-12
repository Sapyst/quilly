using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Text;

namespace Artry;

internal sealed class WorkPanel : Panel
{
    public WorkPanel()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        Padding = Padding.Empty;
    }

    protected override Point ScrollToControl(Control activeControl)
    {
        // Impede que o WinForms role o container automaticamente quando um controle filho ganha foco,
        // evitando que a folha salte para cima e seja cortada ou comida pelas barras superiores.
        return AutoScrollPosition;
    }
}

internal sealed class MainForm : Form
{
    private readonly Settings _settings = Settings.Load();
    private readonly RichTextBox _editor = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        AcceptsTab = true,
        HideSelection = false,
        DetectUrls = true,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };

    private readonly Panel _gutter = new() { Dock = DockStyle.Left, Width = 48, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly RulerCorner _corner;
    private readonly Ruler _horizontalRuler;
    private readonly Ruler _verticalRuler;
    private readonly Panel _rulerTopPanel;
    private readonly Panel _mainContent;
    private readonly Panel _bodyPanel;
    private readonly Panel _dragGuideLine;
    private readonly MenuStrip _menu;
    private readonly ToolStrip _docToolbar;
    private readonly ToolStrip _codeToolbar;
    private readonly StatusStrip _statusBar;
    private readonly WorkPanel _work;
    private readonly Panel _paper;

    private readonly PrintDocument _printer = new();
    private string? _path;
    private bool _dirty, _highlighting, _fullscreen, _codeMode, _updatingFormatUI;
    private int _printPosition;

    private Color _currentTextColor;
    private Color _currentHighlightColor = Color.Transparent;

    // Controles da barra estilo Google Docs
    private ToolStripComboBox _cboStyles = null!;
    private ToolStripComboBox _cboFont = null!;
    private ToolStripTextBox _txtFontSize = null!;
    private ToolStripButton _btnSizeDown = null!;
    private ToolStripButton _btnSizeUp = null!;
    private ToolStripButton _btnBold = null!;
    private ToolStripButton _btnItalic = null!;
    private ToolStripButton _btnUnderline = null!;
    private ToolStripButton _btnStrike = null!;
    private ToolStripButton _btnTextColor = null!;
    private ToolStripButton _btnHighlight = null!;
    private ToolStripButton _btnAlignLeft = null!;
    private ToolStripButton _btnAlignCenter = null!;
    private ToolStripButton _btnAlignRight = null!;
    private ToolStripButton _btnAlignJustify = null!;
    private ToolStripDropDownButton _btnLineSpacing = null!;
    private ToolStripButton _btnBullet = null!;
    private ToolStripButton _btnNumbered = null!;
    private ToolStripButton _btnIndentLess = null!;
    private ToolStripButton _btnIndentMore = null!;
    private ToolStripButton _btnClearFormat = null!;
    private ToolStripComboBox _cboZoom = null!;
    private ToolStripButton _btnPageMode = null!;
    private ToolStripButton _btnRuler = null!;
    private ToolStripButton _btnHome = null!;
    private readonly HomeScreen _homeScreen;

    public MainForm(string? initialFile = null)
    {
        Text = "Artry";
        Icon = IconArt.GetAppIcon();
        StartPosition = FormStartPosition.Manual;
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        if (_settings.WindowX >= 0) Location = new Point(_settings.WindowX, _settings.WindowY);
        MinimumSize = new Size(760, 480);
        KeyPreview = true;

        _currentTextColor = _settings.Dark ? AppPalette.Dark.Text : AppPalette.Light.Text;

        _menu = BuildMenu();
        _docToolbar = BuildDocToolbar();
        _codeToolbar = BuildCodeToolbar();

        _horizontalRuler = new Ruler(_editor, RulerAxis.Horizontal);
        _verticalRuler = new Ruler(_editor, RulerAxis.Vertical);
        _corner = new RulerCorner { HorizontalRuler = _horizontalRuler };

        _rulerTopPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = _settings.ShowRuler
        };
        _rulerTopPanel.Controls.Add(_horizontalRuler);
        _rulerTopPanel.Controls.Add(_corner);
        _corner.SendToBack();

        _work = new WorkPanel();

        _paper = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle
        };

        _horizontalRuler.Paper = _paper;
        _verticalRuler.Paper = _paper;

        // Na folha de documento: APENAS o editor e os números de linha!
        // As réguas ficam FORA do documento.
        _paper.Controls.Add(_editor);
        _paper.Controls.Add(_gutter);
        _gutter.SendToBack();
        _work.Controls.Add(_paper);

        _dragGuideLine = new Panel
        {
            Width = 1,
            Visible = false,
            BackColor = Color.FromArgb(83, 117, 102)
        };
        _work.Controls.Add(_dragGuideLine);
        _dragGuideLine.BringToFront();

        _horizontalRuler.OnGuideLineChanged = (screenX, color) =>
        {
            if (screenX.HasValue)
            {
                var workPt = _work.PointToClient(new Point(screenX.Value, 0));
                _dragGuideLine.Left = workPt.X;
                _dragGuideLine.Top = 0;
                _dragGuideLine.Height = _work.Height;
                _dragGuideLine.BackColor = color.IsEmpty ? _horizontalRuler.CursorIndicatorColor : color;
                _dragGuideLine.Visible = true;
                _dragGuideLine.BringToFront();
            }
            else
            {
                _dragGuideLine.Visible = false;
            }
        };

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _bodyPanel.Controls.Add(_work);
        _bodyPanel.Controls.Add(_verticalRuler);
        _verticalRuler.SendToBack();

        _mainContent = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _mainContent.Controls.Add(_bodyPanel);
        _mainContent.Controls.Add(_rulerTopPanel);
        _rulerTopPanel.SendToBack();

        _statusBar = new StatusStrip { SizingGrip = false };
        _statusBar.Items.Add(new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Name = "state" });
        _statusBar.Items.Add(new ToolStripStatusLabel("Ln 1, Col 1") { Name = "cursor" });
        _statusBar.Items.Add(new ToolStripStatusLabel("UTF-8"));

        _homeScreen = new HomeScreen(_settings);
        _homeScreen.CreateNewRequested += () => { NewFile(); ShowEditorView(); };
        _homeScreen.OpenFileRequested += (path) => { OpenFile(path, true); ShowEditorView(); };
        _homeScreen.BrowseFileRequested += () => { Open(); if (_path != null) ShowEditorView(); };
        _homeScreen.OpenSettingsRequested += ShowShortcutSettings;
        _homeScreen.ToggleThemeRequested += ToggleTheme;

        Controls.Add(_homeScreen);
        Controls.Add(_mainContent);
        Controls.Add(_statusBar);
        Controls.Add(_codeToolbar);
        Controls.Add(_docToolbar);
        Controls.Add(_menu);

        _statusBar.SendToBack();
        _codeToolbar.SendToBack();
        _docToolbar.SendToBack();
        _menu.SendToBack();
        _mainContent.BringToFront();
        _homeScreen.BringToFront();

        _editor.Font = new Font(_settings.FontName, _settings.FontSize);
        SetWrap(_settings.WordWrap);
        _gutter.Visible = _settings.ShowLines;
        SetRuler(_settings.ShowRuler);

        UpdatePaperLayout();
        ApplyTheme();

        _work.Resize += (_, _) => UpdatePaperLayout();
        _work.Paint += (_, e) =>
        {
            if (_settings.PageViewMode && _paper.Visible)
            {
                var r = _paper.Bounds;
                using var shadowBrush = new SolidBrush(Color.FromArgb(18, 0, 0, 0));
                e.Graphics.FillRectangle(shadowBrush, r.X + 3, r.Y + 3, r.Width, r.Height);
            }
        };

        _editor.TextChanged += (_, _) => { if (!_highlighting) _dirty = true; UpdateState(); _gutter.Invalidate(); };
        _editor.SelectionChanged += (_, _) => { if (!_highlighting) { UpdateState(); SyncToolbarWithSelection(); } };
        _editor.VScroll += (_, _) => { _gutter.Invalidate(); _verticalRuler.Invalidate(); };
        _editor.HScroll += (_, _) => _horizontalRuler.Invalidate();
        _editor.Resize += (_, _) => { _gutter.Invalidate(); _horizontalRuler.Invalidate(); _verticalRuler.Invalidate(); };
        _gutter.Paint += PaintGutter;
        _printer.PrintPage += PrintPage;
        FormClosing += SaveSettings;
        KeyDown += HandleShortcut;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) OpenFile(files[0]); };
        AllowDrop = true;

        UpdateRulerCornerWidth();
        UpdateState();

        if (!string.IsNullOrWhiteSpace(initialFile))
        {
            var cleanPath = initialFile.Trim('"', ' ', '\'');
            if (File.Exists(cleanPath))
            {
                OpenFile(System.IO.Path.GetFullPath(cleanPath), true);
                ShowEditorView();
            }
            else
            {
                ShowHome();
            }
        }
        else
        {
            ShowHome();
        }
    }

    private void UpdatePaperLayout()
    {
        if (_settings.PageViewMode)
        {
            _paper.Dock = DockStyle.None;
            const int pageWidth = 816; // Largura padrão A4/Carta em 96 DPI (~8.5 polegadas)
            var workW = _work.ClientSize.Width;
            var workH = _work.ClientSize.Height;
            var left = Math.Max(24, (workW - pageWidth) / 2);
            var top = 20; // 20px de respiro abaixo da régua superior, sem ser comida pelas barras
            var pageHeight = Math.Max(300, workH - 40); // folga de 20px acima e 20px abaixo
            _paper.Bounds = new Rectangle(left, top, Math.Min(pageWidth, Math.Max(300, workW - 48)), pageHeight);
            _paper.Padding = new Padding(44, 28, 44, 28);
        }
        else
        {
            _paper.Dock = DockStyle.Fill;
            _paper.Padding = new Padding(16, 12, 16, 12);
        }
        _work.Invalidate();
        _paper.Invalidate();
        UpdateRulerCornerWidth();
    }

    private void TogglePageMode()
    {
        _settings.PageViewMode = !_settings.PageViewMode;
        UpdatePaperLayout();
        _btnPageMode.Checked = _settings.PageViewMode;
        _btnPageMode.ToolTipText = _settings.PageViewMode ? "Modo Página Ativo (816px) — Alternar para Largura Total" : "Modo Largura Total Ativo — Alternar para Modo Página";
    }

    private void UpdateRulerCornerWidth()
    {
        var leftWidth = _verticalRuler.Visible ? _verticalRuler.Width : 28;
        _corner.Width = Math.Max(28, leftWidth);
        _corner.Invalidate();
        _horizontalRuler.Invalidate();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        menu.Items.Add(MakeMenu("Arquivo", ("Início", ShowHome), ("Novo", NewFile), ("Abrir…", Open), ("Salvar", Save), ("Salvar como…", SaveAs), ("Exportar PDF…", ExportPdf), ("Imprimir…", Print)));
        menu.Items.Add(MakeMenu("Editar", ("Desfazer", () => _editor.Undo()), ("Refazer", () => _editor.Redo()), ("Localizar…", Find), ("Substituir…", Replace), ("Ir para linha…", GoToLine), ("Selecionar tudo", () => _editor.SelectAll())));
        menu.Items.Add(MakeMenu("Ver", ("Barra de ferramentas", () => _docToolbar.Visible = !_docToolbar.Visible), ("Barra de código", () => _codeToolbar.Visible = !_codeToolbar.Visible), ("Modo de Exibição (Página/Completo)", TogglePageMode), ("Quebra de linha", ToggleWrap), ("Números de linha", ToggleLines), ("Régua", ToggleRuler), ("Tema escuro", ToggleTheme), ("Tela cheia", ToggleFullscreen)));
        menu.Items.Add(MakeMenu("Formatar", ("Negrito", () => ToggleStyle(FontStyle.Bold)), ("Itálico", () => ToggleStyle(FontStyle.Italic)), ("Sublinhado", () => ToggleStyle(FontStyle.Underline)), ("Tachado", () => ToggleStyle(FontStyle.Strikeout)), ("Alinhar à Esquerda", () => SetAlignment(RichEditService.PFA_LEFT)), ("Centralizar", () => SetAlignment(RichEditService.PFA_CENTER)), ("Alinhar à Direita", () => SetAlignment(RichEditService.PFA_RIGHT)), ("Justificar", () => SetAlignment(RichEditService.PFA_JUSTIFY)), ("Limpar Formatação", ClearFormatting)));
        menu.Items.Add(MakeMenu("Configurar", ("Atalhos…", ShowShortcutSettings), ("Aplicativo padrão e formatos…", ShowDefaultAppSettings), ("Aparência", ToggleTheme)));
        menu.Items.Add(MakeMenu("Ajuda", ("Sobre o Artry…", ShowAboutDialog)));
        return menu;
    }

    private static ToolStripMenuItem MakeMenu(string text, params (string Text, Action Action)[] items)
    {
        var root = new ToolStripMenuItem(text);
        foreach (var (caption, action) in items) root.DropDownItems.Add(caption, null, (_, _) => action());
        return root;
    }

    private ToolStrip BuildDocToolbar()
    {
        var bar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(8, 4, 8, 4),
            Height = 38,
            RenderMode = ToolStripRenderMode.Professional
        };

        _btnHome = new ToolStripButton { Text = "Início", ToolTipText = "Tela Inicial / Documentos", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        _btnHome.Click += (_, _) => ShowHome();
        bar.Items.Add(_btnHome);
        bar.Items.Add(new ToolStripSeparator());

        var btnUndo = new ToolStripButton { ToolTipText = "Desfazer (Ctrl+Z)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        btnUndo.Click += (_, _) => _editor.Undo();
        var btnRedo = new ToolStripButton { ToolTipText = "Refazer (Ctrl+Y)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        btnRedo.Click += (_, _) => _editor.Redo();
        var btnPrint = new ToolStripButton { ToolTipText = "Imprimir (Ctrl+P)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        btnPrint.Click += (_, _) => Print();

        bar.Items.Add(btnUndo);
        bar.Items.Add(btnRedo);
        bar.Items.Add(btnPrint);
        bar.Items.Add(new ToolStripSeparator());

        _cboZoom = new ToolStripComboBox { Width = 68, ToolTipText = "Zoom" };
        _cboZoom.Items.AddRange(new object[] { "50%", "75%", "90%", "100%", "125%", "150%", "200%" });
        _cboZoom.Text = "100%";
        _cboZoom.SelectedIndexChanged += (_, _) =>
        {
            var raw = _cboZoom.Text.Replace("%", "").Trim();
            if (float.TryParse(raw, out var pct) && pct >= 20 && pct <= 500)
            {
                _editor.ZoomFactor = pct / 100f;
            }
        };
        bar.Items.Add(_cboZoom);
        bar.Items.Add(new ToolStripSeparator());

        _cboStyles = new ToolStripComboBox { Width = 118, ToolTipText = "Estilos de parágrafo" };
        _cboStyles.Items.AddRange(new object[] { "Texto normal", "Título", "Subtítulo", "Título 1", "Título 2", "Título 3" });
        _cboStyles.SelectedIndex = 0;
        _cboStyles.SelectedIndexChanged += (_, _) => ApplyTextStylePreset(_cboStyles.Text);
        bar.Items.Add(_cboStyles);
        bar.Items.Add(new ToolStripSeparator());

        _cboFont = new ToolStripComboBox { Width = 150, ToolTipText = "Família da fonte", AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        var families = FontFamily.Families.Select(f => f.Name).Order().Cast<object>().ToArray();
        _cboFont.Items.AddRange(families);
        _cboFont.Text = _settings.FontName;
        _cboFont.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFormatUI) return;
            RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
            _editor.Focus();
        };
        bar.Items.Add(_cboFont);
        bar.Items.Add(new ToolStripSeparator());

        _btnSizeDown = new ToolStripButton("−") { ToolTipText = "Diminuir tamanho da fonte (Ctrl+Shift+,)", Width = 24, DisplayStyle = ToolStripItemDisplayStyle.Text, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        _btnSizeDown.Click += (_, _) => StepFontSize(-1f);

        _txtFontSize = new ToolStripTextBox { Width = 38, TextBoxTextAlign = HorizontalAlignment.Center, Text = _settings.FontSize.ToString("0.##"), ToolTipText = "Tamanho da fonte" };
        _txtFontSize.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyTypedFontSize();
                e.SuppressKeyPress = true;
            }
        };
        _txtFontSize.LostFocus += (_, _) => ApplyTypedFontSize();

        _btnSizeUp = new ToolStripButton("+") { ToolTipText = "Aumentar tamanho da fonte (Ctrl+Shift+.)", Width = 24, DisplayStyle = ToolStripItemDisplayStyle.Text, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        _btnSizeUp.Click += (_, _) => StepFontSize(1f);

        bar.Items.Add(_btnSizeDown);
        bar.Items.Add(_txtFontSize);
        bar.Items.Add(_btnSizeUp);
        bar.Items.Add(new ToolStripSeparator());

        _btnBold = new ToolStripButton { ToolTipText = "Negrito (Ctrl+B)", CheckOnClick = false, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnBold.Click += (_, _) => ToggleStyle(FontStyle.Bold);

        _btnItalic = new ToolStripButton { ToolTipText = "Itálico (Ctrl+I)", CheckOnClick = false, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnItalic.Click += (_, _) => ToggleStyle(FontStyle.Italic);

        _btnUnderline = new ToolStripButton { ToolTipText = "Sublinhado (Ctrl+U)", CheckOnClick = false, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnUnderline.Click += (_, _) => ToggleStyle(FontStyle.Underline);

        _btnStrike = new ToolStripButton { ToolTipText = "Tachado", CheckOnClick = false, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnStrike.Click += (_, _) => ToggleStyle(FontStyle.Strikeout);

        _btnTextColor = new ToolStripButton { ToolTipText = "Cor do texto", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnTextColor.Click += (_, _) => PickTextColor();

        _btnHighlight = new ToolStripButton { ToolTipText = "Cor de destaque", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnHighlight.Click += (_, _) => PickHighlightColor();

        bar.Items.AddRange(new ToolStripItem[] { _btnBold, _btnItalic, _btnUnderline, _btnStrike, _btnTextColor, _btnHighlight });
        bar.Items.Add(new ToolStripSeparator());

        _btnAlignLeft = new ToolStripButton { ToolTipText = "Alinhar à esquerda (Ctrl+Shift+L)", DisplayStyle = ToolStripItemDisplayStyle.Image, Checked = true };
        _btnAlignLeft.Click += (_, _) => SetAlignment(RichEditService.PFA_LEFT);

        _btnAlignCenter = new ToolStripButton { ToolTipText = "Centralizar (Ctrl+Shift+E)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnAlignCenter.Click += (_, _) => SetAlignment(RichEditService.PFA_CENTER);

        _btnAlignRight = new ToolStripButton { ToolTipText = "Alinhar à direita (Ctrl+Shift+R)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnAlignRight.Click += (_, _) => SetAlignment(RichEditService.PFA_RIGHT);

        _btnAlignJustify = new ToolStripButton { ToolTipText = "Justificar (Ctrl+Shift+J)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnAlignJustify.Click += (_, _) => SetAlignment(RichEditService.PFA_JUSTIFY);

        _btnLineSpacing = new ToolStripDropDownButton { ToolTipText = "Espaçamento entre linhas", DisplayStyle = ToolStripItemDisplayStyle.Image };
        void AddSpacing(string label, float mul)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) =>
            {
                RichEditService.SetLineSpacing(_editor, mul);
                _editor.Focus();
            };
            _btnLineSpacing.DropDownItems.Add(item);
        }
        AddSpacing("1,0 (Simples)", 1.0f);
        AddSpacing("1,15", 1.15f);
        AddSpacing("1,5", 1.5f);
        AddSpacing("2,0 (Duplo)", 2.0f);

        bar.Items.AddRange(new ToolStripItem[] { _btnAlignLeft, _btnAlignCenter, _btnAlignRight, _btnAlignJustify, _btnLineSpacing });
        bar.Items.Add(new ToolStripSeparator());

        _btnBullet = new ToolStripButton { ToolTipText = "Lista com marcadores", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnBullet.Click += (_, _) => { RichEditService.ToggleBulletList(_editor); _editor.Focus(); };

        _btnNumbered = new ToolStripButton { ToolTipText = "Lista numerada", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnNumbered.Click += (_, _) => { RichEditService.ToggleNumberedList(_editor); _editor.Focus(); };

        _btnIndentLess = new ToolStripButton { ToolTipText = "Diminuir recuo", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnIndentLess.Click += (_, _) => { RichEditService.ChangeIndent(_editor, -360); _editor.Focus(); };

        _btnIndentMore = new ToolStripButton { ToolTipText = "Aumentar recuo", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnIndentMore.Click += (_, _) => { RichEditService.ChangeIndent(_editor, 360); _editor.Focus(); };

        bar.Items.AddRange(new ToolStripItem[] { _btnBullet, _btnNumbered, _btnIndentLess, _btnIndentMore });
        bar.Items.Add(new ToolStripSeparator());

        _btnClearFormat = new ToolStripButton { ToolTipText = "Limpar formatação (Ctrl+\\)", DisplayStyle = ToolStripItemDisplayStyle.Image };
        _btnClearFormat.Click += (_, _) => ClearFormatting();
        bar.Items.Add(_btnClearFormat);

        bar.Items.Add(new ToolStripSeparator());

        _btnPageMode = new ToolStripButton("📄 Página")
        {
            ToolTipText = "Alternar Modo de Folha (Google Docs)",
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Checked = _settings.PageViewMode
        };
        _btnPageMode.Click += (_, _) => TogglePageMode();
        bar.Items.Add(_btnPageMode);

        _btnRuler = new ToolStripButton("📏 Régua")
        {
            ToolTipText = "Exibir / Ocultar Réguas (Ctrl+R)",
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Checked = _settings.ShowRuler
        };
        _btnRuler.Click += (_, _) => ToggleRuler();
        bar.Items.Add(_btnRuler);

        return bar;
    }

    private ToolStrip BuildCodeToolbar()
    {
        var bar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(8, 2, 8, 2),
            Visible = false
        };

        bar.Items.Add(new ToolStripLabel("CÓDIGO:"));
        var btnCode = new ToolStripButton("Ativar Código") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnCode.Click += (_, _) => EnableCodeMode();
        bar.Items.Add(btnCode);

        var btnHighlight = new ToolStripButton("Realçar Sintaxe") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnHighlight.Click += (_, _) => Highlight(true);
        bar.Items.Add(btnHighlight);

        var btnIndent = new ToolStripButton("Tab → 4 espaços") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnIndent.Click += (_, _) => Indent();
        bar.Items.Add(btnIndent);

        var btnComment = new ToolStripButton("Comentário") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnComment.Click += (_, _) => ToggleComment();
        bar.Items.Add(btnComment);

        var btnLine = new ToolStripButton("Ir à linha") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnLine.Click += (_, _) => GoToLine();
        bar.Items.Add(btnLine);

        var btnWrap = new ToolStripButton("Sem quebra") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnWrap.Click += (_, _) => SetWrap(false);
        bar.Items.Add(btnWrap);

        var btnReload = new ToolStripButton("Recarregar") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnReload.Click += (_, _) => { if (_path is not null) OpenFile(_path, true); };
        bar.Items.Add(btnReload);

        var btnClose = new ToolStripButton("✕")
        {
            ToolTipText = "Fechar barra de código",
            Alignment = ToolStripItemAlignment.Right,
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        btnClose.Click += (_, _) => { _codeToolbar.Visible = false; };
        bar.Items.Add(btnClose);

        return bar;
    }

    private void ApplyTextStylePreset(string preset)
    {
        if (_updatingFormatUI) return;
        switch (preset)
        {
            case "Título":
                RichEditService.SetSelectionFontSize(_editor, 26f);
                RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
                break;
            case "Subtítulo":
                RichEditService.SetSelectionFontSize(_editor, 16f);
                RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
                break;
            case "Título 1":
                RichEditService.SetSelectionFontSize(_editor, 20f);
                RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
                break;
            case "Título 2":
                RichEditService.SetSelectionFontSize(_editor, 16f);
                RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
                break;
            case "Título 3":
                RichEditService.SetSelectionFontSize(_editor, 14f);
                RichEditService.SetSelectionFontName(_editor, _cboFont.Text);
                break;
            case "Texto normal":
            default:
                RichEditService.SetSelectionFontSize(_editor, 11f);
                break;
        }
        SyncToolbarWithSelection();
        _editor.Focus();
    }

    private void StepFontSize(float delta)
    {
        var current = 11f;
        if (float.TryParse(_txtFontSize.Text, out var parsed)) current = parsed;
        var next = Math.Clamp((float)Math.Round(current + delta), 1f, 300f);
        RichEditService.SetSelectionFontSize(_editor, next);
        _txtFontSize.Text = next.ToString("0.##");
        _editor.Focus();
    }

    private void ApplyTypedFontSize()
    {
        if (float.TryParse(_txtFontSize.Text, out var s) && s > 0)
        {
            var clamped = Math.Clamp(s, 1f, 300f);
            RichEditService.SetSelectionFontSize(_editor, clamped);
            _txtFontSize.Text = clamped.ToString("0.##");
        }
        else
        {
            _txtFontSize.Text = "11";
            RichEditService.SetSelectionFontSize(_editor, 11f);
        }
        _editor.Focus();
    }

    private void ToggleStyle(FontStyle style)
    {
        RichEditService.ToggleSelectionStyle(_editor, style);
        SyncToolbarWithSelection();
        _editor.Focus();
    }

    private void SetAlignment(short align)
    {
        RichEditService.SetSelectionAlignment(_editor, align);
        _btnAlignLeft.Checked = (align == RichEditService.PFA_LEFT);
        _btnAlignCenter.Checked = (align == RichEditService.PFA_CENTER);
        _btnAlignRight.Checked = (align == RichEditService.PFA_RIGHT);
        _btnAlignJustify.Checked = (align == RichEditService.PFA_JUSTIFY);
        _editor.Focus();
    }

    private void PickTextColor()
    {
        using var c = new ColorDialog { Color = _currentTextColor };
        if (c.ShowDialog(this) == DialogResult.OK)
        {
            _currentTextColor = c.Color;
            RichEditService.SetSelectionTextColor(_editor, c.Color);
            UpdateToolbarIconColors();
        }
        _editor.Focus();
    }

    private void PickHighlightColor()
    {
        using var c = new ColorDialog { Color = _currentHighlightColor == Color.Transparent ? Color.Yellow : _currentHighlightColor };
        if (c.ShowDialog(this) == DialogResult.OK)
        {
            _currentHighlightColor = c.Color;
            RichEditService.SetSelectionBackColor(_editor, c.Color);
            UpdateToolbarIconColors();
        }
        _editor.Focus();
    }

    private void ClearFormatting()
    {
        var defaultFont = new Font(_settings.FontName, _settings.FontSize);
        RichEditService.ClearFormatting(_editor, defaultFont, _settings.Dark ? AppPalette.Dark.Text : AppPalette.Light.Text);
        SyncToolbarWithSelection();
        _editor.Focus();
    }

    private void SyncToolbarWithSelection()
    {
        if (_updatingFormatUI || _highlighting) return;
        _updatingFormatUI = true;
        try
        {
            var f = RichEditService.GetSelectionFormatting(_editor);
            _btnBold.Checked = f.Bold;
            _btnItalic.Checked = f.Italic;
            _btnUnderline.Checked = f.Underline;
            _btnStrike.Checked = f.Strikeout;

            if (f.FontSize > 0)
            {
                _txtFontSize.Text = f.FontSize.ToString("0.##");
            }
            if (!string.IsNullOrEmpty(f.FontName) && _cboFont.Text != f.FontName)
            {
                _cboFont.Text = f.FontName;
            }

            _currentTextColor = f.TextColor;
            _currentHighlightColor = f.BackColor;
            UpdateToolbarIconColors();
        }
        catch { }
        finally
        {
            _updatingFormatUI = false;
        }
    }

    private void UpdateToolbarIconColors()
    {
        var palette = _settings.Dark ? AppPalette.Dark : AppPalette.Light;
        var iconColor = palette.Text;
        _btnTextColor.Image?.Dispose();
        _btnTextColor.Image = ToolbarIcons.CreateIcon("textcolor", iconColor, _currentTextColor.IsEmpty || _currentTextColor == Color.Transparent ? palette.Text : _currentTextColor);

        _btnHighlight.Image?.Dispose();
        _btnHighlight.Image = ToolbarIcons.CreateIcon("highlight", iconColor, _currentHighlightColor.IsEmpty || _currentHighlightColor == Color.Transparent ? Color.Yellow : _currentHighlightColor);
    }

    private void NewFile()
    {
        if (!ConfirmDiscard()) return;
        _path = null;
        _codeMode = false;
        _codeToolbar.Visible = false;
        _editor.Clear();
        _dirty = false;
        UpdateState();
        SyncToolbarWithSelection();
    }

    private void Open()
    {
        using var d = new OpenFileDialog { Filter = FileFilter };
        if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
    }

    private void OpenFile(string path, bool force = false)
    {
        if (!force && !ConfirmDiscard()) return;
        try
        {
            _highlighting = true;
            _path = path;
            _codeMode = CodeHighlighter.IsCode(path);
            _codeToolbar.Visible = _codeMode;
            if (System.IO.Path.GetExtension(path).Equals(".rtf", StringComparison.OrdinalIgnoreCase)) _editor.Rtf = File.ReadAllText(path);
            else _editor.Text = System.IO.Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ? DocxService.Read(path) : File.ReadAllText(path);
            _dirty = false;
            Highlight();
            UpdateState();
            SyncToolbarWithSelection();
            _settings.AddRecentFile(path);
            ShowEditorView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Não consegui abrir", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _highlighting = false; }
    }

    private void Save() { if (_path is null) { SaveAs(); return; } SaveTo(_path); }
    private void SaveAs()
    {
        var sugestao = _path is null ? "sem título.hyp" : System.IO.Path.GetFileName(_path);
        if (!sugestao.Contains('.')) sugestao += ".hyp";
        using var d = new SaveFileDialog
        {
            Title = "Salvar como",
            Filter = SaveFileFilter,
            DefaultExt = "hyp",
            AddExtension = true,
            FileName = sugestao,
            FilterIndex = sugestao.EndsWith(".artry", StringComparison.OrdinalIgnoreCase) ? 2
                        : sugestao.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? 3
                        : sugestao.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) ? 4
                        : sugestao.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || sugestao.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? 5 : 1
        };
        if (d.ShowDialog(this) == DialogResult.OK) { _path = d.FileName; SaveTo(_path); }
    }
    private void SaveTo(string path)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx") DocxService.Write(path, _editor);
            else if (ext == ".rtf") File.WriteAllText(path, _editor.Rtf);
            else if (ext == ".html" || ext == ".htm") File.WriteAllText(path, "<pre style=\"white-space:pre-wrap\">" + System.Security.SecurityElement.Escape(_editor.Text) + "</pre>");
            else File.WriteAllText(path, _editor.Text, new UTF8Encoding(false));
            _dirty = false;
            UpdateState();
            _settings.AddRecentFile(path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Não consegui salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowHome()
    {
        if (!ConfirmDiscard()) return;
        _homeScreen.ReloadRecentCards();
        _homeScreen.Visible = true;
        _homeScreen.BringToFront();
        _mainContent.Visible = false;
        _menu.Visible = false;
        _docToolbar.Visible = false;
        _codeToolbar.Visible = false;
        _statusBar.Visible = false;
        Text = "Artry";
    }

    private void ShowEditorView()
    {
        _homeScreen.Visible = false;
        _mainContent.Visible = true;
        _mainContent.BringToFront();
        _menu.Visible = true;
        _docToolbar.Visible = true;
        _codeToolbar.Visible = _codeMode;
        _statusBar.Visible = true;
        SetRuler(_settings.ShowRuler);
        UpdatePaperLayout();
        PerformLayout();
        UpdateState();
        _editor.Focus();
    }

    private void EnableCodeMode()
    {
        _codeMode = true;
        SetWrap(false);
        Highlight(true);
        _editor.Focus();
    }
    private void Highlight(bool force = false)
    {
        if (!_codeMode && !force) return;
        _highlighting = true;
        try { CodeHighlighter.Apply(_editor, _path, _editor.ForeColor, force || _codeMode); }
        finally { _highlighting = false; }
    }
    private void Indent()
    {
        if (_editor.SelectionLength == 0) _editor.SelectedText = "    ";
        else _editor.SelectedText = "    " + _editor.SelectedText.Replace("\n", "\n    ");
        _editor.Focus();
    }
    private void ToggleComment()
    {
        _codeMode = true;
        var marker = System.IO.Path.GetExtension(_path ?? "").ToLowerInvariant() is ".py" or ".sh" or ".ps1" or ".yml" or ".yaml" ? "# " : "// ";
        var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var start = _editor.GetFirstCharIndexFromLine(line);
        var value = _editor.Lines.ElementAtOrDefault(line) ?? "";
        if (value.StartsWith(marker, StringComparison.Ordinal)) { _editor.Select(start, marker.Length); _editor.SelectedText = ""; }
        else { _editor.Select(start, 0); _editor.SelectedText = marker; }
        Highlight(true); _editor.Focus();
    }

    private void Find() { var q = Ask("Localizar", "Texto para localizar:"); if (!string.IsNullOrEmpty(q)) { var i = _editor.Text.IndexOf(q, _editor.SelectionStart + _editor.SelectionLength, StringComparison.OrdinalIgnoreCase); if (i < 0) i = _editor.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase); if (i >= 0) { _editor.Select(i, q.Length); _editor.ScrollToCaret(); } else MessageBox.Show(this, "Nada encontrado.", "Artry"); } }
    private void Replace()
    {
        var q = Ask("Substituir", "Texto para localizar:");
        if (string.IsNullOrEmpty(q)) return;
        var r = Ask("Substituir", "Trocar por:") ?? "";
        var start = 0;
        var count = 0;
        _highlighting = true;
        try
        {
            while (true)
            {
                var idx = _editor.Text.IndexOf(q, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                _editor.Select(idx, q.Length);
                _editor.SelectedText = r;
                start = idx + r.Length;
                count++;
            }
        }
        finally { _highlighting = false; }

        if (count == 0) MessageBox.Show(this, "Nada encontrado.", "Artry");
        else { _dirty = true; UpdateState(); }
    }
    private void GoToLine() { var raw = Ask("Ir para linha", "Número da linha:"); if (int.TryParse(raw, out var line)) { line = Math.Clamp(line - 1, 0, _editor.Lines.Length - 1); _editor.SelectionStart = _editor.GetFirstCharIndexFromLine(line); _editor.ScrollToCaret(); } }
    private static string? Ask(string title, string label) { using var f = new Form { Text = title, Size = new Size(360, 145), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false }; var t = new TextBox { Left = 15, Top = 45, Width = 315 }; var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 174, Top = 78, Width = 75 }; f.Controls.AddRange([new Label { Text = label, Left = 15, Top = 15, AutoSize = true }, t, ok, new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 255, Top = 78, Width = 75 }]); f.AcceptButton = ok; return f.ShowDialog() == DialogResult.OK ? t.Text : null; }

    private void Print() { using var d = new PrintDialog { Document = _printer, UseEXDialog = true }; if (d.ShowDialog() == DialogResult.OK) { _printPosition = 0; _printer.Print(); } }
    private void PrintPage(object? sender, PrintPageEventArgs e)
    {
        var remaining = _editor.Text.Substring(_printPosition);
        var graphics = e.Graphics;
        if (graphics is null) return;
        graphics.MeasureString(remaining, _editor.Font, e.MarginBounds.Size, StringFormat.GenericTypographic, out var fitted, out _);
        if (fitted == 0) fitted = Math.Min(remaining.Length, 1);
        graphics.DrawString(remaining[..fitted], _editor.Font, Brushes.Black, e.MarginBounds, StringFormat.GenericTypographic);
        _printPosition += fitted;
        e.HasMorePages = _printPosition < _editor.TextLength;
    }
    private void ExportPdf()
    {
        using var d = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = System.IO.Path.GetFileNameWithoutExtension(_path ?? "sem título") + ".pdf" };
        if (d.ShowDialog() == DialogResult.OK) Pdf.Write(d.FileName, _editor.Text);
    }

    private void ToggleWrap() => SetWrap(!_editor.WordWrap);
    private void SetWrap(bool enabled)
    {
        _editor.WordWrap = enabled;
        _editor.ScrollBars = enabled ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
        _settings.WordWrap = enabled;
        if (Controls.OfType<StatusStrip>().Any()) UpdateState();
        _editor.Focus();
    }
    private void ToggleLines()
    {
        _gutter.Visible = !_gutter.Visible;
        _settings.ShowLines = _gutter.Visible;
        UpdateRulerCornerWidth();
    }
    private void ToggleRuler() => SetRuler(!_horizontalRuler.Visible);
    private void SetRuler(bool visible)
    {
        _horizontalRuler.Visible = visible;
        _verticalRuler.Visible = visible;
        _rulerTopPanel.Visible = visible;
        _settings.ShowRuler = visible;
        if (_btnRuler != null) _btnRuler.Checked = visible;
        UpdateRulerCornerWidth();
        _rulerTopPanel.PerformLayout();
        _bodyPanel.PerformLayout();
        _mainContent.PerformLayout();
        UpdatePaperLayout();
        if (Controls.OfType<StatusStrip>().Any()) UpdateState();
    }
    private void ToggleTheme() { _settings.Dark = !_settings.Dark; ApplyTheme(); Highlight(); }
    private void ToggleFullscreen() { _fullscreen = !_fullscreen; FormBorderStyle = _fullscreen ? FormBorderStyle.None : FormBorderStyle.Sizable; WindowState = _fullscreen ? FormWindowState.Maximized : FormWindowState.Normal; }
    private void ShowShortcutSettings()
    {
        using var dialog = new ShortcutSettingsForm(_settings);
        if (dialog.ShowDialog(this) == DialogResult.OK) UpdateState();
    }
    private void ShowDefaultAppSettings()
    {
        using var dialog = new DefaultAppForm(_settings);
        dialog.ShowDialog(this);
    }
    private void ShowAboutDialog()
    {
        var palette = _settings.Dark ? AppPalette.Dark : AppPalette.Light;
        using var dlg = new Form
        {
            Text = "Sobre o Artry",
            Size = new Size(380, 360),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Icon = IconArt.GetAppIcon(),
            BackColor = palette.Background,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 9.25f)
        };

        var pic = new PictureBox
        {
            Size = new Size(110, 110),
            Location = new Point(135, 24),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconArt.Draw(256),
            BackColor = Color.Transparent
        };

        var title = new Label
        {
            Text = "Artry",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = palette.Text,
            Location = new Point(20, 142),
            Size = new Size(340, 32),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var appVer = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var sub = new Label
        {
            Text = $"Versão {appVer}\nEditor de texto minimalista, rápido e focado.\nSuporte a documentos ricos, código e régua coordenada.",
            ForeColor = palette.Muted,
            Location = new Point(20, 178),
            Size = new Size(340, 60),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var ok = new Button
        {
            Text = "OK",
            Location = new Point(145, 258),
            Width = 90,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = palette.Accent,
            ForeColor = palette.HoverText,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        ok.FlatAppearance.BorderSize = 0;

        dlg.Controls.AddRange([pic, title, sub, ok]);
        dlg.AcceptButton = ok;
        dlg.ShowDialog(this);
    }
    private void ApplyTheme()
    {
        var palette = _settings.Dark ? AppPalette.Dark : AppPalette.Light;
        var previousText = _editor.ForeColor;
        BackColor = palette.Background;
        _mainContent.BackColor = palette.Background;
        _bodyPanel.BackColor = palette.Background;
        _work.BackColor = palette.Background;
        _paper.BackColor = palette.Paper;
        _editor.BackColor = palette.Paper;
        _editor.ForeColor = palette.Text;
        _gutter.BackColor = palette.Paper;
        _gutter.ForeColor = palette.Muted;

        _rulerTopPanel.BackColor = palette.Surface;
        _corner.SurfaceColor = palette.Surface;
        _corner.TextColor = palette.Muted;
        _corner.BorderColor = palette.Border;
        _corner.Invalidate();

        foreach (var ruler in new[] { _horizontalRuler, _verticalRuler })
        {
            ruler.SurfaceColor = palette.Surface;
            ruler.PaperColor = palette.Paper;
            ruler.TickColor = palette.Border;
            ruler.TextColor = palette.Muted;
            ruler.CursorIndicatorColor = palette.Accent;
            ruler.Invalidate();
        }

        ToolStripManager.Renderer = new SoftRenderer(palette);

        ApplyStrip(_menu, palette);
        ApplyStrip(_docToolbar, palette);
        ApplyStrip(_codeToolbar, palette);
        ApplyStrip(_statusBar, palette);

        _homeScreen?.RefreshTheme(palette);

        RebuildToolbarIcons(palette);
        RecolorDefaultText(previousText, palette.Text);
        _gutter.Invalidate();
    }

    private void RebuildToolbarIcons(AppPalette palette)
    {
        var c = palette.Text;
        var a = palette.Accent;

        void SetIcon(ToolStripButton btn, string name)
        {
            btn.Image?.Dispose();
            btn.Image = ToolbarIcons.CreateIcon(name, c, a);
        }

        if (_btnHome != null) { _btnHome.Image?.Dispose(); _btnHome.Image = ToolbarIcons.CreateIcon("home", c, a); }

        if (_docToolbar.Items.Count > 2 && _docToolbar.Items[2] is ToolStripButton btnUndo) { btnUndo.Image?.Dispose(); btnUndo.Image = ToolbarIcons.CreateIcon("undo", c, a); }
        if (_docToolbar.Items.Count > 3 && _docToolbar.Items[3] is ToolStripButton btnRedo) { btnRedo.Image?.Dispose(); btnRedo.Image = ToolbarIcons.CreateIcon("redo", c, a); }
        if (_docToolbar.Items.Count > 4 && _docToolbar.Items[4] is ToolStripButton btnPrint) { btnPrint.Image?.Dispose(); btnPrint.Image = ToolbarIcons.CreateIcon("print", c, a); }

        SetIcon(_btnBold, "bold");
        SetIcon(_btnItalic, "italic");
        SetIcon(_btnUnderline, "underline");
        SetIcon(_btnStrike, "strikethrough");
        SetIcon(_btnAlignLeft, "alignleft");
        SetIcon(_btnAlignCenter, "aligncenter");
        SetIcon(_btnAlignRight, "alignright");
        SetIcon(_btnAlignJustify, "alignjustify");

        _btnLineSpacing.Image?.Dispose();
        _btnLineSpacing.Image = ToolbarIcons.CreateIcon("linespacing", c, a);

        SetIcon(_btnBullet, "bulletlist");
        SetIcon(_btnNumbered, "numberedlist");
        SetIcon(_btnIndentLess, "indentdecrease");
        SetIcon(_btnIndentMore, "indentincrease");
        SetIcon(_btnClearFormat, "clearformatting");

        UpdateToolbarIconColors();
    }

    private static void ApplyStrip(ToolStrip strip, AppPalette palette)
    {
        strip.BackColor = palette.Surface;
        strip.ForeColor = palette.Text;
        strip.Renderer = new SoftRenderer(palette);
        ApplyItemsRecursively(strip.Items, palette);
    }

    private static void ApplyItemsRecursively(ToolStripItemCollection items, AppPalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = palette.Text;

            if (item is ToolStripControlHost host)
            {
                host.Control.BackColor = palette.Paper;
                host.Control.ForeColor = palette.Text;
            }

            if (item is ToolStripDropDownItem dropDownItem)
            {
                var dropDown = dropDownItem.DropDown;
                if (dropDown != null)
                {
                    dropDown.BackColor = palette.Surface;
                    dropDown.ForeColor = palette.Text;
                    dropDown.Renderer = new SoftRenderer(palette);
                    ApplyItemsRecursively(dropDown.Items, palette);
                }
            }
        }
    }
    private void RecolorDefaultText(Color oldText, Color newText)
    {
        if (oldText == newText) return;
        if (string.IsNullOrEmpty(_path) || !System.IO.Path.GetExtension(_path).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
        {
            _editor.ForeColor = newText;
            return;
        }
        if (_editor.TextLength > 30_000) return;
        var start = _editor.SelectionStart; var length = _editor.SelectionLength;
        _highlighting = true;
        try
        {
            for (var i = 0; i < _editor.TextLength; i++)
            {
                _editor.Select(i, 1);
                var color = _editor.SelectionColor;
                var wasNeutral = Math.Abs(color.R - oldText.R) < 8 && Math.Abs(color.G - oldText.G) < 8 && Math.Abs(color.B - oldText.B) < 8;
                var blackOnDark = _settings.Dark && color.R < 45 && color.G < 45 && color.B < 45;
                if (wasNeutral || blackOnDark) _editor.SelectionColor = newText;
            }
        }
        finally { _editor.Select(start, length); _highlighting = false; }
    }
    private void UpdateState() { var line = _editor.GetLineFromCharIndex(_editor.SelectionStart) + 1; var col = _editor.SelectionStart - _editor.GetFirstCharIndexFromLine(line - 1) + 1; ((ToolStripStatusLabel)_statusBar.Items[0]).Text = (_path is null ? "Sem título" : _path) + (_dirty ? "  • alterado" : "") + $"  · {(_codeMode ? "Código" : "Texto")} · quebra {(_editor.WordWrap ? "ligada" : "desligada")} · réguas {(_horizontalRuler.Visible ? "ligadas" : "desligadas")}"; ((ToolStripStatusLabel)_statusBar.Items[1]).Text = $"Ln {line}, Col {col}"; Text = (_dirty ? "• " : "") + (_path is null ? "Artry" : System.IO.Path.GetFileName(_path) + " — Artry"); }
    private void PaintGutter(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_gutter.BackColor);
        var total = _editor.GetLineFromCharIndex(_editor.TextLength) + 1;
        var first = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, 1)));
        var last = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, Math.Max(1, _editor.ClientSize.Height - 1))));
        last = Math.Min(total - 1, Math.Max(first, last + 1));
        for (var i = Math.Max(0, first); i <= last; i++)
        {
            var index = _editor.GetFirstCharIndexFromLine(i);
            if (index < 0) index = _editor.TextLength;
            var point = _editor.GetPositionFromCharIndex(index);
            var bounds = new Rectangle(0, point.Y, _gutter.ClientSize.Width - 6, _editor.Font.Height + 2);
            TextRenderer.DrawText(e.Graphics, (i + 1).ToString(), _editor.Font, bounds, _gutter.ForeColor, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
    private bool ConfirmDiscard() => !_dirty || MessageBox.Show(this, "Há alterações não salvas. Descartar?", "Artry", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    private void AlignCurrentLineToColumn(int targetCol)
    {
        if (targetCol < 0) return;
        var currentLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var lineStart = _editor.GetFirstCharIndexFromLine(currentLine);
        if (lineStart < 0) return;
        var lineText = _editor.Lines.ElementAtOrDefault(currentLine) ?? "";
        var leadingLen = 0;
        while (leadingLen < lineText.Length && (lineText[leadingLen] == ' ' || lineText[leadingLen] == '\t'))
            leadingLen++;
        var newLeading = new string(' ', targetCol);
        _highlighting = true;
        try
        {
            _editor.Select(lineStart, leadingLen);
            _editor.SelectedText = newLeading;
            _editor.Select(lineStart + newLeading.Length, 0);
        }
        finally { _highlighting = false; }
        _dirty = true;
        UpdateState();
    }

    private void AlignCurrentLineToSubRuler(SubRuler subRuler)
    {
        var cw = _horizontalRuler.GetCharWidth();
        var targetPixels = Math.Max(0, subRuler.Column * cw);

        // Define a margem de escrita / recuo de parágrafo no limite exato da sub-régua
        _editor.SelectionIndent = targetPixels;

        // Se estiver em modo código ou sem quebra de linha, também ajusta espaços para alinhamento puro de caracteres
        if (_codeMode || !_editor.WordWrap)
        {
            AlignCurrentLineToColumn(subRuler.Column);
        }

        _dirty = true;
        UpdateState();
        _horizontalRuler.Invalidate();
    }

    private void HandleShortcut(object? sender, KeyEventArgs e)
    {
        // Atalhos de sub-réguas: Ctrl+1 até Ctrl+9 para alinhar a linha atual à régua
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            var slot = e.KeyCode - Keys.D0;
            var subRuler = _horizontalRuler.GetSubRulerBySlot(slot);
            if (subRuler != null)
            {
                AlignCurrentLineToSubRuler(subRuler);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
        }

        // Tecla Delete: exclui a sub-régua selecionada se houver
        if (e.KeyCode == Keys.Delete && _horizontalRuler.SelectedSubRuler != null)
        {
            _horizontalRuler.DeleteSelectedSubRuler();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        // Atalhos de formatação rápida estilo Google Docs / Word
        if (e.Control && !e.Alt)
        {
            if (e.Shift)
            {
                if (e.KeyCode == Keys.L) { SetAlignment(RichEditService.PFA_LEFT); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.E) { SetAlignment(RichEditService.PFA_CENTER); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.R) { SetAlignment(RichEditService.PFA_RIGHT); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.J) { SetAlignment(RichEditService.PFA_JUSTIFY); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.OemPeriod || e.KeyCode == Keys.OemCloseBrackets) { StepFontSize(1f); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.Oemcomma || e.KeyCode == Keys.OemOpenBrackets) { StepFontSize(-1f); e.SuppressKeyPress = true; e.Handled = true; return; }
            }
            else
            {
                if (e.KeyCode == Keys.B) { ToggleStyle(FontStyle.Bold); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.I) { ToggleStyle(FontStyle.Italic); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.U) { ToggleStyle(FontStyle.Underline); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (e.KeyCode == Keys.OemPipe || e.KeyCode == Keys.OemBackslash) { ClearFormatting(); e.SuppressKeyPress = true; e.Handled = true; return; }
            }
        }

        var commands = new (string Name, Action Run)[]
        {
            ("Novo", NewFile), ("Abrir", Open), ("Salvar", Save), ("Salvar como", SaveAs), ("Imprimir", Print),
            ("Localizar", Find), ("Substituir", Replace), ("Ir para linha", GoToLine), ("Quebra de linha", ToggleWrap),
            ("Régua", ToggleRuler), ("Números de linha", ToggleLines),
            ("Gaveta texto", () => _docToolbar.Visible = !_docToolbar.Visible),
            ("Barra de ferramentas", () => _docToolbar.Visible = !_docToolbar.Visible),
            ("Gaveta código", () => _codeToolbar.Visible = !_codeToolbar.Visible),
            ("Barra de código", () => _codeToolbar.Visible = !_codeToolbar.Visible),
            ("Tema", ToggleTheme), ("Tela cheia", ToggleFullscreen),
            ("Negrito", () => ToggleStyle(FontStyle.Bold)), ("Itálico", () => ToggleStyle(FontStyle.Italic)), ("Sublinhado", () => ToggleStyle(FontStyle.Underline))
        };
        foreach (var (name, run) in commands)
        {
            if (!ShortcutCatalog.Matches(_settings.Shortcuts.GetValueOrDefault(name), e)) continue;
            run(); e.SuppressKeyPress = true; e.Handled = true; return;
        }
    }
    private void SaveSettings(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        _settings.FontName = _cboFont.Text;
        if (float.TryParse(_txtFontSize.Text, out var fs)) _settings.FontSize = fs;
        _settings.WindowX = Location.X;
        _settings.WindowY = Location.Y;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Save();
    }
    private const string SaveFileFilter = "Documento Hyper (*.hyp)|*.hyp|Documento Artry (*.artry)|*.artry|Documento Word (*.docx)|*.docx|Texto rico (*.rtf)|*.rtf|Texto Puro / Markdown (*.txt;*.md)|*.txt;*.md|Página Web (*.html;*.htm)|*.html;*.htm|Código e dados|*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.c;*.h;*.css;*.sql;*.json;*.xml;*.ps1;*.sh;*.yml;*.yaml|Todos os arquivos (*.*)|*.*";
    private const string FileFilter = "Documentos Hyper (*.hyp)|*.hyp|Todos os textos|*.hyp;*.artry;*.txt;*.md;*.rtf;*.docx;*.html;*.htm;*.json;*.xml;*.csv;*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.c;*.h;*.css;*.sql;*.ps1;*.sh;*.yml;*.yaml|Documento Artry (*.artry)|*.artry|Documento Word (*.docx)|*.docx|Texto rico (*.rtf)|*.rtf|Texto/Markdown (*.txt;*.md)|*.txt;*.md|Código e dados|*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.c;*.h;*.css;*.sql;*.json;*.xml;*.html;*.ps1;*.sh;*.yml;*.yaml|Todos os arquivos|*.*";
}
