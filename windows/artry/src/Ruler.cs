using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Artry;

internal enum RulerAxis { Horizontal, Vertical }

/// <summary>
/// Modelo de sub-rÃ©gua / guia de margem no editor.
/// </summary>
internal sealed class SubRuler
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Column { get; set; }
    public Color Color { get; set; }
    public int? ShortcutSlot { get; set; } // 1..9
    public bool IsSelected { get; set; }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Verifica se alguma tecla numÃ©rica de 1 a 9 estÃ¡ atualmente pressionada.
    /// </summary>
    public static int? GetActiveDigitKey()
    {
        for (int k = 1; k <= 9; k++)
        {
            // VK 0x31..0x39 (alfanumÃ©rico) e 0x61..0x69 (numpad)
            if ((GetAsyncKeyState(0x30 + k) & 0x8000) != 0 || (GetAsyncKeyState(0x60 + k) & 0x8000) != 0)
                return k;
        }
        return null;
    }
}

/// <summary>
/// Gerador de cores distintas utilizando o Ã¢ngulo Ã¡ureo para evitar sobreposiÃ§Ã£o ou semelhanÃ§a de matiz.
/// </summary>
internal static class SubRulerColorGenerator
{
    private static float _hue = 205f;

    public static Color NextDistinctColor()
    {
        _hue = (_hue + 137.508f) % 360f;
        return FromHsl(_hue, 0.84f, 0.56f);
    }

    private static Color FromHsl(float h, float s, float l)
    {
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = l - c / 2f;
        float r = 0, g = 0, b = 0;
        if (h < 60f) { r = c; g = x; }
        else if (h < 120f) { r = x; g = c; }
        else if (h < 180f) { g = c; b = x; }
        else if (h < 240f) { g = x; b = c; }
        else if (h < 300f) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(
            (int)Math.Clamp((r + m) * 255f, 0, 255),
            (int)Math.Clamp((g + m) * 255f, 0, 255),
            (int)Math.Clamp((b + m) * 255f, 0, 255)
        );
    }
}

/// <summary>
/// Bloco de junÃ§Ã£o superior esquerdo da rÃ©gua: um quadradinho limpo e alinhado fora do documento,
/// que permite arrastar uma nova sub-rÃ©gua a qualquer momento (segurando Ctrl+1..9 para atribuir atalho).
/// </summary>
internal sealed class RulerCorner : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Color.FromArgb(103, 113, 113);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(211, 215, 210);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; } = Color.FromArgb(250, 249, 246);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Ruler? HorizontalRuler { get; set; }

    public RulerCorner()
    {
        Dock = DockStyle.Left;
        Width = 28;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Paint += Draw;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    private void Draw(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);
        using var pen = new Pen(BorderColor);
        // Borda divisÃ³ria inferior e direita
        e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height - 1);

        // Ãcone discreto no cantinho indicando arraste de rÃ©guas
        using var dotBrush = new SolidBrush(TextColor);
        var cx = Width / 2f;
        var cy = Height / 2f;
        e.Graphics.FillEllipse(dotBrush, cx - 2.5f, cy - 2.5f, 5f, 5f);
    }

    private bool _isDraggingFromCorner;

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HorizontalRuler != null)
        {
            _isDraggingFromCorner = true;
            Capture = true;
            var digit = SubRuler.GetActiveDigitKey();
            HorizontalRuler.BeginDragNewFromCorner(digit);
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDraggingFromCorner && HorizontalRuler != null)
        {
            var screenPt = PointToScreen(e.Location);
            var rulerPt = HorizontalRuler.PointToClient(screenPt);
            HorizontalRuler.UpdateDragSubRuler(rulerPt.X);
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_isDraggingFromCorner)
        {
            _isDraggingFromCorner = false;
            Capture = false;
            HorizontalRuler?.EndDragSubRuler();
        }
    }
}

