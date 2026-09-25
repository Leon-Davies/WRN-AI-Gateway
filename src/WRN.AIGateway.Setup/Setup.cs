using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WRN.AIGateway.Setup
{
    internal sealed class SetupForm : Form
    {
        private readonly Button _installButton;
        private readonly Label _status;
        private readonly ProgressBar _progress;

        public SetupForm()
        {
            Text = "WRN AI Gateway Setup";
            Width = 560;
            Height = 380;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 245, 248);
            Font = new Font("Segoe UI", 9F);

            var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Color.FromArgb(75, 20, 100) };
            Controls.Add(header);

            var brand = new Label {
                Text = "W", ForeColor = Color.FromArgb(75, 20, 100), BackColor = Color.White,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
                Width = 38, Height = 38, Left = 24, Top = 25
            };
            header.Controls.Add(brand);

            header.Controls.Add(new Label {
                Text = "WRN AI Gateway", ForeColor = Color.White, Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                AutoSize = true, Left = 76, Top = 23
            });
            header.Controls.Add(new Label {
                Text = "Simple model access for Willis Research Network", ForeColor = Color.FromArgb(229, 215, 235),
                Font = new Font("Segoe UI", 9F), AutoSize = true, Left = 78, Top = 54
            });

            Controls.Add(new Label {
                Text = "Install WRN AI Gateway", ForeColor = Color.FromArgb(43, 35, 48),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold), AutoSize = true, Left = 28, Top = 112
            });
            Controls.Add(new Label {
                Text = "This installs WRN AI Gateway for your Windows account only.\r\nNo administrator rights are required and your Claude history is not changed.",
                ForeColor = Color.FromArgb(96, 87, 101), Font = new Font("Segoe UI", 9.5F),
                AutoSize = true, Left = 30, Top = 148
            });

            _progress = new ProgressBar {
                Left = 30, Top = 211, Width = 486, Height = 8,
                Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0
            };
            Controls.Add(_progress);

            _status = new Label {
                Text = "Ready to install", ForeColor = Color.FromArgb(117, 107, 121),
                AutoSize = true, Left = 30, Top = 230
            };
            Controls.Add(_status);

            _installButton = new Button {
                Text = "Install", Width = 116, Height = 40, Left = 400, Top = 278,
                BackColor = Color.FromArgb(82, 24, 109), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Cursor = Cursors.Hand
            };
            _installButton.FlatAppearance.BorderSize = 0;
            _installButton.Click += InstallButtonClick;
            Controls.Add(_installButton);

            Controls.Add(new Label {
                Text = "Phase 1 preview", ForeColor = Color.FromArgb(128, 116, 133),
                AutoSize = true, Left = 30, Top = 292
            });
        }

        private void InstallButtonClick(object sender, EventArgs e)
        {
            _installButton.Enabled = false;
            try { Install(); }
            catch (Exception ex)
            {
                _status.Text = "Installation failed";
                MessageBox.Show(
                    "WRN AI Gateway could not be installed.\r\n\r\n" + ex.Message,
                    "WRN AI Gateway Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _installButton.Enabled = true;
            }
        }

        private void Install()
        {
            var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            var sourceExe = Path.Combine(source, "WRN-AI-Gateway.exe");
            if (!File.Exists(sourceExe))
                throw new InvalidOperationException("The application files are missing from this setup package.");

            var installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WRN-AI-Gateway");
            var current = Path.Combine(installRoot, "current");
            var staging = Path.Combine(installRoot, "staging-" + Guid.NewGuid().ToString("N"));

            var running = Process.GetProcessesByName("WRN-AI-Gateway");
            if (running.Length > 0)
                throw new InvalidOperationException("WRN AI Gateway is currently open. Close it, then click Install again.");

            _status.Text = "Preparing installation...";
            _progress.Value = 15;
            Application.DoEvents();
            Directory.CreateDirectory(installRoot);
            CopyDirectory(source, staging);

            _status.Text = "Installing application...";
            _progress.Value = 55;
            Application.DoEvents();

            var previous = Path.Combine(installRoot, "previous");
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
            if (Directory.Exists(current)) Directory.Move(current, previous);
            Directory.Move(staging, current);

            _status.Text = "Creating shortcuts...";
            _progress.Value = 80;
            Application.DoEvents();
            CreateShortcuts(Path.Combine(current, "WRN-AI-Gateway.exe"));

            _progress.Value = 100;
            _status.Text = "Installed successfully";
            _installButton.Text = "Open";
            _installButton.Enabled = true;
            _installButton.Click -= InstallButtonClick;
            _installButton.Click += delegate { Launch(Path.Combine(current, "WRN-AI-Gateway.exe")); };
            Launch(Path.Combine(current, "WRN-AI-Gateway.exe"));
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static void CreateShortcuts(string target)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);

            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var programs = Path.Combine(startMenu, "Programs");
            Directory.CreateDirectory(programs);
            CreateShortcut(shell, Path.Combine(programs, "WRN AI Gateway.lnk"), target);

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
                CreateShortcut(shell, Path.Combine(desktop, "WRN AI Gateway.lnk"), target);

            Marshal.FinalReleaseComObject(shell);
        }

        private static void CreateShortcut(dynamic shell, string path, string target)
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.Description = "WRN AI Gateway";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }

        private static void Launch(string target)
        {
            Process.Start(new ProcessStartInfo {
                FileName = target, WorkingDirectory = Path.GetDirectoryName(target), UseShellExecute = true
            });
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
