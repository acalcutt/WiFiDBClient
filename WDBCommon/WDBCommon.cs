using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WDBAPI;
using WDBSQLite;
namespace WDBCommon
{
    public class WDBCommon
    {
        private WDBAPI.WDBAPI WDBAPIObj;
        private WDBSQLite.WDBSQLite WDBSQLiteObj;

        public string LogPath;
        private string _ThreadName;
        public string ThreadName
        {
            get { return _ThreadName; }
            set {
                _ThreadName = value;
                WDBSQLiteObj.ThreadName = value;
                WDBAPIObj.ThreadName = value;
            }
        }
        public string ObjectName = "WDBCommon";

        public int InternalImportID = 0;

        public bool AutoUploadFolder;
        public string AutoUploadFolderPath;
        public bool ArchiveImports;
        public string ArchiveImportsFolderPath;

        public string DefaultImportNotes;
        public string DefaultImportTitle;
        public bool UseDefaultImportValues;

        public string ServerAddress;
        public string ApiPath;
        public string Username;
        public string ApiKey;
        public string ApiCompiledPath;
        private WDBTraceLog.TraceLog TraceLogObj;

        public WDBCommon(WDBSQLite.WDBSQLite ArgWDBSQliteObj, WDBAPI.WDBAPI WDBAPIObj, WDBTraceLog.TraceLog WDBTraceLogObj)
        {
            WDBTraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: WDBCommon()");
            this.WDBAPIObj = WDBAPIObj;
            TraceLogObj = WDBTraceLogObj;
            WDBSQLiteObj = ArgWDBSQliteObj;
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: WDBCommon()");
        }
        
        public void initApi()
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Function Call: Init API.");
            WDBAPIObj.ApiCompiledPath = ApiCompiledPath;

            //Debug.WriteLine(_ThreadName);
            WDBSQLiteObj.ThreadName = _ThreadName;
            WDBAPIObj.ThreadName = _ThreadName;
            WDBAPIObj.ApiKey = ApiKey;
            WDBAPIObj.Username = Username;
            WDBAPIObj.DefaultImportTitle = DefaultImportTitle;
            WDBAPIObj.DefaultImportNotes = DefaultImportNotes;
            WDBAPIObj.UseDefaultImportValues = UseDefaultImportValues;
            WDBAPIObj.LogPath = LogPath;
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Function Call: Init API.");
        }

        ///
        /// Common Fucntions
        ///

