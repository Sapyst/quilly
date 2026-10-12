using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuillyInstaller;

internal sealed class SetupForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private readonly Panel _header;
    private readonly Panel _footer;
    private readonly Panel _content;

    private readonly Panel _pageConfig;
    private readonly Panel _pageProgress;
    private readonly Panel _pageFinish;

    private readonly TextBox _txtPath;
    private readonly CheckBox _chkDesktop;
    private readonly CheckBox _chkStartMenu;
    private readonly CheckBox _chkLaunchNow;

    private readonly ProgressBar _progressBar;
    private readonly Label _lblProgressStatus;
    private readonly Label _lblProgressDetail;

    private readonly Button _btnAction;
    private readonly Button _btnCancel;

    private int _currentStep = 1;
    private string _installedExePath = "";

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val) return val == 0;
        }
        catch { }
        return true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AplicaTemaBarraTitulo();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AplicaTemaBarraTitulo();
    }

    private void AplicaTemaBarraTitulo()
    {
        try
        {
            var isDark = IsSystemDark();
            int dark = isDark ? 1 : 0;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

            if (isDark)
            {
                int captionColor = 0x001A1714;
                DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                int textColor = 0x00F8F5F2;
                DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

                int borderColor = 0x003D332D;
                DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
        }
        catch { }
    }

    public SetupForm()
    {
        var isDark = IsSystemDark();
        var bg = isDark ? Color.FromArgb(20, 23, 26) : Color.FromArgb(245, 245, 247);
        var surface = isDark ? Color.FromArgb(27, 30, 35) : Color.FromArgb(255, 255, 255);
        var card = isDark ? Color.FromArgb(32, 36, 43) : Color.FromArgb(250, 250, 252);
        var text = isDark ? Color.FromArgb(242, 245, 248) : Color.FromArgb(24, 26, 30);
        var muted = isDark ? Color.FromArgb(154, 166, 178) : Color.FromArgb(100, 110, 118);
        var border = isDark ? Color.FromArgb(45, 51, 61) : Color.FromArgb(218, 222, 225);
        var accent = Color.FromArgb(224, 90, 61); // Quilly Coral

        Text = "Instalação da Quilly Suite";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 520);
        MinimumSize = new Size(640, 520);
        MaximumSize = new Size(640, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.25f);
        BackColor = bg;
        ForeColor = text;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuillyInstaller.icon.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // Header
        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = surface,
            Padding = new Padding(24, 16, 24, 14)
        };
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        var lblHeaderTitle = new Label
        {
            Text = "Quilly Suite",
            Font = new Font("Gabriola", 22f, FontStyle.Bold),
            ForeColor = accent,
            Location = new Point(20, 10),
            AutoSize = true
        };

        var lblHeaderSub = new Label
        {
            Text = "Instalador da Suite de Criação e Documentos (Noty e Artry)",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = muted,
            Location = new Point(24, 52),
            AutoSize = true
        };

        _header.Controls.AddRange([lblHeaderTitle, lblHeaderSub]);

        // Footer
        _footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = surface,
            Padding = new Padding(20, 14, 20, 14)
        };
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        _btnAction = new Button
        {
            Text = "Instalar",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(110, 36),
            Location = new Point(_footer.Width - 134, 14),
            Cursor = Cursors.Hand
        };
        _btnAction.FlatAppearance.BorderSize = 0;
        _btnAction.Click += OnActionClick;

        _btnCancel = new Button
        {
            Text = "Cancelar",
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Color.Transparent,
            ForeColor = muted,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(95, 36),
            Location = new Point(_footer.Width - 238, 14),
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderColor = border;
        _btnCancel.Click += (_, _) => Close();

        _footer.Controls.AddRange([_btnAction, _btnCancel]);

        // Content
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 14)
        };

        // --- Step 1: Config Page ---
        _pageConfig = new Panel { Dock = DockStyle.Fill, BackColor = bg };

        var lblDirTitle = new Label
        {
            Text = "Pasta de Destino da Suite",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(0, 8),
            AutoSize = true
        };

        var defaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Quilly Suite");

        _txtPath = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(0, 36),
            Size = new Size(470, 26),
            BackColor = card,
            ForeColor = text,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowse = new Button
        {
            Text = "Procurar…",
            Location = new Point(480, 35),
            Size = new Size(96, 28),
            BackColor = card,
            ForeColor = text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnBrowse.FlatAppearance.BorderColor = border;
        btnBrowse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = _txtPath.Text };
            if (fbd.ShowDialog(this) == DialogResult.OK) _txtPath.Text = fbd.SelectedPath;
        };

        var lblTasksTitle = new Label
        {
            Text = "Atalhos e Tarefas Adicionais",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(0, 96),
            AutoSize = true
        };

        var optionsPanel = new Panel
        {
            Location = new Point(0, 126),
            Size = new Size(576, 120),
            BackColor = card,
            Padding = new Padding(16)
        };
        optionsPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, optionsPanel.Width - 1, optionsPanel.Height - 1);
        };

        _chkDesktop = new CheckBox
        {
            Text = "Criar atalho do Quilly Suite na Área de Trabalho",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 18),
            ForeColor = text
        };

        _chkStartMenu = new CheckBox
        {
            Text = "Criar atalho no Menu Iniciar do Windows",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 50),
            ForeColor = text
        };

        optionsPanel.Controls.AddRange([_chkDesktop, _chkStartMenu]);
        _pageConfig.Controls.AddRange([lblDirTitle, _txtPath, btnBrowse, lblTasksTitle, optionsPanel]);

        // --- Step 2: Progress Page ---
        _pageProgress = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = bg };

        var lblProgressHeader = new Label
        {
            Text = "Instalando a Quilly Suite…",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(20, 50),
            AutoSize = true
        };

        _lblProgressStatus = new Label
        {
            Text = "Preparando arquivos da suite…",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = text,
            Location = new Point(22, 84),
            AutoSize = true
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(22, 116),
            Size = new Size(530, 22),
            Style = ProgressBarStyle.Continuous,
            Value = 10
        };

        _lblProgressDetail = new Label
        {
            Text = "Copiando binários do Quilly, Noty e Artry…",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = muted,
            Location = new Point(22, 146),
            AutoSize = true
        };

        _pageProgress.Controls.AddRange([lblProgressHeader, _lblProgressStatus, _progressBar, _lblProgressDetail]);

        // --- Step 3: Finish Page ---
        _pageFinish = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = bg };

        var finishCard = new Panel
        {
            Location = new Point(10, 20),
            Size = new Size(550, 240),
            BackColor = card,
            Padding = new Padding(24)
        };
        finishCard.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, finishCard.Width - 1, finishCard.Height - 1);
        };

        var lblSuccessBadge = new Label
        {
            Text = "✓",
            Font = new Font("Segoe UI", 28f, FontStyle.Bold),
            ForeColor = accent,
            Location = new Point(20, 24),
            Size = new Size(56, 56)
        };

        var lblFinishTitle = new Label
        {
            Text = "Quilly Suite Instalada com Sucesso!",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(80, 24),
            AutoSize = true
        };

        var lblFinishSub = new Label
        {
            Text = "Todos os arquivos e aplicativos da suíte foram preparados.\nVocê já pode abrir o Quilly para gerenciar e usar o Noty e o Artry.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = muted,
            Location = new Point(82, 60),
            Size = new Size(430, 48)
        };

        _chkLaunchNow = new CheckBox
        {
            Text = "Iniciar o Quilly Suite agora",
            Checked = true,
            AutoSize = true,
            Location = new Point(82, 130),
            ForeColor = text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };

        finishCard.Controls.AddRange([lblSuccessBadge, lblFinishTitle, lblFinishSub, _chkLaunchNow]);
        _pageFinish.Controls.Add(finishCard);

        _content.Controls.AddRange([_pageConfig, _pageProgress, _pageFinish]);
        Controls.AddRange([_content, _footer, _header]);
    }

    private async void OnActionClick(object? sender, EventArgs e)
    {
        if (_currentStep == 1)
        {
            _currentStep = 2;
            _pageConfig.Visible = false;
            _pageProgress.Visible = true;
            _btnAction.Enabled = false;
            _btnCancel.Enabled = false;

            var targetDir = _txtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
                targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Quilly Suite");

            var success = await Task.Run(() => InstallPayload(targetDir));

            _currentStep = 3;
            _pageProgress.Visible = false;
            _pageFinish.Visible = true;

            _btnAction.Text = "Concluir";
            _btnAction.Enabled = true;
            _btnCancel.Visible = false;
        }
        else if (_currentStep == 3)
        {
            if (_chkLaunchNow.Checked && File.Exists(_installedExePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_installedExePath) { UseShellExecute = true });
                }
                catch { }
            }
            Close();
        }
    }

    private bool InstallPayload(string targetDir)
    {
        try
        {
            UpdateProgress(10, "Criando diretórios da Suite…", targetDir);
            Directory.CreateDirectory(targetDir);

            _installedExePath = Path.Combine(targetDir, "Quilly.exe");

            var assembly = Assembly.GetExecutingAssembly();
            var resNames = assembly.GetManifestResourceNames();
            var payloadName = resNames.FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

            if (payloadName != null)
            {
                UpdateProgress(20, "Extraindo pacote completo da Quilly Suite…", "Descompactando Quilly, Noty e Artry...");
                using var stream = assembly.GetManifestResourceStream(payloadName);
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    var totalEntries = archive.Entries.Count;
                    var processed = 0;

                    foreach (var entry in archive.Entries)
                    {
                        var destPath = Path.Combine(targetDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                            entry.ExtractToFile(destPath, true);
                        }

                        processed++;
                        var pct = 20 + (int)((processed * 65.0) / totalEntries);
                        UpdateProgress(pct, $"Instalando {entry.Name}…", $"{processed} de {totalEntries} arquivos da suite");
                    }
                }
            }
            else
            {
                // Fallback para desenvolvimento local
                UpdateProgress(30, "Copiando arquivos da suíte…", "Modo local");
                var currentDir = AppContext.BaseDirectory;
                var quillyRoot = Path.GetFullPath(Path.Combine(currentDir, "..", ".."));
                if (!File.Exists(Path.Combine(quillyRoot, "Quilly.exe"))) quillyRoot = currentDir;

                var srcQuillyExe = Path.Combine(quillyRoot, "Quilly.exe");
                if (File.Exists(srcQuillyExe)) File.Copy(srcQuillyExe, _installedExePath, true);

                var srcUi = Path.Combine(quillyRoot, "ui");
                if (!Directory.Exists(srcUi)) srcUi = Path.Combine(quillyRoot, "src", "ui");
                if (Directory.Exists(srcUi)) CopyDirectory(srcUi, Path.Combine(targetDir, "ui"));

                var srcSourceApps = Path.Combine(quillyRoot, "source_apps");
                if (Directory.Exists(srcSourceApps)) CopyDirectory(srcSourceApps, Path.Combine(targetDir, "source_apps"));
            }

            Directory.CreateDirectory(Path.Combine(targetDir, "Suite"));

            // Atalhos
            UpdateProgress(90, "Configurando atalhos do sistema…", "Área de Trabalho e Menu Iniciar");
            if (_chkDesktop.Checked)
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktop, "Quilly Suite.lnk"), _installedExePath, targetDir);
            }

            if (_chkStartMenu.Checked)
            {
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                CreateShortcut(Path.Combine(startMenu, "Quilly Suite.lnk"), _installedExePath, targetDir);
            }

            // Registra ícone oficial do formato .hyp no sistema
            var hypIcoPath = Path.Combine(targetDir, "hyp.ico");
            RegisterHypAssociation(_installedExePath, hypIcoPath);

            UpdateProgress(100, "Instalação concluída com sucesso!", "Pronto para usar.");
            return true;
        }
        catch (Exception ex)
        {
            UpdateProgress(100, "Aviso durante instalação: " + ex.Message, "Continuando...");
            return false;
        }
    }

    private void UpdateProgress(int val, string status, string detail)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProgress(val, status, detail));
            return;
        }
        _progressBar.Value = Math.Clamp(val, 0, 100);
        _lblProgressStatus.Text = status;
        _lblProgressDetail.Text = detail;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSub);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
    {
        try
        {
            var psCmd = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetPath}'; $s.WorkingDirectory = '{workingDir}'; $s.Save()";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"{psCmd}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit();
        }
        catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static void RegisterHypAssociation(string quillyExe, string iconPath)
    {
        try
        {
            using (var progIdKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Hyp.Document"))
            {
                progIdKey.SetValue("", "Documento Hyper (.hyp)");
                progIdKey.SetValue("FriendlyTypeName", "Documento Hyper (.hyp)");

                if (File.Exists(iconPath))
                {
                    using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                    iconKey.SetValue("", iconPath);
                }

                using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{quillyExe}\" \"%1\"");

                using var openKey = progIdKey.CreateSubKey(@"shell\open");
                openKey.SetValue("FriendlyAppName", "Quilly Suite");
            }

            using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.hyp"))
            {
                if (File.Exists(iconPath))
                {
                    using var defIconKey = extKey.CreateSubKey("DefaultIcon");
                    defIconKey.SetValue("", iconPath);
                }

                using var openWith = extKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue("Hyp.Document", Array.Empty<byte>(), RegistryValueKind.None);

                var current = extKey.GetValue("") as string;
                if (string.IsNullOrEmpty(current) || current == "Hyp.Document")
                {
                    extKey.SetValue("", "Hyp.Document");
                }
            }

            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
