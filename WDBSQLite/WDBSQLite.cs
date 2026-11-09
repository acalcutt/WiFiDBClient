using Microsoft.Data.Sqlite;

namespace WDBSQLite
{
    /// <summary>The uploader's list of files, in a SQLite file. Called from the UI and the upload tasks, so every call locks.</summary>
    public sealed class WDBSQLite : IDisposable
    {
        private const string Columns = "ImportID, Username, ImportTitle, Date_Time, FileSize, FileName, Format, FileHash, Status, Message";

        private readonly object _gate = new();
        private readonly SqliteConnection _conn;
        private bool _disposed;

        public WDBSQLite(string path)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            _conn.Open();
            Execute(@"CREATE TABLE IF NOT EXISTS ImportView (
ID INTEGER PRIMARY KEY AUTOINCREMENT,
ImportID INT,
Username VARCHAR(255),
ImportTitle VARCHAR(255),
Date_Time VARCHAR(255),
FileSize VARCHAR(255),
FileName VARCHAR(255),
Format VARCHAR(255),
FileHash VARCHAR(255),
Status VARCHAR(255),
Message VARCHAR(255))");
            // A database from before the Format column
            if (!HasColumn("ImportView", "Format")) Execute("ALTER TABLE ImportView ADD COLUMN Format VARCHAR(255)");
        }

        public List<ImportRow> GetImportRows()
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $"SELECT {Columns} FROM ImportView ORDER BY ID";
                using var reader = cmd.ExecuteReader();
                var rows = new List<ImportRow>();
                while (reader.Read()) rows.Add(Read(reader));
                return rows;
            }
        }

        public ImportRow? FindByHash(string fileHash)
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $"SELECT {Columns} FROM ImportView WHERE FileHash = $hash ORDER BY ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$hash", fileHash.ToUpperInvariant());
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? Read(reader) : null;
            }
        }

        /// <summary>Adds the row, or updates the row its hash already has.</summary>
        public void SaveRow(ImportRow row)
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"UPDATE ImportView SET ImportID = $id, Username = $user, ImportTitle = $title, Date_Time = $date, FileSize = $size,
FileName = $name, Format = $format, Status = $status, Message = $message WHERE FileHash = $hash";
                Bind(cmd, row);
                if (cmd.ExecuteNonQuery() > 0) return;

                cmd.CommandText = @"INSERT INTO ImportView (ImportID, Username, ImportTitle, Date_Time, FileSize, FileName, Format, FileHash, Status, Message)
VALUES ($id, $user, $title, $date, $size, $name, $format, $hash, $status, $message)";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Empties the list, which is what closing the app without "Exit and Save DB" does.</summary>
        public void Clear() => Execute("DELETE FROM ImportView; DELETE FROM sqlite_sequence WHERE name = 'ImportView';");

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _conn.Dispose();
            }
        }

        private static void Bind(SqliteCommand cmd, ImportRow row)
        {
            cmd.Parameters.AddWithValue("$id", row.ImportID);
            cmd.Parameters.AddWithValue("$user", row.Username);
            cmd.Parameters.AddWithValue("$title", row.ImportTitle);
            cmd.Parameters.AddWithValue("$date", row.DateTime);
            cmd.Parameters.AddWithValue("$size", row.FileSize);
            cmd.Parameters.AddWithValue("$name", row.FileName);
            cmd.Parameters.AddWithValue("$format", row.Format);
            cmd.Parameters.AddWithValue("$hash", row.FileHash.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$status", row.Status);
            cmd.Parameters.AddWithValue("$message", row.Message);
        }

        private static ImportRow Read(SqliteDataReader r) => new()
        {
            ImportID = r.IsDBNull(0) ? 0 : r.GetInt32(0),
            Username = Text(r, 1),
            ImportTitle = Text(r, 2),
            DateTime = Text(r, 3),
            FileSize = Text(r, 4),
            FileName = Text(r, 5),
            Format = Text(r, 6),
            FileHash = Text(r, 7),
            Status = Text(r, 8),
            Message = Text(r, 9),
        };

        private static string Text(SqliteDataReader r, int ordinal) => r.IsDBNull(ordinal) ? "" : r.GetString(ordinal);

        private bool HasColumn(string table, string column)
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table})";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        private void Execute(string sql)
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
