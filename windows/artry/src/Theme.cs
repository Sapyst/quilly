using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Artry;

internal sealed record AppPalette(
    Color Background,
    Color Paper,
    Color Surface,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentSoft,
    Color Border)
{
    public Color HoverText => Color.FromArgb(18, 20, 24); // #121418 Jet-black de alto contraste

    public static AppPalette Light => new(
        Color.FromArgb(244, 243, 239),
        Color.FromArgb(255, 254, 250),
        Color.FromArgb(250, 249, 246),
        Color.FromArgb(36, 42, 44),
        Color.FromArgb(103, 113, 113),
        Color.FromArgb(255, 222, 0),
        Color.FromArgb(255, 234, 115),
        Color.FromArgb(211, 215, 210));

    public static AppPalette Dark => new(
        Color.FromArgb(24, 26, 30),
        Color.FromArgb(32, 35, 41),
        Color.FromArgb(40, 44, 52),
        Color.FromArgb(242, 245, 248),
        Color.FromArgb(156, 163, 175),
        Color.FromArgb(255, 222, 0),
        Color.FromArgb(255, 222, 0),
        Color.FromArgb(64, 70, 80));
}

internal sealed class SoftRenderer : ToolStripProfessionalRenderer
{
    private readonly AppPalette _palette;
    private static readonly ImageAttributes _darkIconAttributes = CreateDarkIconAttributes();

    private static ImageAttributes CreateDarkIconAttributes()
    {
        var ia = new ImageAttributes();
        var colorMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0.07f, 0.08f, 0.09f, 0, 1 } // #121418 jet-black
        });
        ia.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        return ia;
    }

    public SoftRenderer(AppPalette palette) : base(new SoftColors(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // Garante que TODOS os menus suspensos, barras e popups sejam pintados
        // com o fundo escuro da paleta (_palette.Surface), eliminando o fundo branco nativo do Windows.
        using var brush = new SolidBrush(_palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Pinta a coluna esquerda de ícones/checks com o mesmo fundo escuro Surface,
        // eliminando qualquer faixa branca ou cinza clara.
        using var brush = new SolidBrush(_palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(_palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        if (e.Item.Selected || e.Item.Pressed)
        {
            // HIGHLIGHT: Amarelo Artry vibrante
            using var brush = new SolidBrush(_palette.AccentSoft);
            e.Graphics.FillRectangle(brush, rect);
        }
        else
        {
            // SEM HIGHLIGHT: Fundo escuro idêntico à superfície (Surface)
            using var brush = new SolidBrush(_palette.Surface);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripButton btn && (btn.Selected || btn.Pressed || btn.Checked))
        {
            using var brush = new SolidBrush(_palette.AccentSoft);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, btn.Size));
            return;
        }
        base.OnRenderButtonBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Se desabilitado: cinza atenuado.
        // Se em highlight (selecionado/pressionado/marcado): PRETO CARVÃO (#121418).
        // Se normal sem highlight: BRANCO NÍTIDO (#F2F5F8).
        Color textColor;
        if (!e.Item.Enabled)
        {
            textColor = _palette.Muted;
        }
        else
        {
            bool isHovered = e.Item.Selected || e.Item.Pressed || (e.Item is ToolStripButton btn && btn.Checked);
            textColor = isHovered ? _palette.HoverText : _palette.Text;
        }

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            e.TextRectangle,
            textColor,
            e.TextFormat);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        bool isHovered = e.Item.Selected || e.Item.Pressed || (e.Item is ToolStripButton btn && btn.Checked);
        if (isHovered && e.Image != null)
        {
            e.Graphics.DrawImage(
                e.Image,
                e.ImageRectangle,
                0,
                0,
                e.Image.Width,
                e.Image.Height,
                GraphicsUnit.Pixel,
                _darkIconAttributes);
        }
        else
        {
            base.OnRenderItemImage(e);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        bool isHovered = e.Item.Selected || e.Item.Pressed;
        var r = e.ImageRectangle;
        r.Inflate(1, 1);

        using (var bgBrush = new SolidBrush(isHovered ? _palette.Accent : _palette.Paper))
        {
            e.Graphics.FillRectangle(bgBrush, r);
        }
        using (var borderPen = new Pen(_palette.Border, 1))
        {
            e.Graphics.DrawRectangle(borderPen, r);
        }

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var checkColor = isHovered ? _palette.HoverText : _palette.Accent;
        TextRenderer.DrawText(e.Graphics, "✓", font, r, checkColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        bool isHovered = e.Item?.Selected == true || e.Item?.Pressed == true;
        e.ArrowColor = isHovered ? _palette.HoverText : _palette.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border, 1);
        if (e.Vertical)
        {
            int midX = e.Item.Width / 2;
            e.Graphics.DrawLine(pen, midX, 3, midX, e.Item.Height - 4);
        }
        else
        {
            int midY = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 24, midY, e.Item.Width - 6, midY);
        }
    }
}

