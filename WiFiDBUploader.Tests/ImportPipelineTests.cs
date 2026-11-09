using Microsoft.Data.Sqlite;
using WDBCommon;
using WDBSQLite;
using WDBTraceLog;
using Xunit;
using Api = WDBAPI.WDBAPI;
using Common = WDBCommon.WDBCommon;
using Database = WDBSQLite.WDBSQLite;

namespace WiFiDBUploader.Tests;

/// <summary>The whole path from a file to its row, with WifiDB faked.</summary>
public sealed class ImportPipelineTests : IDisposable
{
    private const string Unknown = """{"scheduling":{"unknown":"Hash not found in WifiDB"}}""";
    private const string Queued = """{"import":{"title":"Drive","user":"member","message":"File has been inserted for importing at a scheduled time.","importnum":"42","filehash":"abc"}}""";

    private readonly string _dir = Directory.CreateTempSubdirectory("wdb-pipe-").FullName;
    private readonly string _archive;
    private readonly TraceLog _log = new("", false, false);
    private readonly Database _db;
    private readonly List<ImportRow> _published = new();

    public ImportPipelineTests()
    {
        _archive = Path.Combine(_dir, "archive");
        _db = new Database(Path.Combine(_dir, "uploader.db3"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _log.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class Sync(List<ImportRow> rows) : IProgress<ImportRow>
    {
        public void Report(ImportRow value) => rows.Add(value);
    }

    private (Common Common, FakeHttp Http, Api Api) Setup(Func<string, string> answer, bool archive = false)
    {
        var http = FakeHttp.Json(answer);
        var api = new Api(_log, http) { ApiBase = "https://wifidb.test/api/v2/", Username = "member", ApiKey = "key" };
        var common = new Common(_db, api, _log)
        {
            Username = "member",
            ApiKey = "key",
            ArchiveImports = archive,
            ArchiveImportsFolderPath = _archive,
        };
        return (common, http, api);
    }

    private static string Answer(string body, string upload = Queued) => body.Contains("check_hash") ? Unknown : upload;

    private string Vs1(string name = "drive.vs1", string extra = "")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "# Vistumbler VS1 - Detailed Export Version 4.1\r\n" + extra);
        return path;
    }

    private Task Import(Common common, string path, ImportMode mode = ImportMode.Manual) =>
        common.ImportFileAsync(path, "Drive", "Notes", mode, new Sync(_published), CancellationToken.None);

    [Fact]
    public async Task A_new_file_is_uploaded_and_waits()
    {
        var (common, http, api) = Setup(b => Answer(b));
        using (api) await Import(common, Vs1());

        ImportRow row = _published[^1];
        Assert.Equal(ImportStatus.Waiting, row.Status);
        Assert.Equal(42, row.ImportID);
        Assert.Equal("Vistumbler VS1 4.1", row.Format);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(ImportStatus.Waiting, _db.FindByHash(row.FileHash)!.Status);
    }

    [Fact]
    public async Task A_file_wifidb_has_is_not_sent_again()
    {
        var (common, http, api) = Setup(_ => """{"scheduling":{"finished":{"id":"9","aps":"3","gps":"4","title":"Old"}}}""");
        using (api) await Import(common, Vs1());

        Assert.Single(http.Requests);
        Assert.Equal(ImportStatus.Finished, _published[^1].Status);
        Assert.StartsWith("Already on WifiDB.", _published[^1].Message);
    }

    [Fact]
    public async Task An_api_error_on_the_check_is_not_taken_for_already_imported()
    {
        var (common, http, api) = Setup(_ => """{"error":"Invalid API Key set."}""");
        using (api) await Import(common, Vs1());

        Assert.Single(http.Requests);
        Assert.Equal(ImportStatus.Error, _published[^1].Status);
        Assert.Equal("Invalid API Key set.", _published[^1].Message);
    }

    [Fact]
    public async Task A_refused_upload_is_not_archived()
    {
        string path = Vs1();
        var (common, _, api) = Setup(b => Answer(b, """{"error":"Reserved users are not allowed to import."}"""), archive: true);
        using (api) await Import(common, path);

        Assert.Equal(ImportStatus.Rejected, _published[^1].Status);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task An_accepted_upload_is_archived_without_overwriting()
    {
        Directory.CreateDirectory(_archive);
        File.WriteAllText(Path.Combine(_archive, "drive.vs1"), "older");
        string path = Vs1();
        var (common, _, api) = Setup(b => Answer(b), archive: true);
        using (api) await Import(common, path);

        Assert.False(File.Exists(path));
        Assert.Equal("older", File.ReadAllText(Path.Combine(_archive, "drive.vs1")));
        Assert.True(File.Exists(Path.Combine(_archive, "drive (1).vs1")));
    }

    [Fact]
    public async Task An_automatic_scan_deals_with_a_refused_file_once_but_retries_a_lost_connection()
    {
        string refused = Vs1("refused.vs1");
        var (common, http, api) = Setup(b => Answer(b, """{"error":"Nope"}"""));
        using (api)
        {
            await Import(common, refused, ImportMode.Automatic);
            int after = http.Requests.Count;
            await Import(common, refused, ImportMode.Automatic);
            Assert.Equal(after, http.Requests.Count);
            await Import(common, refused, ImportMode.Manual);
            Assert.True(http.Requests.Count > after, "choosing it again tries again");
        }
    }

    [Fact]
    public async Task An_automatic_scan_skips_a_file_still_being_written_and_stray_text()
    {
        Vs1("fresh.vs1");
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "not a scan");
        string settled = Vs1("settled.vs1", "x");
        File.SetLastWriteTimeUtc(settled, DateTime.UtcNow.AddHours(-1));
        string text = Path.Combine(_dir, "readme.txt");
        File.SetLastWriteTimeUtc(text, DateTime.UtcNow.AddHours(-1));

        var (common, http, api) = Setup(b => Answer(b));
        using (api) await common.ImportFolderAsync(_dir, "t", "n", ImportMode.Automatic, new Sync(_published), CancellationToken.None);

        ImportRow only = Assert.Single(_published.Where(r => r.Status == ImportStatus.Waiting));
        Assert.EndsWith("settled.vs1", only.FileName);
        Assert.DoesNotContain(_published, r => r.FileName.EndsWith("readme.txt"));
    }

    [Fact]
    public async Task A_session_database_gets_a_row_saying_what_to_do()
    {
        string path = Path.Combine(_dir, "session.db3");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE DbAccessPoint (id INTEGER)";
            command.ExecuteNonQuery();
        }
        var (common, http, api) = Setup(b => Answer(b));
        using (api) await Import(common, path);

        Assert.Empty(http.Requests);
        Assert.Equal(ImportStatus.Unsupported, _published[^1].Status);
        Assert.Contains("VS1 or VSZ", _published[^1].Message);
    }

