using System.Security.Cryptography;
using WDBAPI;
using WDBSQLite;
using WDBTraceLog;

namespace WDBCommon
{
    public enum ImportMode
    {
        /// <summary>The user chose this file: failures are shown and it can be tried again.</summary>
        Manual,

        /// <summary>A folder scan on a timer: runs over and over, so a file is dealt with once.</summary>
        Automatic,
    }

    /// <summary>Takes files from disk to WifiDB and keeps their rows up to date.</summary>
    public sealed class WDBCommon
    {
        /// <summary>A file changed this recently may still be being written, so an automatic scan leaves it for the next run.</summary>
        private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(60);

        /// <summary>How many checks in a row may not find a file before it stops being followed.</summary>
        private const int MaxUnknownChecks = 5;

        private readonly WDBAPI.WDBAPI _api;
        private readonly WDBSQLite.WDBSQLite _db;
        private readonly TraceLog _log;
        private readonly SemaphoreSlim _importGate = new(1, 1);
        private readonly Dictionary<string, (long Length, DateTime Written, string Hash)> _hashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _unknownChecks = new(StringComparer.OrdinalIgnoreCase);

        public bool ArchiveImports { get; set; }
        public string ArchiveImportsFolderPath { get; set; } = "";
        public string Username { get; set; } = "";
        public string ApiKey { get; set; } = "";

        public WDBCommon(WDBSQLite.WDBSQLite db, WDBAPI.WDBAPI api, TraceLog log)
        {
            _db = db;
            _api = api;
            _log = log;
        }