        public int IsFileImported(string query, bool LocalOnly = false)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: IsFileImported");
            //Check the Local SQLite DB for the File hash, If it is found, return now.
            if (CheckSQLFileHash(query) == 0)
            {
                if(!LocalOnly) //if set to true, dont Check the WiFiDB API
                {
                    // If the filehash was not found in the Local SQLite DB, check the WDB API.
                    if (CheckFileHash(query) == 0)
                    {
                        TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: IsFileImported");
                        return 0; //Hash not found.
                    }
                    else
                    {
                        TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: IsFileImported");
                        return 1; //Hash found.
                    }
                }else
                {
                    TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: IsFileImported");
                    return 0; // Only Check Local, and Hash was Not Found.
                }
            } else
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: IsFileImported");
                return 2; //Local Hash Found
            }            
        }


        ///
        /// WiFiDB API Functions
        ///
        public int CheckFileHash(string query)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: CheckFileHash");
            string APIResponse = WDBAPIObj.CheckFileHash(query);
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "IsFileImported Response: " + APIResponse);
            string response = WDBAPIObj.ParseApiResponse(APIResponse);
            
            string[] stringSeparators = new string[] { "|~|" };
            string[] split = response.Split(stringSeparators, StringSplitOptions.None);
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "split[1].ToLower(): " + split[1].ToLower());

            if (split[1].ToLower() == "hash not found in wifidb")
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: CheckFileHash: Hash Not Found");
                return 0;
            }
            else
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: CheckFileHash: Hash Found");
                return 1;
            }
        }

        public void GetHashStatus(string query, BackgroundWorker BgWk)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: GetHashStatus");
            BgWk.ReportProgress(0, "");
            BgWk.ReportProgress(100, WDBAPIObj.ParseApiResponse(WDBAPIObj.CheckFileHash(query)));
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: GetHashStatus");
        }
        
        public void GetDaemonStatuses(string query, BackgroundWorker BgWk)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: GetDaemonStatuses");
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Active Server: " + WDBAPIObj.ApiCompiledPath);
            BgWk.ReportProgress(0, "");
            BgWk.ReportProgress(100, WDBAPIObj.ParseApiResponse(WDBAPIObj.ApiGetDaemonStatuses(query)));
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: GetDaemonStatuses");
        }

        public void GetWaiting(string query, BackgroundWorker BgWk)
        {
            
        }

        public List<KeyValuePair<int, string>> ImportFolder(string Path, string ImportTitle, string ImportNotes, BackgroundWorker BW)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: ImportFolder");
            var responses = new List<KeyValuePair<int, string>>();
            try
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Auto Import Folder: " + Path + " -> " + Directory.Exists(Path));
                if (Directory.Exists(Path)) 
                {
                    TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Auto Import Folder: " + Path);
                    var md5 = MD5.Create();

                    string[] extensions = new string[] { "*.vs1", "*.vsz", "*.txt", "*.csv", "*.db", "*.db3", "*.mdb" };

                    // Loop through the file extension types, find them in the provided folder, then import it.
                    foreach (string ext in extensions)
                    {
                        TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Extension that will be used: " + ext);
                        string[] files = Directory.GetFiles(Path, ext);
                        TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "The number of VS1 files: " + files.Length);
                        if (files.Length > 0)
                        {
                            foreach (string file in files)
                            {
                                responses.Add(new KeyValuePair<int, string>(InternalImportID, ImportFile(file, ImportTitle, ImportNotes, BW)));
                            }
                        }
                    }
                }else
                {
                    responses.Add(new KeyValuePair<int, string>(InternalImportID, "error|~|Auto Import Folder Not Found."));
                }
            }
            catch (Exception e)
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "The process failed: " + e.ToString());
            }
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: ImportFolder");
            return responses;
        }

        private void ArchiveImportedFile(string FilePath)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: ArchiveImportedFile");
            if (ArchiveImports && (ArchiveImportsFolderPath != "" || ArchiveImportsFolderPath != null) )
            {
                try
                {
                    string FileName = Path.GetFileName(FilePath);

                    string ArchivedFile = ArchiveImportsFolderPath + "\\" + FileName;

                    File.Move(FilePath, ArchivedFile);
                    TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), FilePath + " was moved to " + ArchivedFile);
                    
                }
                catch (Exception e)
                {
                    TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "The process failed: " + e.ToString());
                }
            }
            else
            {
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Archve Imported Files is disabled.");
            }
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: ArchiveImportedFile");
        }

        public string ImportFile(string FilePath, string ImportTitle, string ImportNotes, BackgroundWorker BW)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: ImportFile");
            string response;
            byte[] hashBytes;
            string hashish;
            var md5 = MD5.Create();
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Open Import File: " + FilePath);
            using (var inputFileStream = File.Open(FilePath, FileMode.Open))
            {
                hashBytes = md5.ComputeHash(inputFileStream);
                hashish = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Import File Hash: " + hashish);
            }
            int IsFileImportedResult = IsFileImported(hashish);

            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "IsFileImportedResult :" + IsFileImportedResult);
            if (IsFileImportedResult == 0)
            {
                BW.ReportProgress(0, "newrow|~|" + FilePath + "|~|" + hashish);
                string RawResponse = WDBAPIObj.ApiImportFile(FilePath, ImportTitle, ImportNotes);

                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), RawResponse);

                response = WDBAPIObj.ParseApiResponse(RawResponse);

                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Parse Response Result: " + response);

                BW.ReportProgress(0, response + "::" + FilePath);
                ArchiveImportedFile(FilePath);
            }
            else if (IsFileImportedResult == 2)
            {
                response = "error|~|Already Local Imported.-~-" + FilePath;
                BW.ReportProgress(0, response);
            }
            else
            { 
                response = "error|~|Already Imported.-~-" + FilePath;
                BW.ReportProgress(0, response);
            }
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "File Import Response return: " + response);
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: ImportFile");
            return response;
        }




        ///
        /// SQLite Functions.
        ///

        public int CheckSQLFileHash(string query)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: CheckSQLFileHash");
            SQLiteCommand cmd;
            cmd = new SQLiteCommand(WDBSQLiteObj.conn);
            int ret = 0;
            cmd.CommandText = @"SELECT `ID` FROM `ImportView` WHERE `FileHash` = ?";

            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "FileHash query: " + query);

            var filehash = cmd.CreateParameter();
            filehash.Value = query;
            cmd.Parameters.Add(filehash);

            SQLiteDataReader reader;
            reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                Debug.WriteLine("CheckSQLFileHash: " + reader["ID"].ToString() );
                if(Int32.Parse(reader["ID"].ToString()) > 0)
                {
                    ret = 1;
                }
            }
            cmd.Dispose();
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: CheckSQLFileHash");
            return ret;
        }

        public List<ImportRow> GetImportRows()
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: GetImportRows");
            string ImportID;
            SQLiteCommand cmd;
            SQLiteDataReader reader;

            List<ImportRow> ImportRows = new List<ImportRow>();
            cmd = new SQLiteCommand(WDBSQLiteObj.conn);
            cmd.CommandText = @"SELECT * FROM `ImportView`";
            
            reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ImportRow ImportRowObj = new ImportRow();
                if (reader["ImportID"].ToString() == "")
                {
                    ImportID = "0";
                }else
                {
                    ImportID = reader["ImportID"].ToString();
                }
                TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "ImportID For IntParse: "+ImportID);
                ImportRowObj.ImportID = Int32.Parse(ImportID);
                ImportRowObj.Username = reader["Username"].ToString();
                ImportRowObj.ImportTitle = reader["ImportTitle"].ToString();
                ImportRowObj.DateTime = reader["Date_Time"].ToString();
                ImportRowObj.FileSize = reader["FileSize"].ToString();
                ImportRowObj.FileName = reader["FileName"].ToString();
                ImportRowObj.FileHash = reader["FileHash"].ToString();
                ImportRowObj.Status = reader["Status"].ToString();
                ImportRowObj.Message = reader["Message"].ToString();

                ImportRows.Add(ImportRowObj);

            }
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: GetImportRows");
            return ImportRows;
        }
        
        public void InsertImportRow(ImportRow ImportRowObj)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: InsertImportRow");
            SQLiteCommand cmd;
            cmd = new SQLiteCommand(WDBSQLiteObj.conn);
            cmd.CommandText = @"INSERT INTO `ImportView` 
