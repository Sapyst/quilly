using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace Artry;

internal sealed class HomeScreen : UserControl
{
    public event Action? CreateNewRequested;
    public event Action<string>? OpenFileRequested;
    public event Action? BrowseFileRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ToggleThemeRequested;

    private readonly Settings _settings;
    private AppPalette _palette;
    private readonly FlowLayoutPanel _cardsPanel;
    private readonly Panel _topBar;

    public HomeScreen(Settings settings)
    {
        _settings = settings;
        _palette = _settings.Dark ? AppPalette.Dark : AppPalette.Light;

        Dock = DockStyle.Fill;
        BackColor = _palette.Background;
        AutoScroll = true;

        // Barra Superior Limpa e Moderna
        _topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = _palette.Surface,
            Padding = new Padding(24, 0, 24, 0)
        };
        _topBar.Paint += (s, e) =>
        {
            using var pen = new Pen(_palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };

        // Painel de navegação totalmente alinhado à ESQUERDA (sem botões soltos na direita)
        var navPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 11, 0, 0),
            BackColor = Color.Transparent
        };

        var logoBox = new PictureBox
        {
            Size = new Size(34, 34),
            Margin = new Padding(0, 1, 10, 0),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconArt.Draw(64),
            BackColor = Color.Transparent
        };

        var lblBrand = new Label
        {
            Text = "Artry",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = _palette.Text,
            Margin = new Padding(0, 4, 24, 0),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var btnOpen = CreateHeaderButton("Abrir arquivo…", BrowseFileRequested);
        var btnSettings = CreateHeaderButton("Configurações", OpenSettingsRequested);
        var btnTheme = CreateHeaderButton("Alternar tema", ToggleThemeRequested);

        navPanel.Controls.AddRange([logoBox, lblBrand, btnOpen, btnSettings, btnTheme]);
        _topBar.Controls.Add(navPanel);

        // Painel Principal de Conteúdo
        var mainContent = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(32, 28, 32, 32),
            BackColor = _palette.Background
        };

        _cardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 12, 8, 24),
            BackColor = Color.Transparent
        };

        mainContent.Controls.Add(_cardsPanel);
        Controls.Add(mainContent);
        Controls.Add(_topBar);

        RefreshTheme(_palette);
        ReloadRecentCards();
    }

    public void RefreshTheme(AppPalette palette)
    {
        _palette = palette;
        BackColor = _palette.Background;
        _topBar.BackColor = _palette.Surface;
        _topBar.Invalidate();

        foreach (Control c in _cardsPanel.Controls)
        {
            if (c is DocumentCard card)
            {
                card.UpdatePalette(_palette);
            }
        }
    }

    public void ReloadRecentCards()
    {
        _cardsPanel.SuspendLayout();
        _cardsPanel.Controls.Clear();

        // 1. Card "Novo documento" (folha de papel com ícone '+' limpo centralizado)
        var newDocCard = new DocumentCard(
            title: "Novo documento",
            filePath: null,
            isCreateNew: true,
            palette: _palette);

        newDocCard.CardClicked += () => CreateNewRequested?.Invoke();
        _cardsPanel.Controls.Add(newDocCard);

        // 2. Cards APENAS de arquivos que realmente existem no disco
        var recents = _settings.RecentFiles ?? new List<string>();
        var validFiles = recents.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();

        foreach (var file in validFiles)
        {
            var card = new DocumentCard(
                title: Path.GetFileName(file),
                filePath: file,
                isCreateNew: false,
                palette: _palette);

            var capturedFile = file;
            card.CardClicked += () => OpenFileRequested?.Invoke(capturedFile);
            card.RemoveRequested += () =>
            {
                _settings.RemoveRecentFile(capturedFile);
                ReloadRecentCards();
            };
            _cardsPanel.Controls.Add(card);
        }

        // Se NÃO há arquivos criados/abertos ainda, NÃO exibe nenhum título falso ou card dummy.
        // Fica apenas a opção real de criar um novo documento.

        _cardsPanel.ResumeLayout();
    }

    private Button CreateHeaderButton(string text, Action? action)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(6, 1, 6, 0),
            Padding = new Padding(12, 0, 12, 0),
            BackColor = Color.Transparent,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = _palette.Border;
        btn.MouseEnter += (_, _) =>
        {
            btn.BackColor = _palette.AccentSoft;
            btn.ForeColor = _palette.HoverText;
            btn.FlatAppearance.BorderColor = _palette.Accent;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.BackColor = Color.Transparent;
            btn.ForeColor = _palette.Text;
            btn.FlatAppearance.BorderColor = _palette.Border;
        };
        if (action != null) btn.Click += (_, _) => action();
        return btn;
    }
}