/// <summary>
/// RÃ©gua coordenada externa ao documento com suporte a marcador de limite de escrita (margem),
/// sub-rÃ©guas interativas (1..9), guias visuais e alinhamento do texto.
/// </summary>
internal sealed class Ruler : Panel
{
    private readonly RichTextBox _editor;
    private readonly RulerAxis _axis;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Paper { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TickColor { get; set; } = Color.FromArgb(120, 130, 130);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Color.FromArgb(95, 105, 105);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; } = Color.FromArgb(244, 243, 239);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PaperColor { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GuideColor { get; set; } = Color.FromArgb(180, 100, 100);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CursorIndicatorColor { get; set; } = Color.FromArgb(83, 117, 102);

    // Evento para linha guia vertical em tempo real no documento
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<int?, Color>? OnGuideLineChanged { get; set; }

    // Sub-réguas (disponíveis na régua horizontal)
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<SubRuler> SubRulers { get; } = new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SubRuler? SelectedSubRuler { get; private set; }

    private SubRuler? _draggingSubRuler;
    private bool _draggingMargin;

    public Ruler(RichTextBox editor, RulerAxis axis)
    {
        _editor = editor;
        _axis = axis;
        DoubleBuffered = true;
        Dock = axis == RulerAxis.Horizontal ? DockStyle.Fill : DockStyle.Left;
        if (axis == RulerAxis.Horizontal) Height = 26; else Width = 28;
        Margin = Padding.Empty;
        Visible = false;

        _editor.FontChanged += (_, _) => Invalidate();
        _editor.HScroll += (_, _) => Invalidate();
        _editor.VScroll += (_, _) => Invalidate();
        _editor.SelectionChanged += (_, _) => Invalidate();
        _editor.Resize += (_, _) => Invalidate();
        Paint += Draw;

        if (axis == RulerAxis.Horizontal)
        {
            MouseDown += OnRulerMouseDown;
            MouseMove += OnRulerMouseMove;
            MouseUp += OnRulerMouseUp;
            MouseDoubleClick += OnRulerDoubleClick;
        }
    }

    public SubRuler? GetSubRulerBySlot(int slot) => SubRulers.FirstOrDefault(r => r.ShortcutSlot == slot);

    public void AssignSlot(SubRuler ruler, int slot)
    {
        foreach (var r in SubRulers.Where(r => r.ShortcutSlot == slot))
            r.ShortcutSlot = null;
        ruler.ShortcutSlot = slot;
        Invalidate();
    }

    public SubRuler CreateSubRuler(int column, int? slot = null)
    {
        var sr = new SubRuler
        {
            Column = Math.Max(0, column),
            Color = SubRulerColorGenerator.NextDistinctColor(),
            IsSelected = true
        };
        foreach (var r in SubRulers) r.IsSelected = false;
        if (slot.HasValue) AssignSlot(sr, slot.Value);
        SubRulers.Add(sr);
        SelectedSubRuler = sr;
        Invalidate();
        return sr;
    }

    public void DeleteSelectedSubRuler()
    {
        if (SelectedSubRuler != null)
        {
            SubRulers.Remove(SelectedSubRuler);
            SelectedSubRuler = null;
            Invalidate();
        }
    }

    public void BeginDragNewFromCorner(int? slot)
    {
        _draggingSubRuler = CreateSubRuler(0, slot);
        var screenX = PointToScreen(new Point(GetXAtColumn(0), 0)).X;
        OnGuideLineChanged?.Invoke(screenX, _draggingSubRuler.Color);
    }

    public void UpdateDragSubRuler(int mouseX)
    {
        if (_draggingSubRuler != null)
        {
            _draggingSubRuler.Column = GetColumnAtX(mouseX);
            var digit = SubRuler.GetActiveDigitKey();
            if (digit.HasValue && _draggingSubRuler.ShortcutSlot != digit.Value)
                AssignSlot(_draggingSubRuler, digit.Value);

            var screenX = PointToScreen(new Point(mouseX, 0)).X;
            OnGuideLineChanged?.Invoke(screenX, _draggingSubRuler.Color);
            Invalidate();
        }
    }

    public void EndDragSubRuler()
    {
        _draggingSubRuler = null;
        OnGuideLineChanged?.Invoke(null, Color.Empty);
        Invalidate();
    }

    public int GetTextOriginX()
    {
        try
        {
            if (_editor.IsHandleCreated)
            {
                var screenPt = _editor.PointToScreen(new Point(0, 0));
                var rulerPt = PointToClient(screenPt);
                return rulerPt.X + 1;
            }
        }
        catch { }
        return 28;
    }

    public int GetCharWidth(Graphics? g = null)
    {
        if (g != null)
            return Math.Max(7, TextRenderer.MeasureText(g, "0", _editor.Font, Size.Empty, TextFormatFlags.NoPadding).Width);
        using var tempG = CreateGraphics();
        return Math.Max(7, TextRenderer.MeasureText(tempG, "0", _editor.Font, Size.Empty, TextFormatFlags.NoPadding).Width);
    }

    public int GetColumnAtX(int x)
    {
        var origin = GetTextOriginX();
        var cw = GetCharWidth();
        return Math.Max(0, (int)Math.Round((double)(x - origin) / cw));
    }

    public int GetXAtColumn(int col)
    {
        var origin = GetTextOriginX();
        var cw = GetCharWidth();
        return origin + col * cw;
    }

    private bool HitTestMargin(int mouseX, int mouseY)
    {
        var origin = GetTextOriginX();
        var marginX = origin + _editor.SelectionIndent;
        return Math.Abs(mouseX - marginX) <= 7 && mouseY <= 20;
    }

    private SubRuler? HitTestSubRuler(int mouseX, int mouseY)
    {
        var origin = GetTextOriginX();
        var cw = GetCharWidth();
        for (int i = SubRulers.Count - 1; i >= 0; i--)
        {
            var sr = SubRulers[i];
            var x = origin + sr.Column * cw;
            if (Math.Abs(mouseX - x) <= 8 && mouseY <= 18)
                return sr;
        }
        return null;
    }

    private void OnRulerMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // 1. Testa se clicou no Marcador de Limite de Escrita (Margem)
            if (HitTestMargin(e.X, e.Y))
            {
                _draggingMargin = true;
                Capture = true;
                var screenX = PointToScreen(new Point(e.X, 0)).X;
                OnGuideLineChanged?.Invoke(screenX, CursorIndicatorColor);
                Invalidate();
                return;
            }

            // 2. Testa se clicou em uma sub-rÃ©gua existente
            var hit = HitTestSubRuler(e.X, e.Y);
            if (hit != null)
            {
                foreach (var r in SubRulers) r.IsSelected = false;
                hit.IsSelected = true;
                SelectedSubRuler = hit;

                if (ModifierKeys.HasFlag(Keys.Control))
                {
                    var digit = SubRuler.GetActiveDigitKey();
                    if (digit.HasValue) AssignSlot(hit, digit.Value);
                }

                _draggingSubRuler = hit;
                Capture = true;
                var screenX = PointToScreen(new Point(e.X, 0)).X;
                OnGuideLineChanged?.Invoke(screenX, hit.Color);
                Invalidate();
            }
            else
            {
                // 3. Clicou no corpo da rÃ©gua: cria uma nova sub-rÃ©gua naquele ponto
                foreach (var r in SubRulers) r.IsSelected = false;
                var digit = SubRuler.GetActiveDigitKey();
                var col = GetColumnAtX(e.X);
                _draggingSubRuler = CreateSubRuler(col, digit);
                Capture = true;
                var screenX = PointToScreen(new Point(e.X, 0)).X;
                OnGuideLineChanged?.Invoke(screenX, _draggingSubRuler.Color);
                Invalidate();
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            var hit = HitTestSubRuler(e.X, e.Y);
            if (hit != null)
            {
                foreach (var r in SubRulers) r.IsSelected = false;
                hit.IsSelected = true;
                SelectedSubRuler = hit;
                Invalidate();
                ShowSubRulerContextMenu(hit, e.Location);
            }
        }
    }

