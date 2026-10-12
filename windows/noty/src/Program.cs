using System.Runtime.InteropServices;

namespace Noty;

internal static class Native
{
    public const int HWND_BROADCAST = 0xFFFF;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    // Win11 22H2+: material da janela (0 auto, 1 nenhum, 2 mica, 3 acrilico).
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_BORDER_COLOR = 34;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int L, R, T, B; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

    // Acrilico persistente (nao some com a janela desfocada) com tint proprio.
    // Mesmo mecanismo do TranslucentTB: WCA_ACCENT_POLICY (19), state 4.
    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_ACRYLIC = 4;
    public const int WM_ACTIVATE = 0x0006;

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY { public int State; public int Flags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        RegistraAssociacaoHyp();

        // Sem instancia unica de proposito: cada arquivo pode ter a sua janela.
        // Um caminho passado na linha de comando ("Abrir com" do Explorer) abre direto.
        var arquivo = args.FirstOrDefault(File.Exists);
        Application.Run(new MainForm(arquivo));
    }

    private static void RegistraAssociacaoHyp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
            var exeDir = Path.GetDirectoryName(exePath) ?? "";

            var icoPath = Path.Combine(exeDir, "hyp.ico");
            if (!File.Exists(icoPath))
            {
                try
                {
                    using var stream = typeof(Program).Assembly.GetManifestResourceStream("Noty.hyp.ico");
                    if (stream != null)
                    {
                        using var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fs);
                    }
                }
                catch { }
            }
            if (!File.Exists(icoPath)) icoPath = $"{exePath},0";

            using (var hypProgIdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Hyp.Document"))
            {
                hypProgIdKey.SetValue("", "Documento Hyper (.hyp)");
                hypProgIdKey.SetValue("FriendlyTypeName", "Documento Hyper (.hyp)");

                using var iconKey = hypProgIdKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", icoPath);

                using var cmdKey = hypProgIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");

                using var openKey = hypProgIdKey.CreateSubKey(@"shell\open");
                openKey.SetValue("FriendlyAppName", "Noty");
            }

            using (var extKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\.hyp"))
            {
                using var defIconKey = extKey.CreateSubKey("DefaultIcon");
                defIconKey.SetValue("", icoPath);

                using var openWith = extKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue("Hyp.Document", Array.Empty<byte>(), Microsoft.Win32.RegistryValueKind.None);

                using var openWithList = extKey.CreateSubKey(@"OpenWithList\Noty.exe");

                var current = extKey.GetValue("") as string;
                if (string.IsNullOrEmpty(current) || current == "Hyp.Document")
                {
                    extKey.SetValue("", "Hyp.Document");
                }
            }

            Native.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
