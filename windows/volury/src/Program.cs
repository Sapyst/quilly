using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Volury;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Registra associação .epub silenciosamente
        RegistraAssociacaoEpub();

        string? arquivoInicial = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
        Application.Run(new MainForm(arquivoInicial));
    }

    private static void RegistraAssociacaoEpub()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            using var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Volury.Epub");
            progId.SetValue("", "EPUB Document (Volury)");

            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");

            using var cmd = progId.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");

            using var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.epub\OpenWithProgids");
            ext.SetValue("Volury.Epub", Array.Empty<byte>(), RegistryValueKind.None);

            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* Silencioso: não essencial */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
