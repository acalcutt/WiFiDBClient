namespace WDBSQLite
{
    /// <summary>The values of <see cref="ImportRow.Status"/>.</summary>
    public static class ImportStatus
    {
        public const string Uploading = "Uploading";
        public const string Waiting = "Waiting";
        public const string Importing = "Importing";
        public const string Finished = "Finished";
        public const string Unknown = "Unknown";

        /// <summary>WifiDB took the file and its importer then gave up on it.</summary>
        public const string Bad = "Failed on WifiDB";

        /// <summary>WifiDB answered and refused the file.</summary>
        public const string Rejected = "Rejected";

        /// <summary>WifiDB couldn't be reached or the file couldn't be read: worth trying again.</summary>
        public const string Error = "Error";

        /// <summary>A file WifiDB's API has no way to take.</summary>
        public const string Unsupported = "Unsupported";

        /// <summary>Whether WifiDB may still move this file along, so it's worth asking again.</summary>
        public static bool IsPending(string status) => status is Waiting or Importing;

        /// <summary>Whether choosing this file again should try again, rather than showing the row it has.</summary>
        public static bool CanRetry(string status) => status is Error or Rejected or Unsupported;
    }

    /// <summary>A file's row in the Uploaded Files list. <see cref="FileHash"/> is the upper-case MD5 and is its key.</summary>
    public class ImportRow
    {
        public int ImportID { get; set; }
        public string Username { get; set; } = "";
        public string ImportTitle { get; set; } = "";
        public string DateTime { get; set; } = "";
        public string FileSize { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Format { get; set; } = "";
        public string FileHash { get; set; } = "";
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";

        public ImportRow Copy() => (ImportRow)MemberwiseClone();
    }
}
