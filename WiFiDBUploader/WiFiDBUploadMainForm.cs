using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WDBCommon;
using WDBCommon.Updates;
using WDBSQLite;

namespace WiFiDBUploader
{
    public partial class WiFiDBUploadMainForm : Form
    {
        // listView1's columns, in the designer's order
        private const int ColId = 0, ColUser = 1, ColTitle = 2, ColDate = 3, ColSize = 4, ColFile = 5, ColFormat = 6, ColHash = 7, ColStatus = 8, ColMessage = 9;

        private const string GitHubRepo = "acalcutt/WiFiDBClient";

        /// <summary>The backup update feed on gitlab.techidiots.net. Set it to the project's path once the project exists (see README.md).</summary>
        private const string GitLabProject = null;

        private static readonly HttpClient UpdateHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly Dictionary<string, ListViewItem> _rows = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Progress<ImportRow> _rowProgress;

        private UploaderSettings _settings;
        private WDBTraceLog.TraceLog _log;
        private WDBSQLite.WDBSQLite _db;
        private WDBAPI.WDBAPI _api;
        private WDBCommon.WDBCommon _common;

        private System.Windows.Forms.Timer _statusTimer;
        private System.Windows.Forms.Timer _daemonTimer;
        private System.Windows.Forms.Timer _autoImportTimer;

        private bool _refreshingStatuses;
        private bool _refreshingDaemons;
        private bool _autoImporting;
        private int _importsRunning;
        private bool _saveDbOnExit;
        private bool _shutDown;

        public WiFiDBUploadMainForm()
        {
            _rowProgress = new Progress<ImportRow>(ShowRow);
            _settings = UploaderSettings.Load();
            _log = new WDBTraceLog.TraceLog(_settings.LogFolder, _settings.TraceLogEnable, _settings.PerRunRotate);
            _db = new WDBSQLite.WDBSQLite(_settings.DatabaseFile);
            _api = new WDBAPI.WDBAPI(_log);
            _common = new WDBCommon.WDBCommon(_db, _api, _log);
            ApplyServiceSettings();

            InitializeComponent();
            Text = $"WDB Uploader v{UploaderSettings.Version} - WifiDB.net";
            AddUpdateMenuItems();
            foreach (ImportRow row in _db.GetImportRows())
            {
                ShowRow(row);
            }
            RestartTimers();
            Shown += WiFiDBUploadMainForm_Shown;
        }

        private async void WiFiDBUploadMainForm_Shown(object sender, EventArgs e)
        {
            // An upload-and-close run is unattended: a question would stop it from closing.
            bool unattended = _settings.AutoUploadFolder && _settings.AutoCloseEnable;
            if (_settings.AutoCheckForUpdates && !unattended) _ = CheckForUpdatesAsync(manual: false);

            if (_settings.SelectedServer == null)
            {
                MessageBox.Show(this, "There is no selected server. Go to Settings -> WiFiDB Settings. Select a server from the drop down, or add one with the +.");
                return;
            }
            _ = RefreshDaemonsAsync();
            if (!_settings.AutoUploadFolder) return;

            await RunAutoImportAsync();
            if (_settings.AutoCloseEnable && !_closing.IsCancellationRequested)
            {
                using var form = new AutoCloseTimer { TimerSeconds = _settings.AutoCloseTimerSeconds.ToString() };
                form.ShowDialog(this);
            }
        }

        private void ApplyServiceSettings()
        {
            ServerObj server = _settings.SelectedServer;
            _api.ApiBase = server == null ? "" : WDBAPI.WDBAPI.CombineApiUrl(server.ServerAddress, server.ApiPath);
            _api.Username = server?.Username ?? "";
            _api.ApiKey = server?.ApiKey ?? "";
            _common.Username = _api.Username;
            _common.ApiKey = _api.ApiKey;
            _common.ArchiveImports = _settings.ArchiveImports;
            _common.ArchiveImportsFolderPath = _settings.ArchiveImportsFolderPath;
        }

        private void RestartTimers()
        {
            bool hasServer = _settings.SelectedServer != null;
            RestartTimer(ref _statusTimer, hasServer && _settings.ImportUpdateThreadEnable, _settings.ImportUpdateThreadSeconds, StatusTimer_Tick);
            RestartTimer(ref _daemonTimer, hasServer && _settings.DaemonUpdateThreadEnable, _settings.DaemonUpdateThreadSeconds, DaemonTimer_Tick);
            RestartTimer(ref _autoImportTimer, hasServer && _settings.AutoImportThreadEnable, _settings.AutoImportThreadSeconds, AutoImportTimer_Tick);
        }