internal sealed class SoftColors : ProfessionalColorTable
{
    private readonly AppPalette _p;
    public SoftColors(AppPalette p) => _p = p;

    // Menus e Dropdowns
    public override Color ToolStripDropDownBackground => _p.Surface;
    public override Color ImageMarginGradientBegin => _p.Surface;
    public override Color ImageMarginGradientMiddle => _p.Surface;
    public override Color ImageMarginGradientEnd => _p.Surface;
    public override Color ImageMarginRevealedGradientBegin => _p.Surface;
    public override Color ImageMarginRevealedGradientMiddle => _p.Surface;
    public override Color ImageMarginRevealedGradientEnd => _p.Surface;
    public override Color MenuBorder => _p.Border;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => _p.AccentSoft;
    public override Color MenuItemSelectedGradientBegin => _p.AccentSoft;
    public override Color MenuItemSelectedGradientEnd => _p.AccentSoft;
    public override Color MenuItemPressedGradientBegin => _p.AccentSoft;
    public override Color MenuItemPressedGradientMiddle => _p.AccentSoft;
    public override Color MenuItemPressedGradientEnd => _p.AccentSoft;

    // Barras principais
    public override Color MenuStripGradientBegin => _p.Surface;
    public override Color MenuStripGradientEnd => _p.Surface;
    public override Color ToolStripGradientBegin => _p.Surface;
    public override Color ToolStripGradientMiddle => _p.Surface;
    public override Color ToolStripGradientEnd => _p.Surface;
    public override Color StatusStripGradientBegin => _p.Surface;
    public override Color StatusStripGradientEnd => _p.Surface;
    public override Color ToolStripContentPanelGradientBegin => _p.Background;
    public override Color ToolStripContentPanelGradientEnd => _p.Background;
    public override Color ToolStripPanelGradientBegin => _p.Surface;
    public override Color ToolStripPanelGradientEnd => _p.Surface;

    // Botões
    public override Color ButtonSelectedHighlight => _p.AccentSoft;
    public override Color ButtonSelectedGradientBegin => _p.AccentSoft;
    public override Color ButtonSelectedGradientMiddle => _p.AccentSoft;
    public override Color ButtonSelectedGradientEnd => _p.AccentSoft;
    public override Color ButtonPressedHighlight => _p.AccentSoft;
    public override Color ButtonPressedGradientBegin => _p.AccentSoft;
    public override Color ButtonPressedGradientMiddle => _p.AccentSoft;
    public override Color ButtonPressedGradientEnd => _p.AccentSoft;
    public override Color ButtonCheckedHighlight => _p.AccentSoft;
    public override Color ButtonCheckedGradientBegin => _p.AccentSoft;
    public override Color ButtonCheckedGradientMiddle => _p.AccentSoft;
    public override Color ButtonCheckedGradientEnd => _p.AccentSoft;

    // Caixas de seleção e separadores
    public override Color CheckBackground => _p.Surface;
    public override Color CheckSelectedBackground => _p.AccentSoft;
    public override Color CheckPressedBackground => _p.AccentSoft;
    public override Color SeparatorDark => _p.Border;
    public override Color SeparatorLight => _p.Surface;
    public override Color GripDark => _p.Border;
    public override Color GripLight => _p.Surface;
    public override Color OverflowButtonGradientBegin => _p.Surface;
    public override Color OverflowButtonGradientMiddle => _p.Surface;
    public override Color OverflowButtonGradientEnd => _p.Surface;
    public override Color RaftingContainerGradientBegin => _p.Surface;
    public override Color RaftingContainerGradientEnd => _p.Surface;
}
