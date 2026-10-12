using System.Drawing;
using System.Windows.Forms;
using Artry;

namespace ArtryInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new SetupForm();
        try
        {
            form.Icon = IconArt.GetAppIcon();
        }
        catch { }
        Application.Run(form);
    }
}
