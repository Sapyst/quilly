namespace Quilly;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var initialFile = args.FirstOrDefault(File.Exists);
        Application.Run(new MainForm(initialFile));
    }
}