    private void OnRulerMouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingMargin)
        {
            var origin = GetTextOriginX();
            var newIndent = Math.Max(0, e.X - origin);
            _editor.SelectionIndent = newIndent;
            var screenX = PointToScreen(new Point(e.X, 0)).X;
            OnGuideLineChanged?.Invoke(screenX, CursorIndicatorColor);
            Invalidate();
        }
        else if (_draggingSubRuler != null)
        {
            _draggingSubRuler.Column = GetColumnAtX(e.X);
            var digit = SubRuler.GetActiveDigitKey();
            if (digit.HasValue && _draggingSubRuler.ShortcutSlot != digit.Value)
                AssignSlot(_draggingSubRuler, digit.Value);

            var screenX = PointToScreen(new Point(e.X, 0)).X;
            OnGuideLineChanged?.Invoke(screenX, _draggingSubRuler.Color);
            Invalidate();
        }
        else
        {
            if (HitTestMargin(e.X, e.Y))
                Cursor = Cursors.SizeWE;
            else if (HitTestSubRuler(e.X, e.Y) != null)
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Default;
        }
    }

    private void OnRulerMouseUp(object? sender, MouseEventArgs e)
    {
        if (_draggingMargin)
        {
            _draggingMargin = false;
            Capture = false;
            OnGuideLineChanged?.Invoke(null, Color.Empty);
            Invalidate();
        }
        else if (_draggingSubRuler != null)
        {
            // Se arrastar para fora da rÃ©gua na vertical, exclui
            if (e.Y > Height + 35 || e.Y < -25)
            {
                SubRulers.Remove(_draggingSubRuler);
                if (SelectedSubRuler == _draggingSubRuler) SelectedSubRuler = null;
            }
            _draggingSubRuler = null;
            Capture = false;
            OnGuideLineChanged?.Invoke(null, Color.Empty);
            Invalidate();
        }
    }

    private void OnRulerDoubleClick(object? sender, MouseEventArgs e)
    {
        var hit = HitTestSubRuler(e.X, e.Y);
        if (hit != null)
        {
            using var cd = new ColorDialog { Color = hit.Color };
            if (cd.ShowDialog(this) == DialogResult.OK)
            {
                hit.Color = cd.Color;
                Invalidate();
            }
        }
        else if (HitTestMargin(e.X, e.Y))
        {
            // Duplo clique na margem reseta para zero
            _editor.SelectionIndent = 0;
            Invalidate();
        }
    }

    private void ShowSubRulerContextMenu(SubRuler sr, Point location)
    {
        var menu = new ContextMenuStrip();
        var changeColor = new ToolStripMenuItem("Alterar corâ€¦", null, (_, _) =>
        {
            using var cd = new ColorDialog { Color = sr.Color };
            if (cd.ShowDialog(this) == DialogResult.OK) { sr.Color = cd.Color; Invalidate(); }
        });
        var slotMenu = new ToolStripMenuItem("Vincular atalho");
        for (int i = 1; i <= 9; i++)
        {
            var slotNum = i;
            var item = new ToolStripMenuItem($"Ctrl+{slotNum}", null, (_, _) => AssignSlot(sr, slotNum))
            {
                Checked = sr.ShortcutSlot == slotNum
            };
            slotMenu.DropDownItems.Add(item);
        }
        slotMenu.DropDownItems.Add(new ToolStripSeparator());
        slotMenu.DropDownItems.Add(new ToolStripMenuItem("Nenhum", null, (_, _) => { sr.ShortcutSlot = null; Invalidate(); }));

        var alignItem = new ToolStripMenuItem($"Alinhar parÃ¡grafo atual a esta rÃ©gua (Col {sr.Column})", null, (_, _) =>
        {
            var cw = GetCharWidth();
            _editor.SelectionIndent = Math.Max(0, sr.Column * cw);
            Invalidate();
        });

        var deleteItem = new ToolStripMenuItem("Excluir rÃ©gua", null, (_, _) =>
        {
            SubRulers.Remove(sr);
            if (SelectedSubRuler == sr) SelectedSubRuler = null;
            Invalidate();
        });

        menu.Items.AddRange([changeColor, slotMenu, alignItem, new ToolStripSeparator(), deleteItem]);
        menu.Show(this, location);
    }

    private void Draw(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);
        if (_axis == RulerAxis.Horizontal) DrawHorizontal(e.Graphics);
        else DrawVertical(e.Graphics);
    }

    private void DrawHorizontal(Graphics graphics)
    {
        var charWidth = GetCharWidth(graphics);
        using var pen = new Pen(TickColor);
        using var guidePen = new Pen(GuideColor) { DashStyle = DashStyle.Dot };
        using var cursorBrush = new SolidBrush(CursorIndicatorColor);

        var textOriginX = GetTextOriginX();

        // 1. Desenha a trilha da pÃ¡gina (Paper track) alinhada Ã  folha do documento
        if (Paper != null && Paper.Visible)
        {
            try
            {
                var pScreen = Paper.PointToScreen(new Point(0, 0));
                var pLocal = PointToClient(pScreen);
                var pRect = new Rectangle(pLocal.X, 0, Paper.Width, Height - 1);
                using var paperBrush = new SolidBrush(PaperColor);
                graphics.FillRectangle(paperBrush, pRect);

                // Bordas laterais da folha na rÃ©gua
                using var borderPen = new Pen(TickColor);
                graphics.DrawLine(borderPen, pRect.Left, 0, pRect.Left, Height - 1);
                graphics.DrawLine(borderPen, pRect.Right, 0, pRect.Right, Height - 1);
            }
            catch { }
        }

        // 2. PosiÃ§Ã£o da coluna do cursor
        var selLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var lineStart = _editor.GetFirstCharIndexFromLine(selLine);
        var cursorCol = _editor.SelectionStart - (lineStart >= 0 ? lineStart : 0);
        var cursorX = textOriginX + _editor.SelectionIndent + cursorCol * charWidth;

        // Marcador do cursor no topo (triÃ¢ngulo)
        if (cursorX >= 0 && cursorX <= Width)
        {
            Point[] triangle = [
                new(cursorX - 3, 0),
                new(cursorX + 3, 0),
                new(cursorX, 5)
            ];
            graphics.FillPolygon(cursorBrush, triangle);
        }

        // 3. Ticks de colunas
        using var labelFont = new Font(_editor.Font.FontFamily, Math.Min(7.5f, _editor.Font.Size * 0.75f));
        for (var col = 0; ; col++)
        {
            var x = textOriginX + col * charWidth;
            if (x > Width) break;
            if (x < 0) continue;

            var isMajor = col % 10 == 0;
            var isMedium = col % 5 == 0;

            var tickHeight = isMajor ? 10 : (isMedium ? 6 : 3);
            graphics.DrawLine(pen, x, Height - 1, x, Height - 1 - tickHeight);

            // Guias de 80 e 120 colunas
            if (col is 80 or 120)
            {
                graphics.DrawLine(guidePen, x, 0, x, Height - 1);
            }

            // RÃ³tulo da coluna (10, 20, 30...)
            if (isMajor && col > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    col.ToString(),
                    labelFont,
                    new Point(x + 2, 1),
                    TextColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                );
            }
        }

        // 4. Marcador de Limite de InÃ­cio de Escrita (Margem Esquerda do parÃ¡grafo)
        var marginX = textOriginX + _editor.SelectionIndent;
        if (marginX >= 0 && marginX <= Width)
        {
            using var marginBrush = new SolidBrush(CursorIndicatorColor);
            using var marginBorder = new Pen(Color.FromArgb(50, 0, 0, 0));

            // RetÃ¢ngulo superior (barra de margem)
            graphics.FillRectangle(marginBrush, marginX - 4, 1, 8, 3);
            graphics.DrawRectangle(marginBorder, marginX - 4, 1, 8, 3);

            // TriÃ¢ngulo apontando para baixo (limite de escrita)
            Point[] marginTriangle = [
                new(marginX - 5, 4),
                new(marginX + 5, 4),
                new(marginX, 11)
            ];
            graphics.FillPolygon(marginBrush, marginTriangle);
            graphics.DrawPolygon(marginBorder, marginTriangle);

            // Linha pontilhada na base da rÃ©gua
            using var linePen = new Pen(CursorIndicatorColor) { DashStyle = DashStyle.Dot };
            graphics.DrawLine(linePen, marginX, 11, marginX, Height - 1);
        }

        // 5. Desenhar sub-rÃ©guas (guias personalizadas)
        foreach (var sr in SubRulers)
        {
            var x = textOriginX + sr.Column * charWidth;
            if (x < -10 || x > Width + 10) continue;

            using var subPen = new Pen(sr.Color) { DashStyle = DashStyle.Dot };
            graphics.DrawLine(subPen, x, 14, x, Height - 1);

            Point[] badge = [
                new(x - 6, 0),
                new(x + 6, 0),
                new(x + 6, 9),
                new(x, 15),
                new(x - 6, 9)
            ];

            using var brush = new SolidBrush(sr.Color);
            graphics.FillPolygon(brush, badge);

            using var badgeBorder = new Pen(sr.IsSelected ? Color.White : Color.FromArgb(70, 0, 0, 0), sr.IsSelected ? 2 : 1);
            graphics.DrawPolygon(badgeBorder, badge);

            if (sr.ShortcutSlot.HasValue)
            {
                var lum = (0.299 * sr.Color.R + 0.587 * sr.Color.G + 0.114 * sr.Color.B) / 255.0;
                var textClr = lum > 0.6 ? Color.Black : Color.White;
                using var numFont = new Font("Segoe UI", 6.5f, FontStyle.Bold);
                TextRenderer.DrawText(
                    graphics,
                    sr.ShortcutSlot.Value.ToString(),
                    numFont,
                    new Rectangle(x - 6, 0, 12, 10),
                    textClr,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
            }
        }

        // Linha divisÃ³ria inferior da rÃ©gua
        graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    private void DrawVertical(Graphics graphics)
    {
        using var pen = new Pen(TickColor);
        using var cursorBrush = new SolidBrush(CursorIndicatorColor);

        // 1. Trilha da folha na régua vertical
        if (Paper != null && Paper.Visible)
        {
            try
            {
                var pScreen = Paper.PointToScreen(new Point(0, 0));
                var pLocal = PointToClient(pScreen);
                var pRect = new Rectangle(0, pLocal.Y, Width - 1, Paper.Height);
                using var paperBrush = new SolidBrush(PaperColor);
                graphics.FillRectangle(paperBrush, pRect);

                // Bordas superior e inferior da folha na régua vertical
                using var borderPen = new Pen(TickColor);
                graphics.DrawLine(borderPen, 0, pRect.Top, Width - 1, pRect.Top);
                graphics.DrawLine(borderPen, 0, pRect.Bottom, Width - 1, pRect.Bottom);
            }
            catch { }
        }

        var totalLines = _editor.GetLineFromCharIndex(_editor.TextLength) + 1;
        var first = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, 1)));
        var last = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, Math.Max(1, _editor.ClientSize.Height - 1))));
        last = Math.Min(totalLines - 1, Math.Max(first, last + 1));

        var currentLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        using var labelFont = new Font(_editor.Font.FontFamily, Math.Min(7.5f, _editor.Font.Size * 0.75f));

        for (var i = Math.Max(0, first); i <= last; i++)
        {
            var index = _editor.GetFirstCharIndexFromLine(i);
            if (index < 0) index = _editor.TextLength;
            var pt = _editor.GetPositionFromCharIndex(index);
            int y;
            try
            {
                var screenPt = _editor.PointToScreen(new Point(0, pt.Y));
                y = PointToClient(screenPt).Y;
            }
            catch
            {
                y = pt.Y;
            }

            if (y < -20 || y > Height + 20) continue;

            var lineNum = i + 1;
            var isMajor = lineNum % 10 == 0;
            var isMedium = lineNum % 5 == 0;

            var tickWidth = isMajor ? 12 : (isMedium ? 7 : 4);
            graphics.DrawLine(pen, Width - 1, y, Width - 1 - tickWidth, y);

            if (i == currentLine)
            {
                Point[] marker = [
                    new(0, y + 2),
                    new(5, y + 6),
                    new(0, y + 10)
                ];
                graphics.FillPolygon(cursorBrush, marker);
            }

            if (isMedium && y + 10 < Height)
            {
                TextRenderer.DrawText(
                    graphics,
                    lineNum.ToString(),
                    labelFont,
                    new Rectangle(0, y + 1, Width - tickWidth - 2, _editor.Font.Height),
                    TextColor,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                );
            }
        }

        graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}
