using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using WDBCommon;
using Xunit;

namespace WiFiDBUploader.Tests;

public sealed class ImportFileTypeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wdb-types-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData("a.vs1", "# Vistumbler VS1 - Detailed Export Version 4.0\r\n", "Vistumbler VS1 4.0")]
    [InlineData("a.VS1", "# Vistumbler VS1 - Detailed Export Version 4.1\r\n# Created By: VistumblerCS\r\n", "Vistumbler VS1 4.1 (VistumblerCS)")]
    [InlineData("a.vs1", "﻿# Vistumbler VS1 - Detailed Export Version 4.1\r\n# Created By: VistumblerMAUI\r\n", "Vistumbler VS1 4.1 (VistumblerMAUI)")]
    [InlineData("a.txt", "# Vistumbler TXT - Export Version 1.0\r\n", "Vistumbler TXT 1.0")]
    [InlineData("a.csv", "SSID,BSSID,MANUFACTURER,SIGNAL,High Signal,RSSI\r\n", "Vistumbler CSV")]
    [InlineData("a.csv", "SSID,BSSID,MANUFACTURER,HIGHEST SIGNAL W/GPS,AUTHENTICATION\r\n", "Vistumbler CSV")]
    [InlineData("a.csv", "WigleWifi-1.6,appRelease=VistumblerCS,model=PC\r\nMAC,SSID\r\n", "WiGLE CSV (VistumblerCS)")]
    [InlineData("a.csv", "WigleWifi-1.6,appRelease=VistumblerMAUI,model=PC\r\n", "WiGLE CSV (VistumblerMAUI)")]
    [InlineData("a.csv", "BSSID,SSID,AuthMode,channel,frequency,RSSI\r\n", "SWardriving CSV")]
    [InlineData("a.netxml", "<?xml version=\"1.0\"?>\n<detection-run kismet-version=\"x\">\n", "Kismet NetXML")]
    [InlineData("a.vs1", "something that doesn't say", "Vistumbler VS1")]
    public void Accepts_what_the_apps_write(string name, string text, string format)
    {
        ImportFileInfo info = ImportFileType.Detect(Write(name, text));
        Assert.Equal(ImportFileKind.Accepted, info.Kind);
        Assert.Equal(format, info.Format);
    }

    [Theory]
    [InlineData("notes.txt", "shopping list")]
    [InlineData("data.csv", "a,b,c\r\n1,2,3\r\n")]
    [InlineData("a.txt", "SSID,BSSID,MANUFACTURER,SIGNAL\r\n")]           // the daemon would read it as a VS1
    [InlineData("a.csv", "# Vistumbler VS1 - Detailed Export Version 4.0\r\n")]
    [InlineData("a.xml.netxml", "<?xml version=\"1.0\"?>\n<other/>")]
    public void Passes_over_anything_else(string name, string text) =>
        Assert.Equal(ImportFileKind.NotRecognized, ImportFileType.Detect(Write(name, text)).Kind);

    [Fact]
    public void An_empty_file_is_not_recognized() =>
        Assert.Equal(ImportFileKind.NotRecognized, ImportFileType.Detect(Write("a.vs1", "")).Kind);

    [Fact]
    public void Accepts_a_binary_ns1_only_as_ns1()
    {
        Assert.Equal(ImportFileKind.Accepted, ImportFileType.Detect(Write("a.ns1", "NetS\x0c\0\0\0")).Kind);
        Assert.Equal(ImportFileKind.NotRecognized, ImportFileType.Detect(Write("b.txt", "NetS\x0c\0\0\0")).Kind);
    }

    [Fact]
    public void Netstumbler_text_export_is_explained() =>
        Assert.Equal(ImportFileKind.NotUploadable, ImportFileType.Detect(Write("a.txt", "# $Creator: Network Stumbler Version 0.4.0\r\n")).Kind);

    [Fact]
    public void Reads_the_version_inside_a_vsz()
    {
        string path = Path.Combine(_dir, "a.vsz");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("data.vs1").Open()))
        {
            writer.Write("# Vistumbler VS1 - Detailed Export Version 4.1\r\n# Created By: VistumblerMAUI\r\n");
        }
        ImportFileInfo info = ImportFileType.Detect(path);
        Assert.Equal(ImportFileKind.Accepted, info.Kind);
        Assert.Equal("Vistumbler VSZ 4.1 (VistumblerMAUI)", info.Format);
    }

    [Fact]
    public void A_vsz_without_data_vs1_is_not_recognized()
    {
        string path = Path.Combine(_dir, "a.vsz");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("other.txt");
        Assert.Equal(ImportFileKind.NotRecognized, ImportFileType.Detect(path).Kind);
    }

    [Fact]
    public void Looks_inside_gzip_for_wigle()
    {
        string Gz(string name, string text)
        {
            string path = Path.Combine(_dir, name);
            using var gzip = new GZipStream(File.Create(path), CompressionMode.Compress);
            gzip.Write(Encoding.UTF8.GetBytes(text));
            return path;
        }
        Assert.Equal(ImportFileKind.Accepted, ImportFileType.Detect(Gz("a.gz", "WigleWifi-1.4,appRelease=2.53\r\n")).Kind);
        Assert.Equal(ImportFileKind.NotRecognized, ImportFileType.Detect(Gz("b.gz", "not wigle")).Kind);
    }

    private string Sqlite(string name, string table)
    {
        string path = Path.Combine(_dir, name);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {table} (id INTEGER)";
        command.ExecuteNonQuery();
        return path;
    }

    [Theory]
    [InlineData("a.db3", "networks", ImportFileKind.Accepted)]
    [InlineData("a.db", "wifi", ImportFileKind.Accepted)]
    [InlineData("session.db3", "DbAccessPoint", ImportFileKind.NotUploadable)]
    [InlineData("a.kismet", "KISMET", ImportFileKind.NotUploadable)]
    [InlineData("other.db3", "stuff", ImportFileKind.NotRecognized)]
    public void Tells_sqlite_files_apart(string name, string table, ImportFileKind expected) =>
        Assert.Equal(expected, ImportFileType.Detect(Sqlite(name, table)).Kind);
}