        /// <summary>The importable-looking files directly in a folder.</summary>
        public static List<string> FindFiles(string folder) =>
            Directory.EnumerateFiles(folder)
                .Where(f => ImportFileType.ScannedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public async Task ImportFolderAsync(string folder, string title, string notes, ImportMode mode, IProgress<ImportRow>? progress, CancellationToken ct)
        {
            foreach (string file in FindFiles(folder))
            {
                ct.ThrowIfCancellationRequested();
                if (mode == ImportMode.Automatic && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < SettleTime)
                {
                    _log.Write("Import", $"Leaving {file} for the next scan: it changed moments ago.");
                    continue;
                }
                await ImportFileAsync(file, title, notes, mode, progress, ct);
            }
        }

        /// <summary>One file at a time across the app, so a timer and a menu click can't both send the same file.</summary>
        public async Task ImportFileAsync(string path, string title, string notes, ImportMode mode, IProgress<ImportRow>? progress, CancellationToken ct)
        {
            await _importGate.WaitAsync(ct);
            try
            {
                await ImportFileCoreAsync(path, title, notes, mode, progress, ct);
            }
            finally
            {
                _importGate.Release();
            }
        }

        private async Task ImportFileCoreAsync(string path, string title, string notes, ImportMode mode, IProgress<ImportRow>? progress, CancellationToken ct)
        {
            ImportFileInfo info;
            string hash;
            try
            {
                info = ImportFileType.Detect(path);
                if (info.Kind == ImportFileKind.NotRecognized && mode == ImportMode.Automatic)
                {
                    _log.Write("Import", $"Skipping {path}: {info.Reason}");
                    return;
                }
                hash = await HashFileAsync(path, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Probably still open somewhere. A scan comes back to it; a user who chose it wants to know.
                _log.Write("Import", $"Couldn't read {path}: {ex.Message}");
                if (mode == ImportMode.Manual)
                {
                    var unreadable = NewRow(path, "", "UNREADABLE:" + path.ToUpperInvariant(), title);
                    unreadable.Status = ImportStatus.Error;
                    unreadable.Message = $"Couldn't read the file: {ex.Message}";
                    Publish(unreadable, progress);
                }
                return;
            }

            ImportRow? existing = _db.FindByHash(hash);
            if (existing is not null)
            {
                bool retry = mode == ImportMode.Manual ? ImportStatus.CanRetry(existing.Status) : existing.Status == ImportStatus.Error;
                if (!retry) return;
            }

            ImportRow row = NewRow(path, info.Format, hash, title);
            if (info.Kind != ImportFileKind.Accepted)
            {
                row.Status = ImportStatus.Unsupported;
                row.Message = info.Reason ?? "WifiDB can't import this file.";
                Publish(row, progress);
                return;
            }

            row.Status = ImportStatus.Uploading;
            row.Message = "Checking WifiDB for this file...";
            Publish(row, progress);

            RemoteFileStatus known = await _api.CheckHashAsync(hash, ct);
            if (known.Error is not null)
            {
                Finish(row, ImportStatus.Error, known.Error, progress);
                return;
            }
            if (known.State != RemoteFileState.Unknown)
            {
                ApplyRemote(row, known);
                row.Message = "Already on WifiDB. " + row.Message;
                Publish(row, progress);
                return;
            }

            row.Message = "Uploading file to WifiDB...";
            Publish(row, progress);

            UploadResult result = await _api.UploadAsync(path, title, notes, "", ct);
            if (result.Success)
            {
                row.Status = ImportStatus.Waiting;
                row.Message = result.Message;
                if (int.TryParse(result.ImportId, out int importId)) row.ImportID = importId;
                if (!string.IsNullOrEmpty(result.Title)) row.ImportTitle = result.Title;
                Publish(row, progress);
                ArchiveImportedFile(path);
            }
            else if (result.AlreadyKnown)
            {
                RemoteFileStatus now = await _api.CheckHashAsync(hash, ct);
                if (now.Error is null && now.State != RemoteFileState.Unknown) ApplyRemote(row, now);
                else row.Status = ImportStatus.Finished;
                row.Message = result.Message;
                Publish(row, progress);
            }
            else
            {
                Finish(row, result.ConnectionFailed ? ImportStatus.Error : ImportStatus.Rejected, result.Message, progress);
            }
        }

        /// <summary>
        /// Asks WifiDB about rows still in its queue. The profile answers for a signed-in user's files all at once and is
        /// the only thing that reports failed imports; check_hash covers anonymous uploads and servers without the profile.
        /// </summary>
        /// <returns>The profile's error, if signing in was the problem, for the status bar.</returns>
        public async Task<string?> RefreshStatusesAsync(IReadOnlyList<ImportRow> rows, IProgress<ImportRow>? progress, CancellationToken ct)
        {
            if (rows.Count == 0) return null;

            ProfileResult? profile = null;
            bool signedIn = Username.Length > 0 && ApiKey.Length > 0 && !Username.Equals("AnonCoward", StringComparison.OrdinalIgnoreCase);
            if (signedIn) profile = await _api.GetProfileAsync(ct);

            foreach (ImportRow original in rows)
            {
                ct.ThrowIfCancellationRequested();
                ImportRow row = original.Copy();
                RemoteFileStatus? remote = null;
                if (profile?.Files is not null && profile.Files.TryGetValue(row.FileHash, out RemoteFileStatus? fromProfile)) remote = fromProfile;
                remote ??= await _api.CheckHashAsync(row.FileHash, ct);

                if (remote.Error is not null) continue;   // says nothing about the file; try again next time
                if (remote.State == RemoteFileState.Unknown)
                {
                    _unknownChecks[row.FileHash] = _unknownChecks.GetValueOrDefault(row.FileHash) + 1;
                    if (_unknownChecks[row.FileHash] < MaxUnknownChecks) continue;   // it may be between two of WifiDB's queues
                    row.Status = ImportStatus.Unknown;
                    row.Message = "WifiDB no longer lists this file. Its import may have failed.";
                    Publish(row, progress);
                    continue;
                }

                _unknownChecks.Remove(row.FileHash);
                if (ApplyRemote(row, remote)) Publish(row, progress);
            }
            return profile?.Error;
        }

        private static bool ApplyRemote(ImportRow row, RemoteFileStatus remote)
        {
            string status = remote.State switch
            {
                RemoteFileState.Waiting => ImportStatus.Waiting,
                RemoteFileState.Importing => ImportStatus.Importing,
                RemoteFileState.Finished => ImportStatus.Finished,
                RemoteFileState.Failed => ImportStatus.Bad,
                _ => ImportStatus.Unknown,
            };
            bool changed = row.Status != status || row.Message != remote.Message;
            row.Status = status;
            row.Message = remote.Message;
            if (remote.ImportId != 0) row.ImportID = remote.ImportId;
            if (!string.IsNullOrEmpty(remote.Title)) row.ImportTitle = remote.Title;
            return changed;
        }

        private void Finish(ImportRow row, string status, string message, IProgress<ImportRow>? progress)
        {
            row.Status = status;
            row.Message = message;
            Publish(row, progress);
        }

        /// <summary>Saves the row and hands the list its own copy, since the caller keeps changing this one.</summary>
        private void Publish(ImportRow row, IProgress<ImportRow>? progress)
        {
            _db.SaveRow(row);
            progress?.Report(row.Copy());
        }

        private ImportRow NewRow(string path, string format, string hash, string title) => new()
        {
            Username = Username,
            ImportTitle = title,
            DateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            FileSize = File.Exists(path) ? new FileInfo(path).Length.ToString() : "",
            FileName = path,
            Format = format,
            FileHash = hash,
        };

        /// <summary>The file's MD5, as WifiDB computes it. Remembered until the file changes, because scans repeat.</summary>
        private async Task<string> HashFileAsync(string path, CancellationToken ct)
        {
            var file = new FileInfo(path);
            if (_hashes.TryGetValue(path, out var cached) && cached.Length == file.Length && cached.Written == file.LastWriteTimeUtc)
                return cached.Hash;

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(await MD5.HashDataAsync(stream, ct));
            _hashes[path] = (file.Length, file.LastWriteTimeUtc, hash);
            return hash;
        }

        private void ArchiveImportedFile(string path)
        {
            if (!ArchiveImports || string.IsNullOrWhiteSpace(ArchiveImportsFolderPath)) return;
            try
            {
                Directory.CreateDirectory(ArchiveImportsFolderPath);
                string target = Path.Combine(ArchiveImportsFolderPath, Path.GetFileName(path));
                for (int i = 1; File.Exists(target); i++)
                {
                    target = Path.Combine(ArchiveImportsFolderPath, $"{Path.GetFileNameWithoutExtension(path)} ({i}){Path.GetExtension(path)}");
                }
                File.Move(path, target);
                _log.Write("Import", $"Archived {path} to {target}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Write("Import", $"Couldn't archive {path}: {ex.Message}");
            }
        }
    }
}
