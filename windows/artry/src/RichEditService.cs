using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Artry;

internal static class RichEditService
{
    private const int WM_USER = 0x0400;
    private const int EM_GETCHARFORMAT = WM_USER + 58;
    private const int EM_SETCHARFORMAT = WM_USER + 68;
    private const int EM_GETPARAFORMAT = WM_USER + 61;
    private const int EM_SETPARAFORMAT = WM_USER + 71;
    private const int SCF_SELECTION = 0x0001;

    // CHARFORMAT2 Masks
    private const uint CFM_BOLD = 0x00000001;
    private const uint CFM_ITALIC = 0x00000002;
    private const uint CFM_UNDERLINE = 0x00000004;
    private const uint CFM_STRIKEOUT = 0x00000008;
    private const uint CFM_SIZE = 0x80000000;
    private const uint CFM_COLOR = 0x40000000;
    private const uint CFM_FACE = 0x20000000;
    private const uint CFM_BACKCOLOR = 0x04000000;

    // Effects
    private const uint CFE_BOLD = 0x0001;
    private const uint CFE_ITALIC = 0x0002;
    private const uint CFE_UNDERLINE = 0x0004;
    private const uint CFE_STRIKEOUT = 0x0008;
    private const uint CFE_AUTOCOLOR = 0x40000000;
    private const uint CFE_AUTOBACKCOLOR = 0x04000000;

    // PARAFORMAT2 Masks
    private const uint PFM_ALIGNMENT = 0x00000008;
    private const uint PFM_LINESPACING = 0x00000100;
    private const uint PFM_NUMBERING = 0x00000020;
    private const uint PFM_STARTINDENT = 0x00000001;

