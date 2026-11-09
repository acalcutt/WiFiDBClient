using System.Net;
using WDBCommon.Updates;
using Xunit;

namespace WiFiDBUploader.Tests;

public class UpdateServiceTests
{
    private const string GitHubJson = """
        [{ "tag_name": "v2.1.0", "draft": false, "body": "notes", "html_url": "https://github.com/acalcutt/WiFiDBClient/releases/tag/v2.1.0",
           "assets": [{ "name": "WiFiDBUploader-v2.1.0-win-x64-setup.exe", "browser_download_url": "https://github.test/x64-setup.exe" }] },
         { "tag_name": "v1.4", "draft": false, "assets": [] },
         { "tag_name": "v9.9.9", "draft": true, "assets": [] }]
        """;

    private const string GitLabJson = """
        [{ "tag_name": "v2.1.0", "description": "notes", "_links": { "self": "https://gitlab.test/r" },
           "assets": { "links": [{ "name": "WiFiDBUploader-v2.1.0-win-x64-setup.exe", "direct_asset_url": "https://gitlab.test/x64-setup.exe" }] } }]
        """;

    private static (UpdateService Service, FakeHttp Http) Create(Func<Uri, HttpResponseMessage> answer, string current = "2.0.0", string? gitLab = null)
    {
        var http = new FakeHttp((request, _) => answer(request.RequestUri!));
        return (new UpdateService(new HttpClient(http), "WiFiDBUploader", current, "acalcutt/WiFiDBClient", gitLab), http);
    }

    [Fact]
    public async Task Github_is_asked_first_and_an_old_two_part_tag_is_understood()
    {
        var (service, http) = Create(_ => FakeHttp.Ok(GitHubJson), gitLab: "techidiots-llc/WiFiDBClient");
        UpdateCheckResult result = await service.CheckAsync(includePrereleases: false, "-win-x64-setup.exe");

        Assert.Equal("GitHub", result.Source);
        Assert.Equal("2.1.0", result.Update!.Version.ToString());
        Assert.Equal("https://github.test/x64-setup.exe", result.Update.FindAsset("-win-x64-setup.exe")!.Url);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Gitlab_is_the_backup_when_github_is_down()
    {
        var (service, _) = Create(uri => uri.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : FakeHttp.Ok(GitLabJson), gitLab: "techidiots-llc/WiFiDBClient");
        UpdateCheckResult result = await service.CheckAsync(false, "-win-x64-setup.exe");

        Assert.Equal("GitLab", result.Source);
        Assert.Equal("https://gitlab.test/x64-setup.exe", result.Update!.FindAsset("-win-x64-setup.exe")!.Url);
    }

    [Fact]
    public async Task Without_a_gitlab_project_only_github_is_asked()
    {
        var (service, http) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<UpdateCheckException>(() => service.CheckAsync(false));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task A_prerelease_build_is_offered_the_final_release()
    {
        var (service, _) = Create(_ => FakeHttp.Ok("""[{ "tag_name": "v2.0.0", "assets": [] }]"""), current: "2.0.0-rc.1");
        Assert.Equal("2.0.0", (await service.CheckAsync(false)).Update!.Version.ToString());
    }

    [Fact]
    public async Task Up_to_date_offers_nothing()
    {
        var (service, _) = Create(_ => FakeHttp.Ok("""[{ "tag_name": "v2.0.0", "assets": [] }]"""));
        Assert.False((await service.CheckAsync(false)).IsUpdateAvailable);
    }
}
