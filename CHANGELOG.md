# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._


## 2.0.0-rc.1
### ✨ Features and improvements
- **Runs on .NET 10**, as a self-contained x64 or ARM64 app with an installer or a portable zip. It no longer needs the .NET Framework or the Visual C++ 2010 runtime.
- **Uses WifiDB's current API (`api/v2`) and its JSON answers.** New installs point at `https://wifidb.net` with `/api/v2/`. Saved servers keep working, including `api.wifidb.net` with `/v2/`. The username and API key now go in the POST body and are never written to the log.
- **Knows the files Vistumbler, VistumblerCS and VistumblerMAUI write:** VS1 up to 4.1, VSZ, Vistumbler CSV, WiGLE CSV (also gzipped), NetStumbler NS1, Kismet NetXML, Vistumbler MDB and Wardrive DB/DB3. A new Format column shows what each file is and which app made it. NS1 needs a WifiDB with the API change from this release's work; an older server refuses it with a clear message.
- **Says when a file can't go up, and what to do instead**, such as a VistumblerMAUI session database or a KismetDB file.
- **Follows imports with the profile API**, so a signed-in user sees when WifiDB failed to import a file, and why.
- **Automatic updates.** Checks GitHub releases at startup (Settings can turn this off or include pre-releases), and File → Check For Updates checks on demand. An installed copy updates itself; a portable copy gets a link to the release page.
- **Import File can take several files at once.**

### 🐞 Bug fixes
- **Connects to servers that require modern TLS**, which .NET Framework 4.5.2 didn't do by default.
- **An API error no longer reads as "already imported"**, which silently skipped every file.
- **A failed upload no longer moves the file to the archive folder**, and archiving no longer fails on a name that's already there.
- **A failed upload can be tried again** without restarting the app.
- **Background Auto Import stays on.** The setting was reset at every start and never saved.
- **Auto Import no longer opens the title dialog on every scan**, skips files still being written, doesn't re-hash unchanged files, and doesn't send a file twice when scans overlap.
- **Turning a background timer off now stops it.**
- **Upgrading no longer overwrites Default Import Notes you wrote yourself.**
- **Exit and Save DB saves the list** instead of failing while closing.
- **The database and logs are kept in AppData** when their paths are relative, so an install under Program Files can write them.
