using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;
using WDBCommon.Updates;

namespace WiFiDBUploader
{
    /// <summary>Offers a newer release. Installed copies update in place; a portable copy is sent to the release page.</summary>
    public sealed class UpdateForm : Form
    {
        private readonly HttpClient _http;
        private readonly ReleaseInfo _release;
        private readonly ReleaseAsset _installer;
        private readonly Func<bool> _uploadRunning;
        private readonly CancellationTokenSource _download = new CancellationTokenSource();
        private readonly Button _install = new Button { Text = "Install Now", AutoSize = true };
        private readonly Button _page = new Button { Text = "Release Page", AutoSize = true };
        private readonly Button _later = new Button { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 16, Visible = false };

        /// <summary>The installer is running and waiting for this app to exit.</summary>
        public bool InstallStarted { get; private set; }

        public UpdateForm(HttpClient http, UpdateCheckResult result, Func<bool> uploadRunning)
        {
            _http = http;
            _release = result.Update;
            _uploadRunning = uploadRunning;
            if (WindowsUpdateInstaller.IsInstalledCopy) _installer = _release.FindAsset(WindowsUpdateInstaller.InstallerSuffix);

            Text = "WDB Uploader update";
            ClientSize = new Size(560, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            CancelButton = _later;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(8),
                Text = $"Version {_release.Version} is available (you have {result.CurrentVersion}).\n" +
                       (_installer != null ? "Install Now downloads it, closes the uploader and starts the new version."
                                           : "This copy can't update itself. Get the new version from the release page."),
            };
            var notes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = (_release.Notes ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4) };
            buttons.Controls.Add(_later);
            buttons.Controls.Add(_page);
            if (_installer != null) buttons.Controls.Add(_install);

            Controls.Add(notes);
            Controls.Add(header);
            Controls.Add(_progress);
            Controls.Add(buttons);

            _install.Click += Install_Click;
            _page.Click += (s, e) => OpenReleasePage();
            FormClosed += (s, e) => _download.Cancel();
        }

        private void OpenReleasePage()
        {
            if (!Uri.TryCreate(_release.PageUrl, UriKind.Absolute, out Uri page) || page.Scheme != Uri.UriSchemeHttps) return;
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
        }

        private async void Install_Click(object sender, EventArgs e)
        {
            if (_uploadRunning())
            {
                MessageBox.Show(this, "An upload is running. Let it finish, then install the update.");
                return;
            }

            _install.Enabled = false;
            _progress.Visible = true;
            try
            {
                var progress = new Progress<double>(fraction => _progress.Value = Math.Clamp((int)(fraction * 100), 0, 100));
                string path = await WindowsUpdateInstaller.DownloadAsync(_http, _installer, "WiFiDBUploader", progress, _download.Token);
                WindowsUpdateInstaller.VerifyPublisher(path);
                if (WindowsUpdateInstaller.Launch(path))
                {
                    InstallStarted = true;
                    DialogResult = DialogResult.OK;
                    return;
                }
                MessageBox.Show(this, "The update needs administrator permission to install.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "The update couldn't be installed: " + ex.Message);
            }
            _install.Enabled = true;
            _progress.Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _download.Dispose();
            base.Dispose(disposing);
        }
    }
}
