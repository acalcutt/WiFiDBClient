using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WiFiDBUploader
{
    /// <summary>The uploader's settings, kept under HKCU\SOFTWARE\WiFiDB\Uploader as they always have been.</summary>
    public sealed class UploaderSettings
    {
        private const string RootPath = @"SOFTWARE\WiFiDB\Uploader";
        private const string DefaultNotesPrefix = "WDB Uploader\nVersion: ";

        public bool AutoUploadFolder;
        public string AutoUploadFolderPath = "";
        public bool ArchiveImports;
        public string ArchiveImportsFolderPath = "";
        public bool AutoCloseEnable;
        public int AutoCloseTimerSeconds = 30;
        public string DefaultImportTitle = "%DATETIME%";
        public string DefaultImportNotes = DefaultNotes;
        public bool UseDefaultImportValues;
        public bool UseAutoDateTimeTitle;
        public string SQLiteDBPath = @".\DB";
        public string SQLiteDBFile = "Uploader.db3";
        public bool ImportUpdateThreadEnable = true;
        public int ImportUpdateThreadSeconds = 30;
        public bool DaemonUpdateThreadEnable = true;
        public int DaemonUpdateThreadSeconds = 60;
        public bool AutoImportThreadEnable;
        public int AutoImportThreadSeconds = 30;
        public bool TraceLogEnable;
        public bool PerRunRotate;
        public bool DEBUG;
        public string LogPath = @".\Logs\";
        public bool AutoCheckForUpdates = true;
        public bool IncludePrereleaseUpdates;
        public List<ServerObj> ServerList = new List<ServerObj>();

        public static string Version => Application.ProductVersion;
        private static string DefaultNotes => DefaultNotesPrefix + Version;

        public ServerObj SelectedServer => ServerList.FirstOrDefault(s => s.Selected);

        /// <summary>
        /// The database file. Saved paths are relative (.\DB) from when the app ran from a folder of its own; under
        /// Program Files that isn't writable, so relative paths now live in the user's AppData.
        /// </summary>
        public string DatabaseFile => Path.Combine(ResolveDataPath(SQLiteDBPath), SQLiteDBFile);

        public string LogFolder => ResolveDataPath(LogPath);

        private static string ResolveDataPath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WiFiDB Uploader");
            return Path.GetFullPath(Path.Combine(folder, path));
        }

        public static UploaderSettings Load()
        {
            var settings = new UploaderSettings();
            using RegistryKey root = Registry.CurrentUser.CreateSubKey(RootPath);
            if (root.GetValueNames().Length == 0 && root.GetSubKeyNames().Length == 0)
            {
                settings.ServerList.Add(new ServerObj
                {
                    ServerAddress = "https://wifidb.net",
                    ApiPath = "/api/v2/",
                    Username = "AnonCoward",
                    ApiKey = "scaredycat",
                    Selected = true,
                });
                settings.Save();
                return settings;
            }

            settings.AutoUploadFolder = GetBool(root, "AutoUploadFolder", false);
            settings.AutoUploadFolderPath = GetString(root, "AutoUploadFolderPath", "");
            settings.ArchiveImports = GetBool(root, "ArchiveImports", false);
            settings.ArchiveImportsFolderPath = GetString(root, "ArchiveImportsFolderPath", "");
            settings.AutoCloseEnable = GetBool(root, "AutoCloseEnable", false);
            settings.AutoCloseTimerSeconds = GetInt(root, "AutoCloseTimerSeconds", 30);
            settings.DefaultImportTitle = GetString(root, "DefaultImportTitle", "%DATETIME%");
            settings.DefaultImportNotes = GetString(root, "DefaultImportNotes", DefaultNotes);
            settings.UseDefaultImportValues = GetBool(root, "UseDefaultImportValues", false);
            settings.UseAutoDateTimeTitle = GetBool(root, "UseAutoDateTimeTitle", false);
            settings.SQLiteDBPath = GetString(root, "SQLiteDBPath", @".\DB");
            settings.SQLiteDBFile = GetString(root, "SQLiteDBFile", "Uploader.db3");
            settings.ImportUpdateThreadEnable = GetBool(root, "ImportUpdateThreadEnable", true);
            settings.ImportUpdateThreadSeconds = GetInt(root, "ImportUpdateThreadSeconds", 30);
            settings.DaemonUpdateThreadEnable = GetBool(root, "DaemonUpdateThreadEnable", true);
            settings.DaemonUpdateThreadSeconds = GetInt(root, "DaemonUpdateThreadSeconds", 60);
            settings.AutoImportThreadEnable = GetBool(root, "AutoImportThreadEnable", false);
            settings.AutoImportThreadSeconds = GetInt(root, "AutoImportThreadSeconds", 30);
            settings.TraceLogEnable = GetBool(root, "TraceLogEnable", false);
            settings.PerRunRotate = GetBool(root, "PerRunRotate", false);
            settings.DEBUG = GetBool(root, "DEBUGEnable", false);
            settings.LogPath = GetString(root, "LogPath", @".\Logs\");
            settings.AutoCheckForUpdates = GetBool(root, "AutoCheckForUpdates", true);
            settings.IncludePrereleaseUpdates = GetBool(root, "IncludePrereleaseUpdates", false);

            // The notes the app wrote itself name its version. Notes the user wrote are left alone.
            if (settings.DefaultImportNotes.StartsWith(DefaultNotesPrefix, StringComparison.Ordinal)) settings.DefaultImportNotes = DefaultNotes;

            using RegistryKey servers = root.CreateSubKey("Servers");
            int id = 0;
            foreach (string name in servers.GetSubKeyNames())
            {
                using RegistryKey key = servers.OpenSubKey(name);
                settings.ServerList.Add(new ServerObj
                {
                    ID = id++,
                    ServerAddress = GetString(key, "ServerAddress", ""),
                    ApiPath = GetString(key, "ApiPath", ""),
                    Username = GetString(key, "Username", ""),
                    ApiKey = GetString(key, "ApiKey", ""),
                    Selected = GetBool(key, "Selected", false),
                });
            }
            return settings;
        }

        public void Save()
        {
            using RegistryKey root = Registry.CurrentUser.CreateSubKey(RootPath);
            root.SetValue("Version", Version);
            root.SetValue("AutoUploadFolder", AutoUploadFolder);
            root.SetValue("AutoUploadFolderPath", AutoUploadFolderPath ?? "");
            root.SetValue("ArchiveImports", ArchiveImports);
            root.SetValue("ArchiveImportsFolderPath", ArchiveImportsFolderPath ?? "");
            root.SetValue("AutoCloseEnable", AutoCloseEnable);
            root.SetValue("AutoCloseTimerSeconds", AutoCloseTimerSeconds);
            root.SetValue("DefaultImportTitle", DefaultImportTitle ?? "");
            root.SetValue("DefaultImportNotes", DefaultImportNotes ?? "");
            root.SetValue("UseDefaultImportValues", UseDefaultImportValues);
            root.SetValue("UseAutoDateTimeTitle", UseAutoDateTimeTitle);
            root.SetValue("SQLiteDBPath", SQLiteDBPath);
            root.SetValue("SQLiteDBFile", SQLiteDBFile);
            root.SetValue("ImportUpdateThreadEnable", ImportUpdateThreadEnable);
            root.SetValue("ImportUpdateThreadSeconds", ImportUpdateThreadSeconds);
            root.SetValue("DaemonUpdateThreadEnable", DaemonUpdateThreadEnable);
            root.SetValue("DaemonUpdateThreadSeconds", DaemonUpdateThreadSeconds);
            root.SetValue("AutoImportThreadEnable", AutoImportThreadEnable);
            root.SetValue("AutoImportThreadSeconds", AutoImportThreadSeconds);
            root.SetValue("TraceLogEnable", TraceLogEnable);
            root.SetValue("PerRunRotate", PerRunRotate);
            root.SetValue("DEBUGEnable", DEBUG);
            root.SetValue("LogPath", LogPath);
            root.SetValue("AutoCheckForUpdates", AutoCheckForUpdates);
            root.SetValue("IncludePrereleaseUpdates", IncludePrereleaseUpdates);

            using RegistryKey servers = root.CreateSubKey("Servers");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerObj server in ServerList)
            {
                string name = server.Name;
                names.Add(name);
                using RegistryKey key = servers.CreateSubKey(name);
                key.SetValue("ServerAddress", server.ServerAddress ?? "");
                key.SetValue("ApiPath", server.ApiPath ?? "");
                key.SetValue("Username", server.Username ?? "");
                key.SetValue("ApiKey", server.ApiKey ?? "");
                key.SetValue("Selected", server.Selected);
            }
            foreach (string stale in servers.GetSubKeyNames().Where(n => !names.Contains(n)))
            {
                servers.DeleteSubKeyTree(stale);
            }
        }

        private static string GetString(RegistryKey key, string name, string fallback) => key.GetValue(name)?.ToString() ?? fallback;

        private static bool GetBool(RegistryKey key, string name, bool fallback) =>
            bool.TryParse(key.GetValue(name)?.ToString(), out bool value) ? value : fallback;

        private static int GetInt(RegistryKey key, string name, int fallback) =>
            int.TryParse(key.GetValue(name)?.ToString(), out int value) && value > 0 ? value : fallback;
    }

    public class ServerObj
    {
        public int ID { get; set; }
        public string ServerAddress { get; set; }
        public string ApiPath { get; set; }
        public string Username { get; set; }
        public string ApiKey { get; set; }
        public bool Selected { get; set; }

        /// <summary>How a server is named in the list and the registry: its address without the scheme.</summary>
        public string Name => (ServerAddress ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/');
    }
}