        private static void RestartTimer(ref System.Windows.Forms.Timer timer, bool enabled, int seconds, EventHandler tick)
        {
            timer?.Stop();
            timer?.Dispose();
            timer = null;
            if (!enabled) return;
            timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, seconds) * 1000 };
            timer.Tick += tick;
            timer.Start();
        }

        private void SettingsChanged()
        {
            _settings.Save();
            ApplyServiceSettings();
            RestartTimers();
        }

        //
        // The list
        //

        private void ShowRow(ImportRow row)
        {
            if (!_rows.TryGetValue(row.FileHash, out ListViewItem item))
            {
                item = new ListViewItem(new string[ColMessage + 1]) { Tag = row };
                _rows[row.FileHash] = item;
                listView1.Items.Add(item);
                item.EnsureVisible();
            }
            item.Tag = row;
            item.SubItems[ColId].Text = row.ImportID == 0 ? "" : row.ImportID.ToString();
            item.SubItems[ColUser].Text = row.Username;
            item.SubItems[ColTitle].Text = row.ImportTitle;
            item.SubItems[ColDate].Text = row.DateTime;
            item.SubItems[ColSize].Text = row.FileSize;
            item.SubItems[ColFile].Text = row.FileName;
            item.SubItems[ColFormat].Text = row.Format;
            item.SubItems[ColHash].Text = row.FileHash;
            item.SubItems[ColStatus].Text = row.Status;
            item.SubItems[ColMessage].Text = row.Message;
        }

        private void SetBusy(int change, string message = null)
        {
            _importsRunning += change;
            bool busy = _importsRunning > 0;
            toolStripProgressBar1.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (message != null) toolStripStatusLabel1.Text = message;
            else if (!busy) toolStripStatusLabel1.Text = "";
        }

        //
        // Importing
        //

        /// <summary>Runs an import, showing what went wrong in the status bar rather than letting it end the app.</summary>
        private async Task RunImportAsync(Func<CancellationToken, Task> import, string message)
        {
            SetBusy(1, message);
            string failure = null;
            try
            {
                await import(_closing.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Write("Main", "Import failed: " + ex);
                failure = "Import failed: " + ex.Message;
            }
            SetBusy(-1, failure);
        }

        private Task RunAutoImportAsync()
        {
            if (_autoImporting || _settings.SelectedServer == null || !_settings.AutoUploadFolder) return Task.CompletedTask;
            if (!System.IO.Directory.Exists(_settings.AutoUploadFolderPath))
            {
                toolStripStatusLabel1.Text = "The auto upload folder wasn't found: " + _settings.AutoUploadFolderPath;
                return Task.CompletedTask;
            }

            _autoImporting = true;
            (string title, string notes) = DefaultImportValues();
            return RunAutoImportCoreAsync(title, notes);
        }

        private async Task RunAutoImportCoreAsync(string title, string notes)
        {
            try
            {
                await RunImportAsync(ct => _common.ImportFolderAsync(_settings.AutoUploadFolderPath, title, notes, ImportMode.Automatic, _rowProgress, ct),
                    "Uploading from the auto upload folder...");
            }
            finally
            {
                _autoImporting = false;
            }
        }

        private (string Title, string Notes) DefaultImportValues()
        {
            string title = _settings.UseAutoDateTimeTitle ? "%DATETIME%" : _settings.DefaultImportTitle;
            return (ExpandDate(title), ExpandDate(_settings.DefaultImportNotes));
        }

        private static string ExpandDate(string text) => (text ?? "").Replace("%DATETIME%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        /// <summary>The title and notes for an import the user started. False when they backed out of the dialog.</summary>
        private bool TryGetImportValues(out string title, out string notes)
        {
            if (_settings.UseDefaultImportValues)
            {
                (title, notes) = DefaultImportValues();
                return true;
            }

            using var details = new ImportDetails();
            if (details.ShowDialog(this) != DialogResult.OK)
            {
                title = notes = null;
                return false;
            }
            title = ExpandDate(details.ImportTitle);
            notes = ExpandDate(details.ImportNotes);
            return true;
        }

        private bool ServerIsSelected()
        {
            if (_settings.SelectedServer != null) return true;
            MessageBox.Show(this, "There is no selected server. Go to Settings -> WiFiDB Settings. Select a server from the drop down, or add one with the +.");
            return false;
        }

        private async void importFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ServerIsSelected()) return;
            using var dialog = new OpenFileDialog
            {
                Filter = ImportFileType.OpenFileFilter,
                Multiselect = true,
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!TryGetImportValues(out string title, out string notes)) return;

            string[] files = dialog.FileNames;
            await RunImportAsync(async ct =>
            {
                foreach (string file in files)
                {
                    await _common.ImportFileAsync(file, title, notes, ImportMode.Manual, _rowProgress, ct);
                }
            }, files.Length == 1 ? "Uploading file..." : $"Uploading {files.Length} files...");
        }

        private async void importFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ServerIsSelected()) return;
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!TryGetImportValues(out string title, out string notes)) return;

            string folder = dialog.SelectedPath;
            await RunImportAsync(ct => _common.ImportFolderAsync(folder, title, notes, ImportMode.Manual, _rowProgress, ct), "Uploading folder...");
        }

        //
        // Timers
        //

        private async void AutoImportTimer_Tick(object sender, EventArgs e) => await RunAutoImportAsync();

        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (_refreshingStatuses) return;
            List<ImportRow> pending = _rows.Values.Select(i => (ImportRow)i.Tag).Where(r => ImportStatus.IsPending(r.Status)).ToList();
            if (pending.Count == 0) return;

            _refreshingStatuses = true;
            try
            {
                string error = await _common.RefreshStatusesAsync(pending, _rowProgress, _closing.Token);
                if (error != null) toolStripStatusLabel1.Text = error;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Write("Main", "Status refresh failed: " + ex);
            }
            finally
            {
                _refreshingStatuses = false;
            }
        }

        private async void DaemonTimer_Tick(object sender, EventArgs e) => await RefreshDaemonsAsync();

        private async Task RefreshDaemonsAsync()
        {
            if (_refreshingDaemons || _settings.SelectedServer == null) return;
            _refreshingDaemons = true;
            try
            {
                WDBAPI.DaemonList result = await _api.GetDaemonsAsync(_closing.Token);
                if (result.Error != null)
                {
                    _log.Write("Main", "Daemon list: " + result.Error);
                    return;
                }

                listView2.BeginUpdate();
                listView2.Items.Clear();
                foreach (WDBAPI.DaemonInfo d in result.Daemons)
                {
                    listView2.Items.Add(new ListViewItem(new[] { d.NodeName, d.PidFile, d.Pid, d.RunTime, d.Memory, d.Command, d.Date }));
                }
                listView2.EndUpdate();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _refreshingDaemons = false;
            }
        }

        //
        // Settings
        //

        private void wifidbSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new WiFiDB_Settings { ServerList = _settings.ServerList };
            form.InitForm();
            if (form.ShowDialog(this) != DialogResult.OK) return;

            _settings.ServerList = form.ServerList;
            foreach (ServerObj server in _settings.ServerList)
            {
                server.Selected = server.Name == form.SelectedServer;
            }
            SettingsChanged();
        }

        private void importSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new Import_Settings
            {
                ImportNotes = _settings.DefaultImportNotes,
                ImportTitle = _settings.DefaultImportTitle,
                UseImportDefaultValues = _settings.UseDefaultImportValues,
                UseAutoDateTimeTitle = _settings.UseAutoDateTimeTitle,
            };
            if (form.ShowDialog(this) != DialogResult.OK) return;

            _settings.UseAutoDateTimeTitle = form.UseAutoDateTimeTitle;
            _settings.DefaultImportTitle = form.ImportTitle;
            _settings.DefaultImportNotes = form.ImportNotes;
            _settings.UseDefaultImportValues = form.UseImportDefaultValues;
            SettingsChanged();
        }

        private void autoSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new Auto_Upload_Settings
            {
                AutoUploadFolder = _settings.AutoUploadFolder,
                AutoUploadFolderPath = _settings.AutoUploadFolderPath,
                ArchiveImports = _settings.ArchiveImports,
                ArchiveImportsFolderPath = _settings.ArchiveImportsFolderPath,
                AutoCloseTimerSeconds = _settings.AutoCloseTimerSeconds.ToString(),
                AutoCloseEnable = _settings.AutoCloseEnable,
            };
            if (form.ShowDialog(this) != DialogResult.OK) return;

            _settings.AutoUploadFolder = form.AutoUploadFolder;
            _settings.AutoUploadFolderPath = form.AutoUploadFolderPath;
            _settings.ArchiveImports = form.ArchiveImports;
            _settings.ArchiveImportsFolderPath = form.ArchiveImportsFolderPath;
            _settings.AutoCloseTimerSeconds = ParseSeconds(form.AutoCloseTimerSeconds, _settings.AutoCloseTimerSeconds);
            _settings.AutoCloseEnable = form.AutoCloseEnable;
            SettingsChanged();
        }

        private void backgroundThreadSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new BGThreadsSettings
            {
                DaemonUpdateThreadSeconds = _settings.DaemonUpdateThreadSeconds.ToString(),
                ImportUpdateThreadSeconds = _settings.ImportUpdateThreadSeconds.ToString(),
                AutoImportThreadSeconds = _settings.AutoImportThreadSeconds.ToString(),
                ImportUpdateThreadEnable = _settings.ImportUpdateThreadEnable,
                DaemonUpdateThreadEnable = _settings.DaemonUpdateThreadEnable,
                AutoImportThreadEnable = _settings.AutoImportThreadEnable,
            };
            if (form.ShowDialog(this) != DialogResult.OK) return;

            _settings.DaemonUpdateThreadSeconds = ParseSeconds(form.DaemonUpdateThreadSeconds, _settings.DaemonUpdateThreadSeconds);
            _settings.ImportUpdateThreadSeconds = ParseSeconds(form.ImportUpdateThreadSeconds, _settings.ImportUpdateThreadSeconds);
            _settings.AutoImportThreadSeconds = ParseSeconds(form.AutoImportThreadSeconds, _settings.AutoImportThreadSeconds);
            _settings.ImportUpdateThreadEnable = form.ImportUpdateThreadEnable;
            _settings.DaemonUpdateThreadEnable = form.DaemonUpdateThreadEnable;
            _settings.AutoImportThreadEnable = form.AutoImportThreadEnable;
            SettingsChanged();
        }

        private void loggingSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new LoggingSettings
            {
                TraceLogEnable = _settings.TraceLogEnable,
                DEBUG = _settings.DEBUG,
                PerRunRotate = _settings.PerRunRotate,
            };
            if (form.ShowDialog(this) != DialogResult.OK) return;

            bool reopenLog = _settings.TraceLogEnable != form.TraceLogEnable || _settings.PerRunRotate != form.PerRunRotate;
            _settings.TraceLogEnable = form.TraceLogEnable;
            _settings.DEBUG = form.DEBUG;
            _settings.PerRunRotate = form.PerRunRotate;
            _settings.Save();
            if (reopenLog) toolStripStatusLabel1.Text = "The logging change takes effect the next time the uploader starts.";
        }

        private static int ParseSeconds(string text, int fallback) => int.TryParse(text, out int value) && value > 0 ? value : fallback;

        //
        // Updates
        //

        private void AddUpdateMenuItems()
        {
            fileToolStripMenuItem.DropDownItems.Insert(2, new ToolStripMenuItem("Check For Updates", null, async (s, e) => await CheckForUpdatesAsync(manual: true)));

            var atStartup = new ToolStripMenuItem("Check For Updates At Startup") { CheckOnClick = true, Checked = _settings.AutoCheckForUpdates };
            atStartup.Click += (s, e) => { _settings.AutoCheckForUpdates = atStartup.Checked; _settings.Save(); };
            var prereleases = new ToolStripMenuItem("Include Pre-Releases In Updates") { CheckOnClick = true, Checked = _settings.IncludePrereleaseUpdates };
            prereleases.Click += (s, e) => { _settings.IncludePrereleaseUpdates = prereleases.Checked; _settings.Save(); };
            settingsToolStripMenuItem.DropDownItems.Add(atStartup);
            settingsToolStripMenuItem.DropDownItems.Add(prereleases);
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            var service = new UpdateService(UpdateHttp, "WiFiDBUploader", UploaderSettings.Version, GitHubRepo, GitLabProject);
            string installer = WindowsUpdateInstaller.IsInstalledCopy ? WindowsUpdateInstaller.InstallerSuffix : null;
            try
            {
                UpdateCheckResult result = await service.CheckAsync(_settings.IncludePrereleaseUpdates, installer, _closing.Token);
                if (!result.IsUpdateAvailable)
                {
                    if (manual) MessageBox.Show(this, $"WDB Uploader {UploaderSettings.Version} is up to date.");
                    return;
                }

                using var form = new UpdateForm(UpdateHttp, result, () => _importsRunning > 0);
                form.ShowDialog(this);
                if (form.InstallStarted) Application.Exit();
            }
            catch (OperationCanceledException)
            {
            }
            catch (UpdateCheckException ex)
            {
                _log.Write("Main", ex.Message);
                if (manual) MessageBox.Show(this, ex.Message);
            }
        }

        //
        // Leaving
        //

        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Application.Exit();

        private void exitAndSaveDBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _saveDbOnExit = true;
            Application.Exit();
        }

        private void WiFiDBUploadMainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_shutDown) return;
            _shutDown = true;
            _closing.Cancel();
            _statusTimer?.Stop();
            _daemonTimer?.Stop();
            _autoImportTimer?.Stop();
            if (!_saveDbOnExit) _db.Clear();
            _db.Dispose();
            _api.Dispose();
            _log.Dispose();
        }
    }
}
