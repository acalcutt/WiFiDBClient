using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace WDBCommon
{
    public enum ImportFileKind
    {
        /// <summary>WifiDB can import this.</summary>
        Accepted,

        /// <summary>A scan file, but one WifiDB's API has no importer for.</summary>
        NotUploadable,

        /// <summary>Not a file WifiDB can import. Folder scans skip these without a word.</summary>
        NotRecognized,
    }

    /// <param name="Format">What the file is, for the list.</param>
    /// <param name="Reason">Why it can't be uploaded, when it can't.</param>
    public sealed record ImportFileInfo(ImportFileKind Kind, string Format, string? Reason = null);

    /// <summary>
    /// Says what a file is before it goes up, from its first bytes and its name. The rules follow WifiDB's
    /// lib/import_type.inc.php, plus what its daemon needs: the daemon picks the importer from the extension, so a file
    /// is only accepted under the extension its importer expects.
    /// </summary>
    public static class ImportFileType
    {
        private const int HeadBytes = 4096;

        /// <summary>The extensions a folder scan looks at.</summary>
        public static readonly IReadOnlySet<string> ScannedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".vs1", ".vsz", ".txt", ".csv", ".gz", ".mdb", ".db3", ".db", ".netxml", ".wiglecsv", ".wiglewificsv", ".ns1", ".kismet",
        };

        public const string OpenFileFilter =
            "WifiDB import files|*.vs1;*.vsz;*.csv;*.txt;*.ns1;*.gz;*.mdb;*.db;*.db3;*.netxml;*.wiglecsv;*.wiglewificsv|" +
            "Vistumbler VS1/VSZ (*.vs1;*.vsz)|*.vs1;*.vsz|CSV, Vistumbler or WiGLE (*.csv;*.gz)|*.csv;*.gz|" +
            "NetStumbler NS1 (*.ns1)|*.ns1|Kismet NetXML (*.netxml)|*.netxml|Vistumbler MDB (*.mdb)|*.mdb|" +
            "Wardrive DB (*.db;*.db3)|*.db;*.db3|All files (*.*)|*.*";

        public static ImportFileInfo Detect(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            byte[] head = ReadHead(path);
            if (head.Length == 0) return NotRecognized("Empty file", "The file is empty.");

            if (StartsWith(head, "SQLite format 3\0")) return DetectSqlite(path, ext);
            if (StartsWith(head, "NetS"))
                return ext == ".ns1" ? new(ImportFileKind.Accepted, "NetStumbler NS1") : WrongName("NetStumbler NS1", ".ns1");
            if (head.Length >= 2 && head[0] == 0x1f && head[1] == 0x8b) return DetectGzip(path);
            if (StartsWith(head, "PK\x03\x04")) return DetectVsz(path, ext);
            if (Regex.IsMatch(Encoding.Latin1.GetString(head), @"\A.{4}Standard (Jet|ACE) DB", RegexOptions.Singleline))
                return ext == ".mdb" ? new(ImportFileKind.Accepted, "Vistumbler MDB") : WrongName("Vistumbler MDB", ".mdb");

            string text = Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n');
            ImportFileInfo? byContent = DetectText(text, ext);
            if (byContent is not null) return byContent;

            // Nothing in the file says what it is. WifiDB takes a .vs1 by its name alone.
            return ext == ".vs1" ? new(ImportFileKind.Accepted, "Vistumbler VS1") : NotRecognized("Unrecognized", "Not a file WifiDB can import.");
        }

        private static ImportFileInfo? DetectText(string text, string ext)
        {
            if (text.StartsWith("WigleWifi", StringComparison.OrdinalIgnoreCase))
            {
                Match app = Regex.Match(text, @"appRelease=([^,\r\n]+)");
                return Accepted(app.Success ? $"WiGLE CSV ({app.Groups[1].Value})" : "WiGLE CSV");
            }
            if (text.StartsWith("BSSID,SSID,AuthMode,channel,frequency", StringComparison.OrdinalIgnoreCase))
                return Accepted("SWardriving CSV");
            if (text.StartsWith("SSID,BSSID,MANUFACTURER", StringComparison.OrdinalIgnoreCase))
                return ext == ".csv" ? Accepted("Vistumbler CSV") : WrongName("Vistumbler CSV", ".csv");
            if (text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<detection-run", StringComparison.Ordinal))
            {
                if (text.Contains("detection-run", StringComparison.OrdinalIgnoreCase) || text.Contains("kismet", StringComparison.OrdinalIgnoreCase))
                    return ext == ".netxml" ? Accepted("Kismet NetXML") : WrongName("Kismet NetXML", ".netxml");
                return null;
            }
            if (text.StartsWith("# Vistumbler", StringComparison.OrdinalIgnoreCase))
            {
                string format = DescribeVistumbler(text);
                return ext is ".vs1" or ".txt" ? Accepted(format) : WrongName(format, ".vs1");
            }
            if (text.StartsWith("# $Creator:", StringComparison.Ordinal))
                return new(ImportFileKind.NotUploadable, "NetStumbler text export",
                    "WifiDB can't import NetStumbler's text export. Save it from NetStumbler as a .ns1 file.");
            return null;
        }

        private static string DescribeVistumbler(string text)
        {
            Match vs1 = Regex.Match(text, @"\A# Vistumbler (VS1|TXT)[^\r\n]*?Version ([0-9.]+)", RegexOptions.IgnoreCase);
            string format = vs1.Success ? $"Vistumbler {vs1.Groups[1].Value.ToUpperInvariant()} {vs1.Groups[2].Value}" : "Vistumbler VS1";
            Match creator = Regex.Match(text, @"^# Created By: *([^\r\n]+)", RegexOptions.Multiline);
            return creator.Success ? $"{format} ({creator.Groups[1].Value.Trim()})" : format;
        }

        private static ImportFileInfo DetectSqlite(string path, string ext)
        {
            HashSet<string> tables;
            try
            {
                tables = ReadTableNames(path);
            }
            catch (SqliteException ex)
            {
                return NotRecognized("SQLite database", $"The database couldn't be read: {ex.Message}");
            }

            // WifiDB sends every SQLite file to its Wardrive importers, which read these tables by extension.
            if (ext == ".db3" && tables.Contains("networks")) return Accepted("Wardrive DB3");
            if (ext == ".db" && tables.Contains("wifi")) return Accepted("Wardrive DB");
            if (tables.Contains("KISMET") || ext == ".kismet")
                return new(ImportFileKind.NotUploadable, "KismetDB", "WifiDB can't import a KismetDB file. Export it as Kismet NetXML, VS1 or VSZ.");
            if (tables.Contains("DbAccessPoint"))
                return new(ImportFileKind.NotUploadable, "VistumblerMAUI session", "This is VistumblerMAUI's own session database. Export the session as VS1 or VSZ, then upload that.");
            return NotRecognized("SQLite database", "This SQLite file isn't a Wardrive database.");
        }

        private static HashSet<string> ReadTableNames(string path)
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using SqliteDataReader reader = command.ExecuteReader();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }

        private static ImportFileInfo DetectGzip(string path)
        {
            try
            {
                using FileStream file = OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                var buffer = new byte[HeadBytes];
                int length = gzip.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                string text = Encoding.UTF8.GetString(buffer, 0, length).TrimStart('﻿', ' ', '\r', '\n');
                if (text.StartsWith("WigleWifi", StringComparison.OrdinalIgnoreCase)) return Accepted("WiGLE CSV (gzip)");
            }
            catch (InvalidDataException)
            {
            }
            return NotRecognized("Gzip file", "Only WiGLE's gzipped CSV exports can be imported.");
        }

        private static ImportFileInfo DetectVsz(string path, string ext)
        {
            if (ext != ".vsz") return NotRecognized("Zip file", "Only Vistumbler's .vsz zip files can be imported.");
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(path);
                // WifiDB's daemon opens this entry by name
                ZipArchiveEntry? entry = zip.GetEntry("data.vs1");
                if (entry is null) return NotRecognized("Vistumbler VSZ", "This VSZ has no data.vs1 inside.");

                using Stream stream = entry.Open();
                var buffer = new byte[HeadBytes];
                int length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                string text = Encoding.UTF8.GetString(buffer, 0, length).TrimStart('﻿');
                return Accepted(text.StartsWith("# Vistumbler", StringComparison.OrdinalIgnoreCase)
                    ? DescribeVistumbler(text).Replace("Vistumbler VS1", "Vistumbler VSZ", StringComparison.Ordinal)
                    : "Vistumbler VSZ");
            }
            catch (InvalidDataException)
            {
                return NotRecognized("Vistumbler VSZ", "The VSZ couldn't be opened as a zip file.");
            }
        }

        private static ImportFileInfo Accepted(string format) => new(ImportFileKind.Accepted, format);

        private static ImportFileInfo NotRecognized(string format, string reason) => new(ImportFileKind.NotRecognized, format, reason);

        private static ImportFileInfo WrongName(string format, string extension) =>
            new(ImportFileKind.NotRecognized, format, $"WifiDB picks its importer from the extension. Rename this file to {extension}.");

        private static bool StartsWith(byte[] head, string prefix)
        {
            if (head.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (head[i] != (byte)prefix[i]) return false;
            }
            return true;
        }

        private static byte[] ReadHead(string path)
        {
            using FileStream file = OpenRead(path);
            var buffer = new byte[HeadBytes];
            int length = file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return buffer[..length];
        }

        private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
}
