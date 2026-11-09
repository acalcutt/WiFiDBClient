using System.Net;
using WDBAPI;
using WDBTraceLog;
using Xunit;
using Api = WDBAPI.WDBAPI;

namespace WiFiDBUploader.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"wdb-test-{Guid.NewGuid():N}.vs1");
    private readonly TraceLog _log = new("", false, false);

    public ApiTests() => File.WriteAllText(_file, "# Vistumbler VS1 - Detailed Export Version 4.1\r\n");

    public void Dispose()
    {
        File.Delete(_file);
        _log.Dispose();
    }

    private Api NewApi(HttpMessageHandler http) => new(_log, http)
    {
        ApiBase = "https://wifidb.test/api/v2/",
        Username = "member",
        ApiKey = "secret-key",
    };

    [Theory]
    [InlineData("https://wifidb.net", "/api/v2/", "https://wifidb.net/api/v2/")]
    [InlineData("https://wifidb.net/", "api/v2", "https://wifidb.net/api/v2/")]
    [InlineData("https://api.wifidb.net", "/v2/", "https://api.wifidb.net/v2/")]
    [InlineData("wifidb.net", "/api/v2/", "https://wifidb.net/api/v2/")]
    [InlineData("https://wifidb.net", "", "https://wifidb.net/")]
    public void Builds_the_api_address(string server, string path, string expected) =>
        Assert.Equal(expected, Api.CombineApiUrl(server, path));

    [Fact]
    public async Task Upload_sends_the_file_and_keeps_the_key_out_of_the_url()
    {
        var http = FakeHttp.Json(_ => """{"import":{"title":"Drive","user":"member","message":"File has been inserted for importing at a scheduled time.","importnum":"42","filehash":"abc"}}""");
        using Api api = NewApi(http);

        UploadResult result = await api.UploadAsync(_file, "Drive", "Notes");

        Assert.True(result.Success);
        Assert.Equal("42", result.ImportId);
        var sent = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://wifidb.test/api/v2/import.php", sent.Uri.ToString());
        Assert.DoesNotContain("secret-key", sent.Uri.ToString());
        Assert.Contains("name=\"file\"; filename=\"" + Path.GetFileName(_file) + "\"", sent.Body);
        Assert.Contains("secret-key", sent.Body);
        Assert.Contains("# Vistumbler VS1", sent.Body);
    }

    [Theory]
    [InlineData("""{"error":"Your account requires an api key to import."}""", "Your account requires an api key to import.")]
    [InlineData("""{"error":{"desc":"There was an error inserting file for scheduled import."}}""", "There was an error inserting file for scheduled import.")]
    [InlineData("""["Imports are disabled because the database is being rebuilt."]""", "Imports are disabled because the database is being rebuilt.")]
    public async Task Upload_reports_what_wifidb_says(string answer, string expected)
    {
        using Api api = NewApi(FakeHttp.Json(_ => answer));
        UploadResult result = await api.UploadAsync(_file, "t", "n");
        Assert.False(result.Success);
        Assert.False(result.ConnectionFailed);
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task Upload_of_a_known_file_says_so()
    {
        using Api api = NewApi(FakeHttp.Json(_ => """{"error":"File Hash already exists in WiFiDB:  abc"}"""));
        Assert.True((await api.UploadAsync(_file, "t", "n")).AlreadyKnown);
    }

    [Fact]
    public async Task Html_from_a_broken_server_is_an_error_and_not_a_crash()
    {
        using Api api = NewApi(new FakeHttp((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad Gateway</html>") }));
        UploadResult result = await api.UploadAsync(_file, "t", "n");
        Assert.False(result.Success);
        Assert.True(result.ConnectionFailed);
    }

    [Fact]
    public async Task Json_after_php_warnings_is_still_read()
    {
        using Api api = NewApi(FakeHttp.Json(_ => "<b>Deprecated</b>: something on line 3\n{\"scheduling\":{\"waiting\":{\"id\":\"7\",\"title\":\"Drive\"}}}"));
        RemoteFileStatus status = await api.CheckHashAsync("ABC");
        Assert.Equal(RemoteFileState.Waiting, status.State);
        Assert.Equal(7, status.ImportId);
    }

    [Theory]
    [InlineData("""{"scheduling":{"unknown":"Hash not found in WifiDB"}}""", RemoteFileState.Unknown)]
    [InlineData("""{"scheduling":{"waiting":{"id":"1"}}}""", RemoteFileState.Waiting)]
    [InlineData("""{"scheduling":{"importing":{"id":"1","ap":"5","tot":"10"}}}""", RemoteFileState.Importing)]
    [InlineData("""{"scheduling":{"finished":{"id":"1","aps":"12","gps":"34"}}}""", RemoteFileState.Finished)]
    public async Task Reads_every_hash_state(string answer, RemoteFileState expected)
    {
        using Api api = NewApi(FakeHttp.Json(_ => answer));
        RemoteFileStatus status = await api.CheckHashAsync("ABC");
        Assert.Null(status.Error);
        Assert.Equal(expected, status.State);
    }

    [Fact]
    public async Task A_refused_hash_check_is_an_error_and_not_a_known_file()
    {
        using Api api = NewApi(FakeHttp.Json(_ => """{"error":"Invalid API Key set."}"""));
        RemoteFileStatus status = await api.CheckHashAsync("ABC");
        Assert.Equal("Invalid API Key set.", status.Error);
    }

    [Fact]
    public async Task Reads_the_daemons_and_an_empty_list()
    {
        using (Api api = NewApi(FakeHttp.Json(_ => """{"daemons":{"daemon1":{"nodename":"wifidb-a","pidfile":"x.pid","pid":"9","pidtime":"0:00","pidmem":"0%","pidcmd":"php","date":"2026-11-09"}}}""")))
        {
            DaemonInfo daemon = Assert.Single((await api.GetDaemonsAsync()).Daemons);
            Assert.Equal("wifidb-a", daemon.NodeName);
        }
        using (Api api = NewApi(FakeHttp.Json(_ => """{"daemons":"No Daemons running."}""")))
        {
            DaemonList none = await api.GetDaemonsAsync();
            Assert.Null(none.Error);
            Assert.Empty(none.Daemons);
        }
    }

    [Fact]
    public async Task Profile_gives_the_queue_by_hash_and_failed_imports()
    {
        using Api api = NewApi(FakeHttp.Json(_ => """
            {"profile":{"queue":{"waiting":[{"id":"1","hash":"AAA"}],"importing":[{"id":"2","hash":"BBB","ap":"1","tot":"2"}],"bad":[{"id":"3","hash":"CCC","error_msg":"Bad file"}]},
             "imports":{"rows":[{"id":4,"hash":"DDD","aps":10,"gps":20,"title":"Done"}]}}}
            """));
        ProfileResult result = await api.GetProfileAsync();
        Assert.Equal(RemoteFileState.Waiting, result.Files!["aaa"].State);
        Assert.Equal(RemoteFileState.Importing, result.Files["BBB"].State);
        Assert.Equal(RemoteFileState.Failed, result.Files["CCC"].State);
        Assert.Equal("Bad file", result.Files["CCC"].Message);
        Assert.Equal(RemoteFileState.Finished, result.Files["DDD"].State);
    }

    [Fact]
    public async Task A_redirect_resends_the_file_as_a_post()
    {
        int calls = 0;
        var http = new FakeHttp((_, _) =>
        {
            if (calls++ == 0)
            {
                var moved = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                moved.Headers.Location = new Uri("https://wifidb.test/moved/import.php");
                return moved;
            }
            return FakeHttp.Ok("""{"import":{"message":"ok","importnum":"1"}}""");
        });
        using Api api = NewApi(http);

        Assert.True((await api.UploadAsync(_file, "t", "n")).Success);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(HttpMethod.Post, http.Requests[1].Method);
        Assert.Contains("# Vistumbler VS1", http.Requests[1].Body);
    }

    [Fact]
    public void A_redirect_never_drops_to_http_or_leaves_the_host()
    {
        var current = new Uri("https://wifidb.net/wifidb/api/v2/import.php");
        Assert.Equal("https://wifidb.net/api/v2/import.php", Api.NextHop(current, new Uri("http://wifidb.net/api/v2/import.php")).ToString());
        Assert.Equal("https://www.wifidb.net/x", Api.NextHop(current, new Uri("https://www.wifidb.net/x")).ToString());
        Assert.Throws<HttpRequestException>(() => Api.NextHop(current, new Uri("https://evil.test/import.php")));
    }

    [Fact]
    public void Sends_a_name_php_can_read() =>
        Assert.Equal("caf_ drive.vs1", Api.UploadFileName(@"C:\scans\café drive.vs1"));
}
