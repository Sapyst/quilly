using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using Artry;
using Microsoft.Win32;

namespace ArtryInstaller;

internal sealed class SetupForm : Form
{
    private readonly Panel _header;
    private readonly Panel _footer;
    private readonly Panel _content;

    private readonly Panel _pageConfig;
    private readonly Panel _pageProgress;
    private readonly Panel _pageFinish;

    private readonly TextBox _txtPath;
    private readonly CheckBox _chkDesktop;
    private readonly CheckBox _chkStartMenu;
    private readonly CheckBox _chkCommand;
    private readonly CheckBox _chkFileAssoc;
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
        return false;
    }

    public SetupForm()
    {
        var isDark = IsSystemDark();
        var bg = isDark ? Color.FromArgb(24, 26, 30) : Color.FromArgb(245, 244, 240);
        var surface = isDark ? Color.FromArgb(36, 40, 48) : Color.FromArgb(255, 254, 250);
        var card = isDark ? Color.FromArgb(42, 46, 54) : Color.FromArgb(250, 249, 245);
        var text = isDark ? Color.FromArgb(242, 245, 248) : Color.FromArgb(28, 34, 40);
        var muted = isDark ? Color.FromArgb(156, 163, 175) : Color.FromArgb(100, 110, 118);
        var border = isDark ? Color.FromArgb(64, 70, 80) : Color.FromArgb(218, 222, 225);
        var accent = Color.FromArgb(255, 222, 0); // Artry Yellow
        var accentText = Color.FromArgb(18, 20, 24); // High contrast jet black

        Text = "Instalação do Artry";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 520);
        MinimumSize = new Size(640, 520);
        MaximumSize = new Size(640, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.25f);
        BackColor = bg;
        ForeColor = text;

        try { Icon = IconArt.GetAppIcon(); } catch { }

        // Header
        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = surface,
            Padding = new Padding(24, 16, 24, 14)
        };
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        var picIcon = new PictureBox
        {
            Size = new Size(68, 68),
            Location = new Point(24, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconArt.Draw(128),
            BackColor = Color.Transparent
        };

        var lblHeaderTitle = new Label
        {
            Text = "Instalação do Artry",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(106, 22),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var lblHeaderSub = new Label
        {
            Text = "Editor de texto minimalista, rápido e elegante para Windows.",
            ForeColor = muted,
            Location = new Point(108, 52),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        _header.Controls.AddRange([picIcon, lblHeaderTitle, lblHeaderSub]);

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
            Width = 110,
            Height = 36,
            Location = new Point(_footer.Width - 130, 14),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            BackColor = accent,
            ForeColor = accentText,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnAction.FlatAppearance.BorderSize = 0;
        _btnAction.Click += OnActionClick;

        _btnCancel = new Button
        {
            Text = "Cancelar",
            Width = 96,
            Height = 36,
            Location = new Point(_footer.Width - 236, 14),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            BackColor = card,
            ForeColor = text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderColor = border;
        _btnCancel.Click += (_, _) => Close();

        _footer.Controls.AddRange([_btnAction, _btnCancel]);

        // Content Area
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 16),
            BackColor = bg
        };

        // --- Step 1: Configuration Page ---
        _pageConfig = new Panel { Dock = DockStyle.Fill, BackColor = bg };

        var lblDirTitle = new Label
        {
            Text = "Pasta de Destino",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(0, 4),
            AutoSize = true
        };

        var defaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Artry");

        _txtPath = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(2, 32),
            Width = 460,
            Height = 28,
            Font = new Font("Segoe UI", 9.5f),
            BackColor = card,
            ForeColor = text,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowse = new Button
        {
            Text = "Procurar…",
            Location = new Point(472, 30),
            Width = 96,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = card,
            ForeColor = text,
            Cursor = Cursors.Hand
        };
        btnBrowse.FlatAppearance.BorderColor = border;
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = _txtPath.Text, Description = "Selecione a pasta de instalação do Artry" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtPath.Text = dlg.SelectedPath;
        };

        var lblSpace = new Label
        {
            Text = GetSpaceInfoText(defaultInstallDir),
            ForeColor = muted,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(2, 66),
            AutoSize = true
        };

        var lblTasksTitle = new Label
        {
            Text = "Opções e Integração com o Windows",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(0, 104),
            AutoSize = true
        };

        var optionsPanel = new Panel
        {
            Location = new Point(2, 132),
            Size = new Size(566, 150),
            BackColor = card,
            Padding = new Padding(16, 12, 16, 12)
        };
        optionsPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, optionsPanel.Width - 1, optionsPanel.Height - 1);
        };

        _chkDesktop = new CheckBox
        {
            Text = "Criar atalho na Área de Trabalho (Desktop)",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 14),
            ForeColor = text
        };

        _chkStartMenu = new CheckBox
        {
            Text = "Criar atalho no Menu Iniciar do Windows",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 42),
            ForeColor = text
        };

        _chkCommand = new CheckBox
        {
            Text = "Habilitar comando global 'artry' (Executar via Win + R ou terminal)",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 70),
            ForeColor = text
        };

        _chkFileAssoc = new CheckBox
        {
            Text = "Associar como editor de textos (.artry, .txt, .md, .rtf, .docx, .log)",
            Checked = true,
            AutoSize = true,
            Location = new Point(16, 98),
            ForeColor = text
        };

        optionsPanel.Controls.AddRange([_chkDesktop, _chkStartMenu, _chkCommand, _chkFileAssoc]);

        _pageConfig.Controls.AddRange([lblDirTitle, _txtPath, btnBrowse, lblSpace, lblTasksTitle, optionsPanel]);

        // --- Step 2: Progress Page ---
        _pageProgress = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = bg };

        var lblProgressHeader = new Label
        {
            Text = "Instalando o Artry…",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(20, 60),
            AutoSize = true
        };

        _lblProgressStatus = new Label
        {
            Text = "Preparando arquivos para instalação…",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = text,
            Location = new Point(22, 94),
            AutoSize = true
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(22, 126),
            Size = new Size(530, 22),
            Style = ProgressBarStyle.Continuous,
            Value = 10
        };

        _lblProgressDetail = new Label
        {
            Text = "Por favor, aguarde enquanto o instalador configura o aplicativo.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = muted,
            Location = new Point(22, 156),
            AutoSize = true
        };

        _pageProgress.Controls.AddRange([lblProgressHeader, _lblProgressStatus, _progressBar, _lblProgressDetail]);

        // --- Step 3: Finish Page ---
        _pageFinish = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = bg };

        var finishCard = new Panel
        {
            Location = new Point(10, 20),
            Size = new Size(550, 250),
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
            Size = new Size(56, 56),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        var lblFinishTitle = new Label
        {
            Text = "Instalação concluída com sucesso!",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = text,
            Location = new Point(88, 28),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var lblFinishDesc = new Label
        {
            Text = "O Artry foi instalado e configurado no seu sistema.\nTodos os atalhos e integrações foram preparados.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = muted,
            Location = new Point(90, 60),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        _chkLaunchNow = new CheckBox
        {
            Text = "Iniciar o Artry agora",
            Checked = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = isDark ? accent : Color.FromArgb(30, 30, 30),
            Location = new Point(90, 120),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        finishCard.Controls.AddRange([lblSuccessBadge, lblFinishTitle, lblFinishDesc, _chkLaunchNow]);
        _pageFinish.Controls.Add(finishCard);

        _content.Controls.AddRange([_pageConfig, _pageProgress, _pageFinish]);

        Controls.Add(_content);
        Controls.Add(_footer);
        Controls.Add(_header);

        AcceptButton = _btnAction;
    }

    private static string GetSpaceInfoText(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                return $"Espaço em disco necessário: ~120 MB  |  Espaço disponível: {freeGb:F1} GB";
            }
        }
        catch { }
        return "Espaço em disco necessário: ~120 MB";
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
                targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Artry");

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
            UpdateProgress(15, "Criando diretórios de instalação…", targetDir);
            Directory.CreateDirectory(targetDir);

            _installedExePath = Path.Combine(targetDir, "Artry.exe");

            UpdateProgress(35, "Extraindo binário do Artry…", "Copiando Artry.exe para a pasta de destino");

            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames();
            var payloadName = names.FirstOrDefault(n => n.EndsWith("Artry.exe", StringComparison.OrdinalIgnoreCase));

            if (payloadName != null)
            {
                using var stream = assembly.GetManifestResourceStream(payloadName);
                if (stream != null)
                {
                    using var fileStream = File.Create(_installedExePath);
                    stream.CopyTo(fileStream);
                }
            }
            else
            {
                var localExe = Path.Combine(AppContext.BaseDirectory, "Artry.exe");
                if (!File.Exists(localExe)) localExe = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Artry.exe");
                if (File.Exists(localExe))
                {
                    File.Copy(localExe, _installedExePath, true);
                }
            }

            try
            {
                using var bmp = IconArt.Draw(256);
                bmp.Save(Path.Combine(targetDir, "icon.png"), ImageFormat.Png);
            }
            catch { }

            UpdateProgress(65, "Configurando atalhos do usuário…", "Criando atalhos");

            if (_chkDesktop.Checked)
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                FileAssociationService.CreateShortcut(_installedExePath, Path.Combine(desktop, "Artry.lnk"), "Artry — Editor de Texto");
            }

            if (_chkStartMenu.Checked)
            {
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                FileAssociationService.CreateShortcut(_installedExePath, Path.Combine(startMenu, "Artry.lnk"), "Artry — Editor de Texto");
            }

            UpdateProgress(85, "Registrando no sistema operacional…", "Registrando em HKCU e Default Apps");

            if (_chkCommand.Checked || _chkFileAssoc.Checked)
            {
                var exts = _chkFileAssoc.Checked ? FileAssociationService.TextExtensions : [];
                FileAssociationService.RegisterApp(exts);
            }

            FileAssociationService.RegisterUninstallEntry(targetDir, _installedExePath);

            UpdateProgress(100, "Finalizando instalação…", "Pronto!");
            Thread.Sleep(300);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro na instalação: {ex}");
            return false;
        }
    }

    private void UpdateProgress(int value, string status, string detail)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProgress(value, status, detail));
            return;
        }
        _progressBar.Value = Math.Clamp(value, 0, 100);
        _lblProgressStatus.Text = status;
        _lblProgressDetail.Text = detail;
    }
}
