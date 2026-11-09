# WiFiDB Uploader

Uploads wireless scan files to [WifiDB](https://wifidb.net), one at a time, by folder, or automatically from a watched folder.

## Download

Get the latest version from the [releases page](https://github.com/acalcutt/WiFiDBClient/releases). Each release has an installer (`-setup.exe`) and a portable zip, for x64 and ARM64. No .NET install is needed.

## Files it can upload

| Written by | Files |
| --- | --- |
| Vistumbler | `.vs1`, `.vsz`, `.csv`, `.txt`, `.mdb`, WiGLE `.csv` |
| VistumblerCS and VistumblerMAUI | `.vs1` (4.0 and 4.1), `.vsz`, Vistumbler `.csv`, WiGLE `.csv`, `.netxml`, `.ns1` |
| Other tools | NetStumbler `.ns1`, Kismet `.netxml`, WiGLE `.csv` and `.csv.gz`, SWardriving `.csv`, Wardrive `.db` and `.db3` |

The file's contents decide what it is, and the Format column shows the result. `.ns1` needs a WifiDB with the `api/v2/import.php` change that accepts it. VistumblerMAUI's session database and KismetDB files can't be uploaded: export them as VS1 or VSZ first.

## Building

Needs the .NET 10 SDK on Windows.

```powershell
dotnet build "WiFiDB Client.sln" -c Release
dotnet test "WiFiDB Client.sln" -c Release
```

## Releasing

The version is `<Version>` in `WiFiDBUploader/WDB_Uploader.csproj`. Add changes under `## master` in `CHANGELOG.md` as you go.

1. Run the **Create bump version PR** workflow and choose the bump type.
2. Review and merge the PR it opens.
3. The merge to `master` runs **Build and Release**, which publishes the installers and zips with that version's changelog as the notes.

Code signing uses the `SIG_PFX` (base64 of the `.pfx`) and `SIG_PFX_PASS` repository secrets. Without them, the release is published unsigned with a warning.

## Automatic updates

The app reads the releases of `acalcutt/WiFiDBClient` on GitHub at startup. Pre-releases are offered only if **Settings → Include Pre-Releases In Updates** is on or the running version is itself a pre-release. An installed copy downloads its `-setup.exe`, checks that it has the same publisher as the running app when the app is signed, and upgrades in place.

### Adding the GitLab backup

GitHub is the only update feed for now. The code already falls back to a second feed; to turn it on:

1. Create the project on `gitlab.techidiots.net` and push this repository to it. `.gitlab-ci.yml` and `.gitlab/scripts` are already here.
2. Add the CI/CD variables listed at the top of `.gitlab-ci.yml`, and make sure the runner tagged `windows` is available to the project.
3. Make the project's releases publicly readable, then confirm the first release publishes on GitLab.
4. Set `GitLabProject` in `WiFiDBUploader/WiFiDBUploadMainForm.cs` to the project's path, such as `techidiots-llc/WiFiDBClient`, and release that version.

Copies older than that release only read GitHub.