    [Fact]
    public async Task Refresh_follows_the_queue_and_reports_failed_imports()
    {
        var row = new ImportRow { FileHash = "AAA", Status = ImportStatus.Waiting, Message = "m" };
        _db.SaveRow(row);
        var (common, _, api) = Setup(_ => """{"profile":{"queue":{"waiting":[],"importing":[],"bad":[{"id":"3","hash":"AAA","error_msg":"Bad VS1"}]},"imports":{"rows":[]}}}""");
        using (api) await common.RefreshStatusesAsync(new[] { row }, new Sync(_published), CancellationToken.None);

        Assert.Equal(ImportStatus.Bad, _published[^1].Status);
        Assert.Equal("Bad VS1", _published[^1].Message);
    }

    [Fact]
    public async Task Refresh_lets_go_of_a_file_only_after_several_misses()
    {
        var row = new ImportRow { FileHash = "AAA", Status = ImportStatus.Waiting };
        var (common, _, api) = Setup(_ => Unknown);
        common.Username = "AnonCoward";
        using (api)
        {
            for (int i = 0; i < 4; i++) await common.RefreshStatusesAsync(new[] { row }, new Sync(_published), CancellationToken.None);
            Assert.Empty(_published);
            await common.RefreshStatusesAsync(new[] { row }, new Sync(_published), CancellationToken.None);
        }
        Assert.Equal(ImportStatus.Unknown, _published[^1].Status);
    }

    [Fact]
    public void A_database_from_before_the_format_column_still_opens()
    {
        string path = Path.Combine(_dir, "old.db3");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"CREATE TABLE ImportView (ID INTEGER PRIMARY KEY AUTOINCREMENT, ImportID INT, Username VARCHAR(255), ImportTitle VARCHAR(255), Date_Time VARCHAR(255),
FileSize VARCHAR(255), FileName VARCHAR(255), FileHash VARCHAR(255), Status VARCHAR(255), Message VARCHAR(255));
INSERT INTO ImportView (FileHash, Status) VALUES ('ABC', 'Finished');";
            command.ExecuteNonQuery();
        }
        using var db = new Database(path);
        Assert.Equal(ImportStatus.Finished, db.FindByHash("abc")!.Status);
    }
}