(`Username`, `Date_Time`, `FileSize`, `FileName`, `FileHash`, `Status`, `Message`) 
VALUES ( ?, ?, ?, ?, ?, ?, ?)";
            var Username = cmd.CreateParameter();
            Username.Value = ImportRowObj.Username;
            cmd.Parameters.Add(Username);

            var Date_Time = cmd.CreateParameter();
            Date_Time.Value = ImportRowObj.DateTime;
            cmd.Parameters.Add(Date_Time);

            var FileSize = cmd.CreateParameter();
            FileSize.Value = ImportRowObj.FileSize;
            cmd.Parameters.Add(FileSize);

            var FileName = cmd.CreateParameter();
            FileName.Value = ImportRowObj.FileName;
            cmd.Parameters.Add(FileName);

            var FileHash = cmd.CreateParameter();
            FileHash.Value = ImportRowObj.FileHash;
            cmd.Parameters.Add(FileHash);

            var Status = cmd.CreateParameter();
            Status.Value = ImportRowObj.Status;
            cmd.Parameters.Add(Status);

            var Message = cmd.CreateParameter();
            Message.Value = ImportRowObj.Message;
            cmd.Parameters.Add(Message);

            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "cmd.ExecuteNonQuery(): " + cmd.ExecuteNonQuery().ToString());

            cmd.Dispose();
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: InsertImportRow");
        }

        public void UpdateImportRow(ImportRow ImportRowObj)
        {
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "Start Call: UpdateImportRow");
            SQLiteCommand cmd;
            cmd = new SQLiteCommand(WDBSQLiteObj.conn);
            cmd.CommandText = @"UPDATE `ImportView` SET 
`ImportID` = ?,
`ImportTitle` = ?,
`Status` = ?,
`Message` = ?
WHERE `FileHash` = ?";

            var ImportIDParm = cmd.CreateParameter();
            ImportIDParm.Value = ImportRowObj.ImportID;
            cmd.Parameters.Add(ImportIDParm);

            var ImportTitleParm = cmd.CreateParameter();
            ImportTitleParm.Value = ImportRowObj.ImportTitle;
            cmd.Parameters.Add(ImportTitleParm);

            var StatusParm = cmd.CreateParameter();
            StatusParm.Value = ImportRowObj.Status;
            cmd.Parameters.Add(StatusParm);

            var MessageParm = cmd.CreateParameter();
            MessageParm.Value = ImportRowObj.Message;
            cmd.Parameters.Add(MessageParm);

            var FileHashParm = cmd.CreateParameter();
            FileHashParm.Value = ImportRowObj.FileHash.ToUpper();
            cmd.Parameters.Add(FileHashParm);

            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "cmd.ExecuteNonQuery(): " + cmd.ExecuteNonQuery().ToString());

            cmd.Dispose();
            TraceLogObj.WriteToLog(_ThreadName, ObjectName, GetCurrentMethod(), "End Call: UpdateImportRow");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string GetCurrentMethod()
        {
            StackTrace st = new StackTrace();
            StackFrame sf = st.GetFrame(1);

            return sf.GetMethod().Name;
        }

    }
}