    // Alignments
    public const short PFA_LEFT = 1;
    public const short PFA_RIGHT = 2;
    public const short PFA_CENTER = 3;
    public const short PFA_JUSTIFY = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct CHARFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public uint dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szFaceName;
        public ushort wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public uint dwCookie;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PARAFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public ushort wNumbering;
        public ushort wEffects;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public ushort wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public short sStyle;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public ushort wShadingWeight;
        public ushort wShadingStyle;
        public ushort wNumberingStart;
        public ushort wNumberingStyle;
        public ushort wNumberingTab;
        public ushort wBorderSpace;
        public ushort wBorderWidth;
        public ushort wBorders;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref CHARFORMAT2 lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref PARAFORMAT2 lParam);

    public static void SetSelectionFontSize(RichTextBox rtb, float ptSize)
    {
        if (ptSize <= 1f) ptSize = 1f;
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_SIZE,
            yHeight = (int)(ptSize * 20)
        };
        SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref cf);
    }

    public static void SetSelectionFontName(RichTextBox rtb, string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return;
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_FACE,
            szFaceName = fontName
        };
        SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref cf);
    }

    public static void ToggleSelectionStyle(RichTextBox rtb, FontStyle style)
    {
        var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf<CHARFORMAT2>() };
        SendMessage(rtb.Handle, EM_GETCHARFORMAT, SCF_SELECTION, ref cf);

        uint mask = 0;
        uint effect = 0;

        switch (style)
        {
            case FontStyle.Bold:
                mask = CFM_BOLD;
                effect = (cf.dwEffects & CFE_BOLD) != 0 ? 0 : CFE_BOLD;
                break;
            case FontStyle.Italic:
                mask = CFM_ITALIC;
                effect = (cf.dwEffects & CFE_ITALIC) != 0 ? 0 : CFE_ITALIC;
                break;
            case FontStyle.Underline:
                mask = CFM_UNDERLINE;
                effect = (cf.dwEffects & CFE_UNDERLINE) != 0 ? 0 : CFE_UNDERLINE;
                break;
            case FontStyle.Strikeout:
                mask = CFM_STRIKEOUT;
                effect = (cf.dwEffects & CFE_STRIKEOUT) != 0 ? 0 : CFE_STRIKEOUT;
                break;
        }

        if (mask != 0)
        {
            var update = new CHARFORMAT2
            {
                cbSize = Marshal.SizeOf<CHARFORMAT2>(),
                dwMask = mask,
                dwEffects = effect
            };
            SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref update);
        }
    }

    public static void SetSelectionTextColor(RichTextBox rtb, Color color)
    {
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_COLOR,
            crTextColor = ColorTranslator.ToWin32(color)
        };
        SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref cf);
    }

    public static void SetSelectionBackColor(RichTextBox rtb, Color color)
    {
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_BACKCOLOR,
            crBackColor = color.IsEmpty || color == Color.Transparent ? 0 : ColorTranslator.ToWin32(color),
            dwEffects = color.IsEmpty || color == Color.Transparent ? CFE_AUTOBACKCOLOR : 0
        };
        SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref cf);
    }

    public static void SetSelectionAlignment(RichTextBox rtb, short alignment)
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            dwMask = PFM_ALIGNMENT,
            wAlignment = (ushort)alignment,
            rgxTabs = new int[32]
        };
        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static void SetLineSpacing(RichTextBox rtb, float multiple)
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            dwMask = PFM_LINESPACING,
            rgxTabs = new int[32]
        };

        if (Math.Abs(multiple - 1.0f) < 0.05f)
        {
            pf.bLineSpacingRule = 0; // Single
        }
        else if (Math.Abs(multiple - 1.5f) < 0.05f)
        {
            pf.bLineSpacingRule = 1; // 1.5 lines
        }
        else if (Math.Abs(multiple - 2.0f) < 0.05f)
        {
            pf.bLineSpacingRule = 2; // Double
        }
        else
        {
            pf.bLineSpacingRule = 5;
            pf.dyLineSpacing = (int)(20 * multiple * 20);
        }

        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static void ToggleBulletList(RichTextBox rtb)
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            rgxTabs = new int[32]
        };
        SendMessage(rtb.Handle, EM_GETPARAFORMAT, 0, ref pf);

        pf.dwMask = PFM_NUMBERING;
        pf.wNumbering = pf.wNumbering == 1 ? (ushort)0 : (ushort)1;
        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static void ToggleNumberedList(RichTextBox rtb)
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            rgxTabs = new int[32]
        };
        SendMessage(rtb.Handle, EM_GETPARAFORMAT, 0, ref pf);

        pf.dwMask = PFM_NUMBERING;
        pf.wNumbering = pf.wNumbering == 2 ? (ushort)0 : (ushort)2;
        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static void ChangeIndent(RichTextBox rtb, int deltaTwips)
    {
        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            rgxTabs = new int[32]
        };
        SendMessage(rtb.Handle, EM_GETPARAFORMAT, 0, ref pf);

        pf.dwMask = PFM_STARTINDENT;
        pf.dxStartIndent = Math.Max(0, pf.dxStartIndent + deltaTwips);
        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static void ClearFormatting(RichTextBox rtb, Font baseFont, Color baseColor)
    {
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_BOLD | CFM_ITALIC | CFM_UNDERLINE | CFM_STRIKEOUT | CFM_SIZE | CFM_FACE | CFM_COLOR | CFM_BACKCOLOR,
            dwEffects = CFE_AUTOBACKCOLOR,
            yHeight = (int)(baseFont.Size * 20),
            szFaceName = baseFont.FontFamily.Name,
            crTextColor = ColorTranslator.ToWin32(baseColor)
        };
        SendMessage(rtb.Handle, EM_SETCHARFORMAT, SCF_SELECTION, ref cf);

        var pf = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf<PARAFORMAT2>(),
            dwMask = PFM_ALIGNMENT | PFM_NUMBERING | PFM_STARTINDENT,
            wAlignment = (ushort)PFA_LEFT,
            wNumbering = 0,
            dxStartIndent = 0,
            rgxTabs = new int[32]
        };
        SendMessage(rtb.Handle, EM_SETPARAFORMAT, 0, ref pf);
    }

    public static (string FontName, float FontSize, bool Bold, bool Italic, bool Underline, bool Strikeout, Color TextColor, Color BackColor) GetSelectionFormatting(RichTextBox rtb)
    {
        var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf<CHARFORMAT2>() };
        SendMessage(rtb.Handle, EM_GETCHARFORMAT, SCF_SELECTION, ref cf);

        var fontName = string.IsNullOrEmpty(cf.szFaceName) ? rtb.Font.FontFamily.Name : cf.szFaceName;
        var fontSize = cf.yHeight > 0 ? cf.yHeight / 20f : rtb.Font.Size;
        var isBold = (cf.dwEffects & CFE_BOLD) != 0;
        var isItalic = (cf.dwEffects & CFE_ITALIC) != 0;
        var isUnderline = (cf.dwEffects & CFE_UNDERLINE) != 0;
        var isStrikeout = (cf.dwEffects & CFE_STRIKEOUT) != 0;
        var textColor = ColorTranslator.FromWin32(cf.crTextColor);
        var backColor = (cf.dwEffects & CFE_AUTOBACKCOLOR) != 0 ? Color.Transparent : ColorTranslator.FromWin32(cf.crBackColor);

        return (fontName, fontSize, isBold, isItalic, isUnderline, isStrikeout, textColor, backColor);
    }
}
