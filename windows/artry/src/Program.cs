using System.Runtime.InteropServices;

namespace Artry;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            var first = args[0].Trim();
            if (first.Equals("--register", StringComparison.OrdinalIgnoreCase))
            {
                AttachConsole(-1);
                var settings = Settings.Load();
                IEnumerable<string> exts = settings.AssociatedExtensions?.Count > 0 ? settings.AssociatedExtensions : FileAssociationService.TextExtensions;
                var success = FileAssociationService.RegisterApp(exts);
                Console.WriteLine(success ? "Artry registrado com sucesso no Windows." : "Erro ao registrar no Windows.");
                Environment.ExitCode = success ? 0 : 1;
                return;
            }
            if (first.Equals("--unregister", StringComparison.OrdinalIgnoreCase))
            {
                AttachConsole(-1);
                var success = FileAssociationService.UnregisterApp();
                Console.WriteLine(success ? "Artry desregistrado com sucesso do Windows." : "Erro ao desregistrar do Windows.");
                Environment.ExitCode = success ? 0 : 1;
                return;
            }
            if (first.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                AttachConsole(-1);
                FileAssociationService.UnregisterApp();
                FileAssociationService.RemoveShortcutsAndUninstallEntry();
                Console.WriteLine("Artry desinstalado com sucesso.");
                MessageBox.Show("O Artry foi desinstalado do seu sistema com sucesso.", "Desinstalação — Artry", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.ExitCode = 0;
                return;
            }
        }

        ApplicationConfiguration.Initialize();

        // Garante registro automático do .hyp com o ícone oficial
        try
        {
            var settings = Settings.Load();
            IEnumerable<string> exts = settings.AssociatedExtensions?.Count > 0 ? settings.AssociatedExtensions : (IEnumerable<string>)FileAssociationService.TextExtensions;
            FileAssociationService.RegisterApp(exts);
        }
        catch { }

        string? initialFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : null;
        var form = new MainForm(initialFile) { Icon = IconArt.GetAppIcon() };
        Application.Run(form);
    }
}