/// <summary>
/// Card visual representando o documento na tela inicial:
/// - Se for "Novo documento": folha de papel com um ícone de '+' limpo no centro.
/// - Se for um documento existente: prévia real da 1ª página do documento, suavemente ofuscada/focada.
/// - Base: faixa escura com o título real do arquivo (amarela com texto preto no hover).
/// </summary>
internal sealed class DocumentCard : UserControl
{
    public event Action? CardClicked;
    public event Action? RemoveRequested;

    private readonly string _title;
    private readonly string? _filePath;
    private readonly bool _isCreateNew;
    private readonly string _previewText;
    private AppPalette _palette;
    private bool _isHovered;

    public DocumentCard(string title, string? filePath, bool isCreateNew, AppPalette palette)
    {
        _title = title;
        _filePath = filePath;
        _isCreateNew = isCreateNew;
        _palette = palette;
        _previewText = isCreateNew || string.IsNullOrEmpty(filePath) ? "" : ExtractDocumentPreviewText(filePath);

        Size = new Size(184, 236);
        Margin = new Padding(14);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        MouseEnter += (_, _) => { _isHovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _isHovered = false; Invalidate(); };
        Click += (_, _) => CardClicked?.Invoke();

        if (!string.IsNullOrEmpty(_filePath))
        {
            var menu = new ContextMenuStrip();
            var itemOpen = new ToolStripMenuItem("Abrir no editor", null, (_, _) => CardClicked?.Invoke());
            var itemFolder = new ToolStripMenuItem("Abrir local do arquivo", null, (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_filePath}\""); } catch { }
            });
            var itemRemove = new ToolStripMenuItem("Remover dos recentes", null, (_, _) => RemoveRequested?.Invoke());
            menu.Items.AddRange([itemOpen, itemFolder, itemRemove]);
            ContextMenuStrip = menu;
        }
    }

    public void UpdatePalette(AppPalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int w = Width;
        int h = Height;
        int bannerHeight = 44;
        int paperAreaHeight = h - bannerHeight;

        // Fundo geral do card
        using (var bgBrush = new SolidBrush(_palette.Surface))
        {
            g.FillRectangle(bgBrush, 0, 0, w, h);
        }

        // Retângulo da folha de papel centralizada
        var sheetRect = new Rectangle(14, 12, w - 28, paperAreaHeight - 16);

        if (_isCreateNew || string.IsNullOrEmpty(_filePath))
        {
            DrawNewDocumentPage(g, sheetRect);
        }
        else
        {
            DrawDocumentPagePreview(g, sheetRect, _previewText);
        }

        // Faixa inferior com o título
        var bannerRect = new Rectangle(0, paperAreaHeight, w, bannerHeight);
        Color bannerBg = _isHovered ? _palette.AccentSoft : Color.FromArgb(32, 36, 42);
        Color bannerTextColor = _isHovered ? _palette.HoverText : Color.FromArgb(242, 245, 248);

        using (var bannerBrush = new SolidBrush(bannerBg))
        {
            g.FillRectangle(bannerBrush, bannerRect);
        }

        // Título centralizado
        using var fontTitle = new Font("Segoe UI", 9f, FontStyle.Bold);
        var displayTitle = _title;
        if (displayTitle.Length > 21) displayTitle = displayTitle[..19] + "…";

        TextRenderer.DrawText(
            g,
            displayTitle.ToUpperInvariant(),
            fontTitle,
            bannerRect,
            bannerTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Borda externa de contorno do Card
        Color borderColor = _isHovered ? _palette.Accent : _palette.Border;
        int borderWidth = _isHovered ? 2 : 1;
        using var borderPen = new Pen(borderColor, borderWidth);
        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
    }

    private static void DrawNewDocumentPage(Graphics g, Rectangle pageRect)
    {
        // Folha de papel branca/limpa
        using (var paperBrush = new SolidBrush(Color.FromArgb(252, 252, 254)))
        {
            g.FillRectangle(paperBrush, pageRect);
        }
        using (var borderPen = new Pen(Color.FromArgb(215, 220, 228), 1))
        {
            g.DrawRectangle(borderPen, pageRect);
        }

        // Canto superior dobrado (dog-ear de folha de documento)
        int fold = 14;
        Point[] foldPoints = [
            new Point(pageRect.Right - fold, pageRect.Top),
            new Point(pageRect.Right, pageRect.Top + fold),
            new Point(pageRect.Right - fold, pageRect.Top + fold)
        ];
        using (var foldBrush = new SolidBrush(Color.FromArgb(230, 234, 240)))
        {
            g.FillPolygon(foldBrush, foldPoints);
        }
        using (var foldPen = new Pen(Color.FromArgb(195, 202, 212), 1))
        {
            g.DrawLine(foldPen, foldPoints[0], foldPoints[2]);
            g.DrawLine(foldPen, foldPoints[2], foldPoints[1]);
        }

        // Ícone de '+' limpo no centro da folha
        int cx = pageRect.Left + (pageRect.Width / 2);
        int cy = pageRect.Top + (pageRect.Height / 2);
        int r = 18;

        using var plusPen = new Pen(Color.FromArgb(115, 125, 140), 3.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawLine(plusPen, cx - r, cy, cx + r, cy);
        g.DrawLine(plusPen, cx, cy - r, cx, cy + r);
    }

    private static void DrawDocumentPagePreview(Graphics g, Rectangle pageRect, string text)
    {
        // 1. Folha de papel branca
        using (var paperBrush = new SolidBrush(Color.FromArgb(252, 252, 254)))
        {
            g.FillRectangle(paperBrush, pageRect);
        }
        using (var borderPen = new Pen(Color.FromArgb(215, 220, 228), 1))
        {
            g.DrawRectangle(borderPen, pageRect);
        }

        // 2. Canto dobrado
        int fold = 14;
        Point[] foldPoints = [
            new Point(pageRect.Right - fold, pageRect.Top),
            new Point(pageRect.Right, pageRect.Top + fold),
            new Point(pageRect.Right - fold, pageRect.Top + fold)
        ];
        using (var foldBrush = new SolidBrush(Color.FromArgb(230, 234, 240)))
        {
            g.FillPolygon(foldBrush, foldPoints);
        }
        using (var foldPen = new Pen(Color.FromArgb(195, 202, 212), 1))
        {
            g.DrawLine(foldPen, foldPoints[0], foldPoints[2]);
            g.DrawLine(foldPen, foldPoints[2], foldPoints[1]);
        }

        // 3. Renderiza linhas reais do texto da primeira página do documento
        if (!string.IsNullOrWhiteSpace(text))
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            using var fontRegular = new Font("Segoe UI", 4.75f, FontStyle.Regular);
            using var fontHeading = new Font("Segoe UI", 5.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(50, 56, 68));

            float textX = pageRect.Left + 10;
            float textY = pageRect.Top + 14;
            float maxY = pageRect.Bottom - 10;
            int linesDrawn = 0;

            foreach (var rawLine in lines)
            {
                if (textY >= maxY) break;
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    textY += 6;
                    continue;
                }

                bool isHeading = linesDrawn == 0 || line.StartsWith('#') || (line.Length < 30 && char.IsUpper(line[0]));
                var font = isHeading ? fontHeading : fontRegular;
                var clean = line.TrimStart('#', ' ', '-');

                if (clean.Length > 36) clean = clean[..34] + "…";

                g.DrawString(clean, font, textBrush, new PointF(textX, textY));
                textY += isHeading ? 11.5f : 8.5f;
                linesDrawn++;
            }
        }

        // 4. Película translúcida suave ("meio ofuscada")
        using var frostBrush = new LinearGradientBrush(
            pageRect,
            Color.FromArgb(55, 252, 252, 254),
            Color.FromArgb(140, 248, 249, 252),
            LinearGradientMode.Vertical);
        g.FillRectangle(frostBrush, pageRect);
    }

    private static string ExtractDocumentPreviewText(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".docx")
            {
                return DocxService.Read(filePath);
            }
            if (ext == ".rtf")
            {
                var rtfContent = File.ReadAllText(filePath);
                using var rtb = new RichTextBox();
                rtb.Rtf = rtfContent;
                return rtb.Text;
            }

            // Arquivos de texto, markdown e código
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var sb = new StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }
}
