using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using WDBSQLite;

namespace WiFiDBUploader
{
    public partial class WiFiDBUploadMainForm : Form
    {
        private WDBAPI.WDBAPI WDBAPIObj;
        private WDBCommon.WDBCommon WDBCommonObj;
        private WDBTraceLog.TraceLog WDBTraceLogObj;
        private WDBSQLite.WDBSQLite WDBSQLiteObj;

        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Timer timer2;
        private System.Windows.Forms.Timer timer3;

        private List<ServerObj> ServerList;
        private List<BackgroundWorker> ImportUpdatesBackgroungWorkersList = new List<BackgroundWorker>();
        private List<QueryArguments> ImportUpdatesQueryArgsList = new List<QueryArguments>();
        private List<BackgroundWorker> DaemonBackgroungWorkersList = new List<BackgroundWorker>();
        private List<QueryArguments> DaemonQueryArgsList = new List<QueryArguments>();

        private int    ImportNextID = 0;
        private int    DaemonNextID = 0;
        private int    FileImportNextID = 0;
        private int    FolderImportNextID = 0;

        private bool   AutoUploadFolder;
        private string AutoUploadFolderPath;

        private bool   ArchiveImports;
        private string ArchiveImportsFolderPath;

        private int    AutoCloseTimerSeconds;
        private bool   AutoCloseEnable;

        private string DefaultImportNotes;
        private string DefaultImportTitle;
        private bool   DefaultImportTitleIsDateTime;
        private bool   UseDefaultImportValues;
        private bool   UseAutoDateTimeTitle;

        private string SQLiteDBFile;
        private string SQLiteDBPath;
        private string SQLiteFile;

        private bool   ImportUpdateThreadEnable;
        private bool   DaemonUpdateThreadEnable;
        private bool   AutoImportThreadEnable;
        private int    ImportUpdateThreadSeconds;
        private int    DaemonUpdateThreadSeconds;
        private int    AutoImportThreadSeconds;


        private string LogPath;
        private bool   TraceLogEnable = false;
        private bool   TogglePerRun = false;
        private bool   PerRunRotate;
        private bool   DEBUG;

        private string SelectedServer;
        private string ServerAddress;
        private string ApiPath;
        private string Username;
        private string ApiKey;
        private string ApiCompiledPath;

        private string ThreadName = "Main";
        private string ObjectName = "Main";

        private string WDBVersionNumber = "1.2.1";

        private struct QueryArguments
        {
            public QueryArguments(int QueryID, string Query)
                : this()
            {
                this.QueryID = QueryID;
                this.Query = Query;
            }
            public int QueryID { get; set; }
            public string Query { get; set; }
            public List<KeyValuePair<int, string>> Result { get; set; }
        }

        public WiFiDBUploadMainForm()
        {
            LoadSettings();
            InitClasses();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Loaded Settngs and Init Classes complete.");

            InitializeComponent();
            LoadDbDataIntoUI();
            InitTimer();
            AutoUploadCheck();
        }

        private void LoadDbDataIntoUI()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: LoadDbDataIntoUI");
            List<ImportRow> ImportedRows = WDBCommonObj.GetImportRows();
            foreach ( ImportRow Row in ImportedRows)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "\n------------------\n------------------\nCreate ROW from SQL: " + Row.ImportID.ToString() + " |=| " + Row.Username + " |=| " + Row.ImportTitle + " |=| " + Row.Message + " |=| " + Row.Status + "\n------------------\n------------------\n");
                string[] row = { Row.ImportID.ToString(), Row.Username, Row.ImportTitle, Row.DateTime,
                    Row.FileSize, Row.FileName, Row.FileHash, Row.Status, Row.Message };

                var listViewItemNew = new ListViewItem(row);
                listView1.Items.Add(listViewItemNew);
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: LoadDbDataIntoUI");
        }

        private void AutoUploadCheck()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: AutoUploadCheck");
            if (AutoUploadFolder == true && SelectedServer != null)
            {
                string[] GUIValues = GetImportValues();
                string Query = AutoUploadFolderPath + "|"
                + GUIValues[0] + "|"
                + GUIValues[1];

                StartFolderImport(Query);
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: AutoUploadCheck");
        }
        
        private void InitClasses()
        {
            if( (WDBTraceLogObj != null) || (TogglePerRun == true))
            {
                WDBTraceLogObj.Dispose();
                this.TogglePerRun = false;
            }
            
            WDBTraceLogObj = new WDBTraceLog.TraceLog(LogPath, TraceLogEnable, PerRunRotate);
            WDBSQLiteObj = new WDBSQLite.WDBSQLite(SQLiteDBPath + "\\" +SQLiteDBFile, "uploader", WDBTraceLogObj);
            WDBAPIObj = new WDBAPI.WDBAPI(WDBTraceLogObj);
            WDBCommonObj = new WDBCommon.WDBCommon(WDBSQLiteObj, WDBAPIObj, WDBTraceLogObj);
            
            WDBCommonObj.AutoUploadFolder = AutoUploadFolder;
            WDBCommonObj.AutoUploadFolderPath = AutoUploadFolderPath;
            WDBCommonObj.ArchiveImports = ArchiveImports;
            WDBCommonObj.ArchiveImportsFolderPath = ArchiveImportsFolderPath;
            WDBCommonObj.LogPath = LogPath;

            WDBCommonObj.DefaultImportNotes = DefaultImportNotes;
            WDBCommonObj.DefaultImportTitle = DefaultImportTitle;
            WDBCommonObj.UseDefaultImportValues = UseDefaultImportValues;

            WDBCommonObj.ServerAddress = ServerAddress;
            WDBCommonObj.ApiPath = ApiPath;
            WDBCommonObj.Username = Username;
            WDBCommonObj.ApiKey = ApiKey;
            WDBCommonObj.ApiCompiledPath = ApiCompiledPath;
            
            WDBCommonObj.initApi();
        }

        private void InitTimer()
        {
            if (ApiCompiledPath == null)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Not running background threads till there is a server selected. whats the point if there is no server?");
            }
            else
            {


                /*
                Import Update Background Thread Init.
                */

                if (ImportUpdateThreadEnable)
                {
                    if (timer1 != null)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Stopping Import update background threads.");
                        timer1.Stop();
                        timer1.Dispose();
                        timer1 = null;
                        foreach(BackgroundWorker BW in ImportUpdatesBackgroungWorkersList)
                        {
                            BW.CancelAsync();
                            BW.Dispose();
                        }
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Starting Import update background Timer and Workers.");
                    timer1 = new System.Windows.Forms.Timer();
                    timer1.Tick += new EventHandler(CheckForImportUpdates);
                    timer1.Interval = (ImportUpdateThreadSeconds * 1000); // in miliseconds
                    timer1.Start();
                    //CheckForImportUpdates(new object(), new EventArgs());
                }



                /*
                Daemon Update Background Thread Init.
                */

                if (DaemonUpdateThreadEnable)
                {
                    StartGetDaemonStats(); //prep the tables.
                    if (timer2 != null)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Stopping Daemon update background Timer and Workers.");
                        timer2.Stop();
                        timer2.Dispose();
                        timer2 = null;
                        foreach (BackgroundWorker BW in DaemonBackgroungWorkersList)
                        {
                            BW.CancelAsync();
                            BW.Dispose();
                        }
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Starting Daemon update background Timer.");
                    timer2 = new System.Windows.Forms.Timer();
                    timer2.Tick += new EventHandler(CheckForDaemonUpdates);
                    timer2.Interval = (DaemonUpdateThreadSeconds * 1000); // in miliseconds
                    timer2.Start();
                }


                /*
                Auto Import Background Thread Init.
                */

                if (AutoImportThreadEnable)
                {
                    if (timer3 != null)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Stopping Auto Import background Timer and Workers.");
                        timer3.Stop();
                        timer3.Dispose();
                        timer3 = null;
                        foreach (BackgroundWorker BW in DaemonBackgroungWorkersList)
                        {
                            BW.CancelAsync();
                            BW.Dispose();
                        }
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Starting Auto Import background Timer.");
                    timer3 = new System.Windows.Forms.Timer();
                    timer3.Tick += new EventHandler(AutoBGUploadCheck);
                    timer3.Interval = (AutoImportThreadSeconds * 1000); // in miliseconds
                    timer3.Start();
                }


            }
        }

        private void UpdateRegKeys()
        {
            Microsoft.Win32.RegistryKey rootKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\WiFiDB\\Uploader", true);
            if (rootKey != null)
            {
                string version = (string)rootKey.GetValue("Version");
                if (version != WDBVersionNumber)
                {
                    rootKey.SetValue("Version", WDBVersionNumber);
                    rootKey.SetValue("DefaultImportNotes", "WDB Uploader\nVersion: " + WDBVersionNumber);
                }

                string AutoImportThreadEnabled = (string)rootKey.GetValue("AutoImportThreadEnabled");
                if (AutoImportThreadEnabled == null)
                {
                    rootKey.SetValue("AutoImportThreadEnable", "False");
                }

                int? AutoImportThreadSeconds = (int?)rootKey.GetValue("AutoImportThreadSeconds");
                if (AutoImportThreadSeconds == null)
                {
                    rootKey.SetValue("AutoImportThreadSeconds", 30);
                }
            }
        }

        private void CreateRegistryKeys(Microsoft.Win32.RegistryKey rootKey)
        {
            Microsoft.Win32.RegistryKey ServersKey;
            Microsoft.Win32.RegistryKey DefaultServerKey;

            rootKey.SetValue("Version", WDBVersionNumber);
            rootKey.SetValue("DefaultImportTitle", "%DATETIME%");
            rootKey.SetValue("DefaultImportTitleIsDateTime", "True");
            rootKey.SetValue("DefaultImportNotes", "WDB Uploader\nVersion: " + WDBVersionNumber);
            rootKey.SetValue("UseDefaultImportValues", "False");
            rootKey.SetValue("UseAutoDateTimeTitle", "False");
            rootKey.SetValue("AutoUploadFolder", "False");
            rootKey.SetValue("AutoUploadFolderPath", "");
            rootKey.SetValue("ArchiveImports", "False");
            rootKey.SetValue("ArchiveImportsFolderPath", "");
            rootKey.SetValue("AutoCloseEnable", "False");
            rootKey.SetValue("AutoCloseTimerSeconds", "30");
            rootKey.SetValue("SQLiteDBPath", ".\\DB");
            rootKey.SetValue("SQLiteDBFile", "Uploader.db3");
            rootKey.SetValue("ImportUpdateThreadEnable", "True");
            rootKey.SetValue("ImportUpdateThreadSeconds", 30);
            rootKey.SetValue("DaemonUpdateThreadEnable", "True");
            rootKey.SetValue("DaemonUpdateThreadSeconds", 60);
            rootKey.SetValue("AutoImportThreadEnable", "False");
            rootKey.SetValue("AutoImportThreadSeconds", 30);
            rootKey.SetValue("TraceLogEnable", "False");
            rootKey.SetValue("LogPath", ".\\Logs\\");
            rootKey.SetValue("DEBUGEnable", "False");
            
            ServersKey = rootKey.CreateSubKey("Servers");
            DefaultServerKey = ServersKey.CreateSubKey("api.wifidb.net");

            DefaultServerKey.SetValue("ServerAddress", "https://api.wifidb.net");
            DefaultServerKey.SetValue("ApiPath", "/v2/");
            DefaultServerKey.SetValue("Username", "AnonCoward");
            DefaultServerKey.SetValue("ApiKey", "");
            DefaultServerKey.SetValue("Selected", "True");
        }

        private void LoadSettings()
        {
            Microsoft.Win32.RegistryKey rootKey;
            rootKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("SOFTWARE").CreateSubKey("WiFiDB").CreateSubKey("Uploader");
            string[] SubKeys = rootKey.GetSubKeyNames();

            if (SubKeys.Count() == 0)
            {
                CreateRegistryKeys(rootKey);
                LoadSettings();
            }
            else
            {
                UpdateRegKeys();
                foreach (string value in rootKey.GetValueNames())
                {
                    switch (value)
                    {
                        case "AutoUploadFolder":
                            AutoUploadFolder = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "AutoUploadFolderPath":
                            AutoUploadFolderPath = rootKey.GetValue(value).ToString();
                            break;
                        case "AutoCloseTimerSeconds":
                            AutoCloseTimerSeconds = Int32.Parse(rootKey.GetValue(value).ToString());
                            break;
                        case "AutoCloseEnable":
                            AutoCloseEnable = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "ArchiveImports":
                            ArchiveImports = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "ArchiveImportsFolderPath":
                            ArchiveImportsFolderPath = rootKey.GetValue(value).ToString();
                            break;
                        case "DefaultImportNotes":
                            DefaultImportNotes = rootKey.GetValue(value).ToString();
                            break;
                        case "DefaultImportTitle":
                            DefaultImportTitle = rootKey.GetValue(value).ToString();
                            break;
                        case "DefaultImportTitleIsDateTime":
                            DefaultImportTitleIsDateTime = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "UseAutoDateTimeTitle":
                            UseAutoDateTimeTitle = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "UseDefaultImportValues":
                            UseDefaultImportValues = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "SQLiteDBFile":
                            SQLiteDBFile = rootKey.GetValue(value).ToString();
                            break;
                        case "SQLiteDBPath":
                            SQLiteDBPath = rootKey.GetValue(value).ToString();
                            break;
                        case "ImportUpdateThreadEnable":
                            ImportUpdateThreadEnable = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "DaemonUpdateThreadEnable":
                            DaemonUpdateThreadEnable = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "AutoImportThreadEnable":
                            AutoImportThreadEnable = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "ImportUpdateThreadSeconds":
                            ImportUpdateThreadSeconds =  Int32.Parse(rootKey.GetValue(value).ToString());
                            break;
                        case "DaemonUpdateThreadSeconds":
                            DaemonUpdateThreadSeconds = Int32.Parse(rootKey.GetValue(value).ToString());
                            break;
                        case "AutoImportThreadSeconds":
                            AutoImportThreadSeconds = Int32.Parse(rootKey.GetValue(value).ToString());
                            break;
                        case "TraceLogEnable":
                            TraceLogEnable = Convert.ToBoolean(rootKey.GetValue(value));
                            Debug.WriteLine("TraceLogEnable: " + TraceLogEnable);
                            break;
                        case "LogPath":
                            LogPath = rootKey.GetValue(value).ToString();
                            break;
                        case "PerRunRotate":
                            PerRunRotate = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                        case "DEBUGEnable":
                            DEBUG = Convert.ToBoolean(rootKey.GetValue(value));
                            break;
                    }
                }
                SQLiteFile = SQLiteDBPath + SQLiteDBFile;
                ServerList = new List<ServerObj>();
                int Increment = 0;
                Microsoft.Win32.RegistryKey ServerSubkeys = rootKey.CreateSubKey("Servers");
                foreach (string subitem in ServerSubkeys.GetSubKeyNames())
                {
                    Microsoft.Win32.RegistryKey ServerKey = ServerSubkeys.CreateSubKey(subitem);

                    ServerObj Server = new ServerObj();

                    Server.ID = Increment;
                    Server.ServerAddress = ServerKey.GetValue("ServerAddress").ToString();
                    Server.ApiPath = ServerKey.GetValue("ApiPath").ToString();
                    Server.Username = ServerKey.GetValue("Username").ToString();
                    Server.ApiKey = ServerKey.GetValue("ApiKey").ToString();
                    Server.Selected = Convert.ToBoolean(ServerKey.GetValue("Selected"));

                    if (Server.Selected)
                    {
                        ServerAddress = Server.ServerAddress.ToString();
                        SelectedServer = ServerAddress.Replace("https://", "").Replace("http://", "");
                        ApiPath = Server.ApiPath.ToString();
                        Username = Server.Username.ToString();
                        ApiKey = Server.ApiKey.ToString();
                        ApiCompiledPath = Server.ServerAddress + Server.ApiPath;
                    }
                    ServerList.Add(Server);
                    Increment++;
                }


                if (ServerAddress == null)
                {
                    MessageBox.Show("There is no selected server. Go to Settings-> WiFiDB Server. Select a server from the drop down, if there is none, add one with the +");
                }
            }
        }

        private void WriteServerSettings()
        {
            /*
                Screw the app.config file, registry is easier to manage.
            */
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: WriteServerSettings");
            Microsoft.Win32.RegistryKey rootKey;
            rootKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("SOFTWARE").CreateSubKey("WiFiDB").CreateSubKey("Uploader").CreateSubKey("Servers");

            List<ServerNameObj> VarNameList = new List<ServerNameObj>();
            foreach (ServerObj server in ServerList)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), server.ServerAddress);

                Microsoft.Win32.RegistryKey ServerKey = rootKey.CreateSubKey( server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "") );
                ServerKey.SetValue("ServerAddress", server.ServerAddress);
                ServerKey.SetValue("ApiPath", server.ApiPath);
                ServerKey.SetValue("Username", server.Username);
                ServerKey.SetValue("ApiKey", server.ApiKey);
                ServerKey.SetValue("Selected", server.Selected);

                ServerNameObj nameObj = new ServerNameObj();
                nameObj.ServerName = server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "");
                VarNameList.Add(nameObj);

            }

            var RegName = rootKey.GetSubKeyNames();
            List<ServerNameObj> RegNameList = new List<ServerNameObj>();

            foreach ( string subkey in RegName)
            {
                ServerNameObj nameObj = new ServerNameObj();
                nameObj.ServerName = subkey;
                RegNameList.Add(nameObj);
            }
            
            var list3 = RegNameList.Except(VarNameList, new IdComparer()).ToList();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Servers not in the list now:");
            foreach(ServerNameObj ServerName in list3)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), ServerName.ServerName);
                rootKey.DeleteSubKeyTree(ServerName.ServerName);
            }

            LoadSettings();
            InitClasses();
            InitTimer();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: WriteServerSettings");
        }

        private void WriteGlobalSettings()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: WriteGlobalSettings");
            /* Screw the app.config file, registry is easier to manage. */
            Microsoft.Win32.RegistryKey rootKey;
            rootKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("SOFTWARE").CreateSubKey("WiFiDB").CreateSubKey("Uploader");

            rootKey.SetValue("AutoUploadFolder", AutoUploadFolder);
            rootKey.SetValue("AutoCloseTimerSeconds", AutoCloseTimerSeconds);
            rootKey.SetValue("AutoCloseEnable", AutoCloseEnable);
            rootKey.SetValue("AutoUploadFolderPath", AutoUploadFolderPath);
            rootKey.SetValue("ArchiveImports", ArchiveImports);
            rootKey.SetValue("ArchiveImportsFolderPath", ArchiveImportsFolderPath);
            rootKey.SetValue("DefaultImportNotes", DefaultImportNotes);
            rootKey.SetValue("DefaultImportTitle", DefaultImportTitle);
            rootKey.SetValue("UseAutoDateTimeTitle", UseAutoDateTimeTitle);
            rootKey.SetValue("UseDefaultImportValues", UseDefaultImportValues);
            rootKey.SetValue("SQLiteFile", SQLiteFile);
            rootKey.SetValue("LogPath", LogPath);
            rootKey.SetValue("ImportUpdateThreadEnable", ImportUpdateThreadEnable);
            rootKey.SetValue("DaemonUpdateThreadEnable", DaemonUpdateThreadEnable);
            rootKey.SetValue("ImportUpdateThreadSeconds", ImportUpdateThreadSeconds);
            rootKey.SetValue("DaemonUpdateThreadSeconds", DaemonUpdateThreadSeconds);
            rootKey.SetValue("TraceLogEnable", TraceLogEnable);
            rootKey.SetValue("DEBUGEnable", DEBUG);
            rootKey.SetValue("PerRunRotate", PerRunRotate);

            LoadSettings();
            InitClasses();
            InitTimer();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: WriteGlobalSettings");
        }
        
        private void InsertNewListViewRow(string[] split, string Type)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: InsertNewListViewRow");
            WDBSQLite.ImportRow ImportRowObj = new WDBSQLite.ImportRow();

            string Date_Time = DateTime.Now.ToString("yyyy-MM-dd    HH:mm:ss");
            string StatusStr;
            string MessageStr;
            string FileSizeString;
            string FileHash;
            string FilePath;

            if (Type == "NewRow")
            {
                FileSizeString = new FileInfo(split[1]).Length.ToString();
                FilePath = split[1];
                FileHash = split[2].ToUpper();
                StatusStr = "Uploading";
                MessageStr = "Uploading File to WiFiDB...";
            }
            else if (Type == "Error")
            {
                string[] stringSep1 = new string[] { "::" };
                string[] stringSep2 = new string[] { "-~-" };
                string[] stringSep3 = new string[] { ": " };

                string[] items_err = split[1].Split(stringSep2, StringSplitOptions.None);
                string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                string[] SplitData1 = SplitData[0].Split(stringSep3, StringSplitOptions.None);

                FilePath = SplitData[1];
                FileHash = SplitData1[1].ToUpper();
                FileSizeString = new FileInfo(FilePath).Length.ToString();

                StatusStr = "Error";
                MessageStr = split[1];
            }
            else
            {
                string[] stringSep1 = new string[] { "::" };
                string[] stringSep2 = new string[] { "-~-" };
                string[] stringSep3 = new string[] { ": " };

                string[] items_err = split[1].Split(stringSep2, StringSplitOptions.None);
                string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                string[] SplitData1 = SplitData[1].Split(stringSep3, StringSplitOptions.None);

                FilePath = SplitData[1];
                FileHash = SplitData1[1].ToUpper();
                FileSizeString = new FileInfo(FilePath).Length.ToString();

                StatusStr = "Error";
                MessageStr = split[1];
            }

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "ListView Update Type: " + Type);

            ImportRowObj.Username = Username;
            ImportRowObj.ImportTitle = "";
            ImportRowObj.DateTime = Date_Time;
            ImportRowObj.FileSize = FileSizeString;
            ImportRowObj.FileName = FilePath;
            ImportRowObj.FileHash = FileHash;
            ImportRowObj.Status = StatusStr;
            ImportRowObj.Message = MessageStr;

            WDBCommonObj.InsertImportRow(ImportRowObj); // Insert import information into SQLite.

            string[] row = { "", Username, "", Date_Time, FileSizeString, FilePath, FileHash, StatusStr, MessageStr };
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "\n------------------\n------------------\n" + Type + ": " + " |=| " + Username + " |=| " + " |=| " + Date_Time + " |=| "
                + FileSizeString + " |=| " + FilePath + " |=| " + FileHash + " |=| " + " |=| " + StatusStr + " |=| " + MessageStr + " \n------------------\n------------------\n");

            var listViewItemNew = new ListViewItem(row);
            listView1.Items.Add(listViewItemNew);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: InsertNewListViewRow");
        }




        /*
            Background init Funtions.
        */

        private void AutoBGUploadCheck(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: AutoBGUploadCheck");
            if (AutoUploadFolder == true && SelectedServer != null)
            {
                string[] GUIValues = GetImportValues();
                string Query = AutoUploadFolderPath + "|"
                + GUIValues[0] + "|"
                + GUIValues[1];

                StartFolderImport(Query);
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: AutoBGUploadCheck");
        }

        private void CheckForDaemonUpdates(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: CheckForDaemonUpdates");
            StartGetDaemonStats();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: CheckForDaemonUpdates");
        }

        private void CheckForImportUpdates(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: CheckForImportUpdates");
            List<string> HashList = new List<string>();
            foreach (ListViewItem item in listView1.Items)
            {
                if (item.SubItems[7].Text.ToLower() == "waiting" || item.SubItems[7].Text.ToLower() == "uploading" || item.SubItems[7].Text.ToLower() == "error")
                {
                    HashList.Add(item.SubItems[6].Text);
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "CheckForImportUpdates: item.SubItems[6].Text: " + item.SubItems[6].Text);
                }
            }
            StartUpdateWiaitng(HashList.ToArray());
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: CheckForImportUpdates");
        }
        
        private void StartUpdateDaemonStats()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: StartUpdateDaemonStats");
            QueryArguments args = new QueryArguments(DaemonNextID++, "");
            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker_UpdateDaemonStatsDoWork);
            backgroundWorker1.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_GetDaemonListViewProgressChanged);
            backgroundWorker1.WorkerReportsProgress = true;
            //backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_ImportWorkerCompleted);
            backgroundWorker1.RunWorkerAsync(args);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: StartUpdateDaemonStats");
        }

        private void StartGetDaemonStats()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: StartGetDaemonStats");
            QueryArguments args = new QueryArguments(DaemonNextID++, "");
            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker_GetDaemonStatsDoWork);
            backgroundWorker1.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_GetDaemonListViewProgressChanged);
            backgroundWorker1.WorkerReportsProgress = true;
            //backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_ImportWorkerCompleted);
            backgroundWorker1.RunWorkerAsync(args);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: StartGetDaemonStats");
        }

        private void StartUpdateWiaitng(string[] Queries)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: StartUpdateWaiting");
            QueryArguments args = new QueryArguments(ImportNextID++, String.Join("|", Queries) );
            ImportUpdatesQueryArgsList.Add(args);

            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker_UpdateWaitingDoWork);
            backgroundWorker1.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_UpdateListViewProgressChanged);
            backgroundWorker1.WorkerReportsProgress = true;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_UpdateListViewCompleted);
            //backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_ImportWorkerCompleted);
            ImportUpdatesBackgroungWorkersList.Add(backgroundWorker1);

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Starting Background Worker: backgroundWorker_UpdateWaitingDoWork");
            backgroundWorker1.RunWorkerAsync(args);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: StartUpdateWaiting");
        }

        private void StartFileImport(string query)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: StartFileImport");
            QueryArguments args = new QueryArguments(FileImportNextID++, query);
            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker_FileImportDoWork);
            backgroundWorker1.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_ImportProgressChanged);
            backgroundWorker1.WorkerReportsProgress = true;
            backgroundWorker1.WorkerSupportsCancellation = true;
            //backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_ImportWorkerCompleted);
            backgroundWorker1.RunWorkerAsync(args);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: StartFileImport");
        }

        private void StartFolderImport(string query, bool ManualRun = false)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: StartFolderImport");
            QueryArguments args = new QueryArguments(FolderImportNextID++, query);
            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker_FolderImportDoWork);
            backgroundWorker1.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_ImportProgressChanged);
            if(!ManualRun)
            {
                backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker1_ImportCompleted);
            }
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.WorkerReportsProgress = true;
            //backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_ImportWorkerCompleted);
            backgroundWorker1.RunWorkerAsync(args);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: StartFolderImport");
        }

        


        //
        // Menu Click Function Items
        //

        private void importFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: importFileToolStripMenuItem_Click");
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            openFileDialog1.InitialDirectory = "c:\\";
            openFileDialog1.Filter = "All files (*.*)|*.*|Vistumbler VS1 (*.vs1)|*.vs1|Vistumbler VSZ (*.vsz)|*.vsz|Vistumbler TXT (*.txt)|*.txt|Vistumbler CSV (*.csv)|*.csv|Vistumbler MDB (*.mdb)|*.mdb|Wardrive DB (*.db)|*.db|Wardrive DB3 (*.db3)|*.db3";
            openFileDialog1.FilterIndex = 2;
            openFileDialog1.RestoreDirectory = true;

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                    string[] GUIValues = GetImportValues();

                    string Query = openFileDialog1.FileName + "|" + GUIValues[0] + "|" + GUIValues[1];
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), Query);
                    StartFileImport(Query);
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: importFileToolStripMenuItem_Click");
        }
        
        private void importFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: importFolderToolStripMenuItem_Click");
            FolderBrowserDialog folderBrowserDialog1 = new FolderBrowserDialog();
            //folderBrowserDialog1.SelectedPath = "C:\\Users\\ferph02\\Desktop\\VS1";
            folderBrowserDialog1.SelectedPath = "M:\\vi_wifidb_archives\\VS1_FILES\\duplicates";
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), folderBrowserDialog1.SelectedPath);
                string[] GUIValues = GetImportValues();

                string Query = folderBrowserDialog1.SelectedPath + "|" 
                    + GUIValues[0] + "|" 
                    + GUIValues[1];

                StartFolderImport(Query, true);
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: importFolderToolStripMenuItem_Click");
        }

        private void wifidbSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: wifidbSettingsToolStripMenuItem_Click");
            WiFiDB_Settings SettingsForm = new WiFiDB_Settings();
            SettingsForm.ServerList = ServerList;
            SettingsForm.InitForm();

            if (SettingsForm.ShowDialog() == DialogResult.OK)
            {
                ServerList = SettingsForm.ServerList;
                this.SelectedServer = SettingsForm.SelectedServer;
                foreach (ServerObj server in ServerList)
                {
                    if (server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "") == SelectedServer)
                    {
                        this.ServerAddress = server.ServerAddress;
                        this.ApiPath = server.ApiPath;
                        this.Username = server.Username;
                        this.ApiKey = server.ApiKey;
                        server.Selected = true;
                    }
                    else
                    {
                        server.Selected = false;
                    }
                }
                this.ApiCompiledPath = this.ServerAddress + this.ApiPath;
            }
            WriteServerSettings();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: wifidbSettingsToolStripMenuItem_Click");
        }

        private void importSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: importSettingsToolStripMenuItem_Click");
            Import_Settings ImportSettingsForm = new Import_Settings();
            ImportSettingsForm.ImportNotes = DefaultImportNotes;
            ImportSettingsForm.ImportTitle = DefaultImportTitle;
            ImportSettingsForm.UseImportDefaultValues = UseDefaultImportValues;
            ImportSettingsForm.UseAutoDateTimeTitle = UseAutoDateTimeTitle;
            if (ImportSettingsForm.ShowDialog() == DialogResult.OK)
            {
                this.UseAutoDateTimeTitle = ImportSettingsForm.UseAutoDateTimeTitle;
                this.DefaultImportTitle = ImportSettingsForm.ImportTitle;
                this.DefaultImportNotes = ImportSettingsForm.ImportNotes;
                this.UseDefaultImportValues = ImportSettingsForm.UseImportDefaultValues;
                WriteGlobalSettings();
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: importSettingsToolStripMenuItem_Click");
        }

        private void autoSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: autoSettingsToolStripMenuItem_Click");
            Auto_Upload_Settings AutoForm = new Auto_Upload_Settings();
            
            AutoForm.AutoUploadFolder = AutoUploadFolder;
            AutoForm.AutoUploadFolderPath = AutoUploadFolderPath;
            AutoForm.ArchiveImports = ArchiveImports;
            AutoForm.ArchiveImportsFolderPath = ArchiveImportsFolderPath;
            AutoForm.AutoCloseTimerSeconds = AutoCloseTimerSeconds.ToString();
            AutoForm.AutoCloseEnable = AutoCloseEnable;

            if (AutoForm.ShowDialog() == DialogResult.OK)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "AutoForm.AutoUploadFolder: " + AutoForm.AutoUploadFolder);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "AutoForm.AutoUploadFolderPath: " + AutoForm.AutoUploadFolderPath);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "AutoForm.ArchiveImports: " + AutoForm.ArchiveImports);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "AutoForm.ArchiveImportsFolderPath: " + AutoForm.ArchiveImportsFolderPath);

                AutoUploadFolder = Convert.ToBoolean(AutoForm.AutoUploadFolder);
                AutoUploadFolderPath = AutoForm.AutoUploadFolderPath;
                ArchiveImports = Convert.ToBoolean(AutoForm.ArchiveImports);
                ArchiveImportsFolderPath = AutoForm.ArchiveImportsFolderPath;
                AutoCloseTimerSeconds = Int32.Parse(AutoForm.AutoCloseTimerSeconds);
                AutoCloseEnable = AutoForm.AutoCloseEnable;

                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "this.AutoUploadFolder: " + this.AutoUploadFolder);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "this.AutoUploadFolderPath: " + this.AutoUploadFolderPath);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "this.ArchiveImports: " + this.ArchiveImports);
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "this.ArchiveImportsFolderPath: " + this.ArchiveImportsFolderPath);

                WriteGlobalSettings();
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: autoSettingsToolStripMenuItem_Click");
        }

        private void backgroundThreadSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundThreadSettingsToolStripMenuItem_Click");

            BGThreadsSettings BGThreadsSettingsForm         = new BGThreadsSettings();
            BGThreadsSettingsForm.DaemonUpdateThreadSeconds = DaemonUpdateThreadSeconds.ToString();
            BGThreadsSettingsForm.ImportUpdateThreadSeconds = ImportUpdateThreadSeconds.ToString();
            BGThreadsSettingsForm.AutoImportThreadSeconds   = AutoImportThreadSeconds.ToString();

            BGThreadsSettingsForm.ImportUpdateThreadEnable  = ImportUpdateThreadEnable;
            BGThreadsSettingsForm.DaemonUpdateThreadEnable  = DaemonUpdateThreadEnable;
            BGThreadsSettingsForm.AutoImportThreadEnable    = AutoImportThreadEnable;

            if (BGThreadsSettingsForm.ShowDialog() == DialogResult.OK)
            {
                DaemonUpdateThreadSeconds = Int32.Parse(BGThreadsSettingsForm.DaemonUpdateThreadSeconds);
                ImportUpdateThreadSeconds = Int32.Parse(BGThreadsSettingsForm.ImportUpdateThreadSeconds);
                AutoImportThreadSeconds   = Int32.Parse(BGThreadsSettingsForm.AutoImportThreadSeconds);
                ImportUpdateThreadEnable  = BGThreadsSettingsForm.ImportUpdateThreadEnable;
                DaemonUpdateThreadEnable  = BGThreadsSettingsForm.DaemonUpdateThreadEnable;
                AutoImportThreadEnable    = BGThreadsSettingsForm.AutoImportThreadEnable;

                WriteGlobalSettings();
                LoadSettings();
                InitTimer();
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundThreadSettingsToolStripMenuItem_Click");
        }
        
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: exitToolStripMenuItem_Click");
            WDBSQLiteObj.Dispose(false);
            Application.Exit();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: exitToolStripMenuItem_Click");
        }

        private void exitAndSaveDBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: exitAndSaveDBToolStripMenuItem_Click");
            WDBSQLiteObj.Dispose(true);
            Application.Exit();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: exitAndSaveDBToolStripMenuItem_Click");
        }

        private void loggingSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: loggingSettingsToolStripMenuItem_Click");
            LoggingSettings LoggingSettingsForm = new LoggingSettings();
            LoggingSettingsForm.TraceLogEnable = TraceLogEnable;
            LoggingSettingsForm.DEBUG = DEBUG;
            LoggingSettingsForm.PerRunRotate = PerRunRotate;

            if(LoggingSettingsForm.ShowDialog() == DialogResult.OK)
            {
                if( (TraceLogEnable != LoggingSettingsForm.TraceLogEnable) || (DEBUG != LoggingSettingsForm.DEBUG) || (PerRunRotate != LoggingSettingsForm.PerRunRotate) )
                {
                    if(PerRunRotate != LoggingSettingsForm.PerRunRotate)
                    {
                        this.TogglePerRun = true;
                    }else
                    {
                        this.TogglePerRun = false;
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "One of the Logging Settings has changed, update them.");
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "TraceLogEnable: " + TraceLogEnable.ToString() + " ----- LoggingForm.TraceLogEnable: " + LoggingSettingsForm.TraceLogEnable.ToString());
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Debug: " + DEBUG.ToString() + " ----- LoggingForm.Debug: " + LoggingSettingsForm.DEBUG.ToString());
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "PerRunRotate: " + PerRunRotate.ToString() + " ----- LoggingForm.PerRunRotate: " + LoggingSettingsForm.PerRunRotate.ToString());

                    this.TraceLogEnable = LoggingSettingsForm.TraceLogEnable;
                    DEBUG = LoggingSettingsForm.DEBUG;
                    PerRunRotate = LoggingSettingsForm.PerRunRotate;


                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Write registry settings then re-init settings, classes, and timers.");
                    WriteGlobalSettings();
                    
                    
                    /*
                    WDBTraceLogObj = new WDBTraceLog.TraceLog(LogPath, TraceLogEnable, PerRunRotate);

                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Save Settings to Registry.");
                    
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Re-load Settings from Registry.");
                    LoadSettings();
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Re-init Timers for background threads.");
                    InitTimer();
                    */
                }
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: loggingSettingsToolStripMenuItem_Click");
        }

        private string[] GetImportValues()
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: GetImportValues");
            string UseImportTitle;
            string UseImportNotes;
            //try
            //{
                if (UseDefaultImportValues)
                {
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Use Defaults from settings.");
                    if (UseAutoDateTimeTitle)
                    {
                        UseImportTitle = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        UseImportTitle = DefaultImportTitle;
                    }
                    UseImportNotes = DefaultImportNotes;
                }
                else
                {
                    //Ask For Import Title and Notes. They dont want to use the defaults.
                    ImportDetails ImportDetailsForm = new ImportDetails();

                    if (ImportDetailsForm.ShowDialog() == DialogResult.OK)
                    {
                        UseImportTitle = ImportDetailsForm.ImportTitle;
                        UseImportNotes = ImportDetailsForm.ImportNotes;
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Use GUI Values: ");
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import Title: " + UseImportTitle);
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import Notes: " + UseImportNotes);
                    }
                    else
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "GUI Cancel, Use Defaults.");
                        UseImportTitle = DefaultImportTitle;
                        UseImportNotes = DefaultImportNotes;
                    }
                }
                string[] ReturnString =
                    {
                        UseImportTitle.Replace("%DATETIME%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                        UseImportNotes.Replace("%DATETIME%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    };
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: GetImportValues");
                return ReturnString;

            //}catch(Exception e)
            //{
            //    string[] ret = { "Error|~|", e.Message };
            //    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Exception Message: " + e.Message);
            //    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: GetImportValues");
            //    return ret;
            //}
        }
        //
        // Do Work Functions
        //

        private void backgroundWorker_UpdateDaemonStatsDoWork(object sender, DoWorkEventArgs e)
        {
            WDBCommonObj.ThreadName = "DaemonUpdateStats";
            ThreadName = "DaemonUpdateStats";
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_UpdateDaemonStatsDoWork");
            
            var backgroundWorker = sender as BackgroundWorker;
            QueryArguments args = (QueryArguments)e.Argument;
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), args.Query);
            WDBCommonObj.GetDaemonStatuses(args.Query, backgroundWorker);
            //e.Result = "Waiting Done";
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_UpdateDaemonStatsDoWork");
            ThreadName = "Main";
        }

        private void backgroundWorker_GetDaemonStatsDoWork(object sender, DoWorkEventArgs e)
        {
            ThreadName = "DaemonUpdate";
            WDBCommonObj.ThreadName = "DaemonUpdate";
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_GetDaemonStatsDoWork");
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), ServerAddress);

            var backgroundWorker = sender as BackgroundWorker;
            QueryArguments args = (QueryArguments)e.Argument;

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), args.Query);
            WDBCommonObj.GetDaemonStatuses(args.Query, backgroundWorker);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_GetDaemonStatsDoWork");
            ThreadName = "Main";
        }

        private void backgroundWorker_UpdateWaitingDoWork(object sender, DoWorkEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_UpdateWaitingDoWork");
            QueryArguments args = (QueryArguments)e.Argument;

            ThreadName = "UpdateWaiting";
            WDBCommonObj.ThreadName = "UpdateWaiting";

            Random rand = new Random();
            foreach (string str in args.Query.Split('|'))
            {
                Thread.Sleep( (rand.Next(5, 100) * 100 ) );
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Spawning Function for: " + str);
                var backgroundWorker = sender as BackgroundWorker;
                WDBCommonObj.GetHashStatus(str, backgroundWorker);
            }
            e.Result = args.QueryID;
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_UpdateWaitingDoWork");
            ThreadName = "Main";
        }

        private void backgroundWorker_FolderImportDoWork(object sender, DoWorkEventArgs e)
        {
            WDBCommonObj.ThreadName = "FolderImport";
            ThreadName = "FolderImport";
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_FolderImportDoWork");
            List<KeyValuePair<int, string>> ImportIDs;
            var backgroundWorker = sender as BackgroundWorker;
            QueryArguments args = (QueryArguments)e.Argument;

            string[] splits = args.Query.Split('|');

            //splits[0] == ImportFolder
            //splits[1] == ImportTitle
            //splits[2] == ImportNotes

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), args.Query);
            ImportIDs = WDBCommonObj.ImportFolder(splits[0], splits[1], splits[2], backgroundWorker);
            /*
            if (ImportIDs.Count > 0)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), ImportIDs[0].Value);
                if (ImportIDs[0].Value == "Error with API")
                {
                    MessageBox.Show("Error With API.");
                }else if (ImportIDs[0].Value == "Already Imported.")
                {
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "File Already Imported.");
                    //MessageBox.Show("Error With API.");
                }else
                {
                    args.Result = ImportIDs;
                    e.Result = args.Result;
                }
            }
            */
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_FolderImportDoWork");
            ThreadName = "Main";
        }
        
        private void backgroundWorker_FileImportDoWork(object sender, DoWorkEventArgs e)
        {
            ThreadName = "FileImport";
            WDBCommonObj.ThreadName = "FileImport";
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_FileImportDoWork");
            var backgroundWorker = sender as BackgroundWorker;
            QueryArguments args = (QueryArguments)e.Argument;
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), args.Query);

            string[] splits = args.Query.Split('|');

            //splits[0] == ImportFile
            //splits[1] == ImportTitle
            //splits[2] == ImportNotes
            string ImportFileResult = WDBCommonObj.ImportFile(splits[0], splits[1], splits[2], backgroundWorker);
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import File Return: " + ImportFileResult);
            
            if (ImportFileResult == "Error with API")
            {
                //MessageBox.Show("Error With API.");
            }
            else if (ImportFileResult == "Already Imported.")
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "File Already Imported.");
                //MessageBox.Show("File Already Imported.");
            }/*
            else
            {
                ImportIDs.Add(new KeyValuePair<int, string>(ImportInternalID, ImportFileResult));
            }
            
            args.Result = ImportIDs;
            e.Result = args.Result;
            */
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_FileImportDoWork");
            ThreadName = "Main";
        }

        private void UpdateListViewStatus(string hashish, string StatusStr, string MessageStr)
        {
            ListViewItem listViewItem1 = listView1.FindItemWithText(hashish);

            try
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), listViewItem1.SubItems[1].Text + " ==== " + listViewItem1.SubItems.Count);
                listViewItem1.SubItems[7].Text = StatusStr;
                listViewItem1.SubItems[8].Text = MessageStr;
            }
            catch(Exception e)
            {
                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Exception: " + e.Message + " ListView Search Hash: " + hashish);
            }
        }
        
        //
        // Progress Changed Functions
        //
        private void backgroundWorker_ImportProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_ImportProgressChanged");
            List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
            string[] stringSeparators = new string[] { "|~|" };
            string[] split = e.UserState.ToString().Split(stringSeparators, StringSplitOptions.None);
            
            string title = "";
            string user = "";
            string message = "";
            string ImportID = "";
            string filehash = "";
            string FileSizeString;
            string FileHash;
            string FilePath;
            string StatusStr;
            string MessageStr;


            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "e.UserState: " + e.UserState.ToString() );

            string[] stringSep1 = new string[] { "::" };
            string[] stringSep2 = new string[] { "-~-" };
            string[] stringSep3 = new string[] { ": " };

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Split[0]: " + split[0]);
            switch (split[0].ToLower())
            {
                case "newrow":
                    InsertNewListViewRow(split, "NewRow");
                    break;

                case "error":
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), split[0]);
                    string[] items_err = split[1].Split(stringSep2, StringSplitOptions.None);
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "items_err.Count(): " + items_err.Count().ToString());

                    if (items_err.Count() < 2)  // error |~| There was An error during Import -~- No upload file found :( :: M:\vi
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), split[1]);
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "HHmmmm...... e.UserState: " + e.UserState.ToString());

                        /* if (items_err.Count() > 1)
                        {
                            string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                            //string[] SplitData1 = SplitData[0].Split(stringSep3, StringSplitOptions.None);
                            FilePath = SplitData[0];
                        }
                        else
                        { */
                            string[] SplitData = items_err[0].Split(stringSep1, StringSplitOptions.None);
                            //string[] SplitData1 = SplitData[0].Split(stringSep3, StringSplitOptions.None);
                            FilePath = SplitData[1];
                        //}



                        byte[] hashBytes;
                        string hashish;
                        using (var inputFileStream = File.Open(FilePath, FileMode.Open))
                        {
                            var md5 = MD5.Create();
                            hashBytes = md5.ComputeHash(inputFileStream);
                            hashish = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import File Hash: " + hashish);
                        }

                        FileSizeString = new FileInfo(FilePath).Length.ToString();

                        StatusStr = "Error";
                        MessageStr = split[1];

                        UpdateListViewStatus(hashish, StatusStr, MessageStr);
                    }
                    else
                    {
                        if( items_err[0] == "Already Local Imported." )
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Skip That Shit! : " + items_err[0]);
                        }
                        else if (items_err[0] == "Already Imported.")
                        {
                            string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                            //string[] SplitData1 = SplitData[0].Split(stringSep3, StringSplitOptions.None);

                            FilePath = SplitData[0];

                            byte[] hashBytes;
                            string hashish;
                            using (var inputFileStream = File.Open(FilePath, FileMode.Open))
                            {
                                var md5 = MD5.Create();
                                hashBytes = md5.ComputeHash(inputFileStream);
                                hashish = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import File Hash: " + hashish);
                            }

                            //FileHash = SplitData1[1].ToUpper();
                            FileSizeString = new FileInfo(FilePath).Length.ToString();

                            StatusStr = "Error";
                            MessageStr = split[1];

                            UpdateListViewStatus(hashish, StatusStr, MessageStr);
                        }
                        else
                        {
                            string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                            string[] SplitData1 = SplitData[0].Split(stringSep3, StringSplitOptions.None);

                            FilePath = SplitData[1];

                            byte[] hashBytes;
                            string hashish;
                            using (var inputFileStream = File.Open(FilePath, FileMode.Open))
                            {
                                var md5 = MD5.Create();
                                hashBytes = md5.ComputeHash(inputFileStream);
                                hashish = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Import File Hash: " + hashish);
                            }

                            //FileHash = SplitData1[1].ToUpper();
                            FileSizeString = new FileInfo(FilePath).Length.ToString();

                            StatusStr = "Error";
                            MessageStr = split[1];

                            UpdateListViewStatus(hashish, StatusStr, MessageStr);
                        }
                        //InsertNewListViewRow(split, "Error");
                    }
                    break;
                default:
                    foreach (string part in split)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), part + " \n--------------------\n");
                        string[] items_pre = part.Split('|');

                        foreach (var item in items_pre)
                        {
                            string[] items = item.Split(stringSep2, StringSplitOptions.None);
                            switch (items[0])
                            {
                                case "title":
                                    title = items[1];
                                    break;
                                case "user":
                                    user = items[1];
                                    break;
                                case "importnum":
                                    ImportID = items[1];
                                    break;
                                case "message":
                                    message = items[1];
                                    break;
                                case "filehash":
                                    
                                    string[] FileData = items[1].Split(stringSep1, StringSplitOptions.None);

                                    //FilePath = FileData[1];
                                    filehash = FileData[0].ToUpper();
                                    break;
                            }
                        }
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------------------\n");
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Update Row FileHash: " + filehash);
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Update Row FileName: " + FileName);
                    WDBSQLite.ImportRow ImportRowObj = new WDBSQLite.ImportRow();
                    ListViewItem listViewItem = listView1.FindItemWithText(filehash);
                    if(user != "" && filehash != "")
                    {
                        ImportRowObj.ImportID = Int32.Parse(ImportID);
                        ImportRowObj.ImportTitle = title;
                        ImportRowObj.FileHash = filehash;
                        ImportRowObj.Status = "Waiting";
                        ImportRowObj.Message = message;

                        WDBCommonObj.UpdateImportRow(ImportRowObj); // Update Import row information in SQLite.

                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "\n------------------\n------------------\nUpdate ROW: " + ImportID + " |=| " + user + " |=| " + title + " |=| " + "Waiting" + " |=| " + message + "\n------------------\n------------------\n");
                        listViewItem.SubItems[0].Text = ImportID;
                        listViewItem.SubItems[1].Text = user;
                        listViewItem.SubItems[2].Text = title;
                        listViewItem.SubItems[7].Text = "Waiting";
                        listViewItem.SubItems[8].Text = message;
                    }
                    break;
            }
            if (listView1.Items.Count > 1)
            {
                listView1.TopItem = listView1.Items[listView1.Items.Count - 1];
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), e.ProgressPercentage.ToString());
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_ImportProgressChanged");
        }

        private void backgroundWorker_UpdateListViewProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_UpdateListViewProgressChanged");
            List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
            WDBSQLite.ImportRow ImportRowObj = new WDBSQLite.ImportRow();

            string[] stringSeparators = new string[] { "|~|" };
            string[] stringSep1 = new string[] { "::" };
            string[] stringSep2 = new string[] { "-~-" };
            string[] split = e.UserState.ToString().Split(stringSeparators, StringSplitOptions.None);

            string title = "";
            string user = "";
            string message = "";
            string status = "";
            string ImportID = "";
            string filehash = "";

            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "e.UserState.ToString(): " + e.UserState.ToString() );
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "First of the Split array [0]: " + split[0]);
            switch (split[0])
            {
                case "error":
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), split[0]);
                    string[] items_err = split[1].Split(stringSep2, StringSplitOptions.None);
                    string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);

                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), items_err[0]);
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), SplitData[0]);
                    if(SplitData.Count() > 1)
                    {
                        ListViewItem listViewItem1 = listView1.FindItemWithText(SplitData[1].TrimStart(' '));
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), listViewItem1.SubItems[1].Text + " ==== " + listViewItem1.SubItems.Count);
                        listViewItem1.SubItems[8].Text = SplitData[0];
                    }

                    break;
                default:
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- Start Parse ListView Update Return String -----------\n");

                    foreach (string part in split)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n---------Part: " + part + "-----------\n");
                        string[] items_pre = part.Split('|');

                        foreach (var item in items_pre)
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- Item: " + item + "-----------\n");
                            if(!item.Contains("-~-"))
                            {
                                switch (item.ToString())
                                {
                                    case "waiting":
                                        status = item;
                                        break;
                                    case "importing":
                                        status = item;
                                        break;
                                    case "finished":
                                        status = item;
                                        break;
                                }
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Message Loop Message: " + message + " ==== Item Value:" + item + " Part: " + part);
                                continue;
                            }
                            string[] items = item.Split(stringSep2, StringSplitOptions.None);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "---- Items and Values: " + items[0] + " :: Value: " + items[1]);
                            switch (items[0].ToString())
                            {
                                case "title":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Title? " + items[1]);
                                    title = items[1];
                                    break;
                                case "user":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "user? " + items[1]);
                                    user = items[1];
                                    break;
                                case "id":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "importnum? " + items[1]);
                                    ImportID = items[1];
                                    break;
                                case "message":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "message? " + items[1]);
                                    message = items[1];
                                    break;
                                case "hash":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "filehash? " + items[1]);
                                    filehash = items[1];
                                    break;
                                case "ap":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "AP? " + items[1]);
                                    message = message + " - " + items[1];
                                    break;
                                case "tot":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "This Of This? " + items[1]);
                                    message = message + " - " + items[1];
                                    break;
                            }
                        }
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), filehash);
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Parse Loop Message: " + message);
                    listView1.BeginUpdate();
                    ListViewItem listViewItem = listView1.FindItemWithText(filehash);
                    if( (ImportID != "") )
                    {
                        ImportRowObj.ImportID = Int32.Parse(ImportID);
                        ImportRowObj.ImportTitle = title;
                        ImportRowObj.FileHash = filehash;
                        ImportRowObj.Status = status;
                        ImportRowObj.Message = message;

                        WDBCommonObj.UpdateImportRow(ImportRowObj); // Update Import row information in SQLite.

                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "\n------------------\n------------------\nUpdate ROW: " + ImportID.ToString() + " |=| " + user + " |=| " + title + " |=| " + message + " |=| " + status + "\n------------------\n------------------\n");
                        listViewItem.SubItems[0].Text = ImportID;
                        listViewItem.SubItems[2].Text = title;
                        listViewItem.SubItems[7].Text = status;
                        listViewItem.SubItems[8].Text = message;
                    }else
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Status was not Finish, or ImportID was empty and Message was empty. Status: " + status + " ImportID: " + ImportID + " Message: " + message);
                    }
                    listView1.EndUpdate();
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- End Parse ListView Update Return String -----------\n");
                    break;
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_UpdateListViewProgressChanged");
        }
        
        private void backgroundWorker_GetDaemonListViewProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_GetDaemonListViewProgressChanged");
            List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();

            string[] stringSeparators = new string[] { "|~|" };
            string[] split = e.UserState.ToString().Split(stringSeparators, StringSplitOptions.None);
            
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), e.UserState.ToString());
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), split[0]);

            string nodename = "";
            string pidfile = "";
            string pid = "";
            string pidmem = "";
            string pidtime = "";
            string cmd = "";
            string datetime_col = "";
            string[] stringSep1 = new string[] { "::" };
            string[] stringSep2 = new string[] { "-~-" };
            switch (split[0])
            {
                case "error":
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), split[1]);
                    if(split[1] == "No_Daemons_Running")
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "No Daemons running, do ListView CleanUp.");
                        if (this.listView2.Items.Count > 0)
                        {
                            foreach (ListViewItem item in this.listView2.Items)
                            {
                                item.Remove();
                            }
                        }else
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "No rows, no need for cleanup...");
                        }
                    }
                    else
                    {
                        string E_UserString = e.UserState.ToString();
                        try
                        {
                            string[] items_err = split[1].Split(stringSep2, StringSplitOptions.None);
                            string[] SplitData = items_err[1].Split(stringSep1, StringSplitOptions.None);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), items_err[0]);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), SplitData[0]);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), SplitData[1]);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), E_UserString);
                        }
                        catch (Exception ex)
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Exception Updating Daemon Listview: " + E_UserString + " ----+++---- Exception: " + ex.Message);
                        }
                    }
                    break;

                default:
                    
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- Start Parse Daemon ListView Update Return String -----------\n");
                    int DaemonReturnCount = split.Count();
                    foreach (string part in split)
                    {
                        if(part == "")
                        {
                            DaemonReturnCount--;
                            continue;
                        }
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n---------Part: " + part + "-----------\n");
                        string[] items_pre = part.Split('|');

                        foreach (var item in items_pre)
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- Item: " + item + "-----------\n");
                            if (!item.Contains("-~-"))
                            {
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Bad Message: " + item + " Part: " + part);
                                continue;
                            }
                            string[] items = item.Split(stringSep2, StringSplitOptions.None);
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "---- Items and Values: " + items[0] + " :: Value: " + items[1]);
                            switch (items[0].ToString())
                            {
                                case "nodename":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "nodename? " + items[1]);
                                    nodename = items[1];
                                    break;
                                case "pidfile":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "pidfile? " + items[1]);
                                    pidfile = items[1];
                                    break;
                                case "pid":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "pid? " + items[1]);
                                    pid = items[1];
                                    break;
                                case "pidtime":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "runtime? " + items[1]);
                                    pidtime = items[1];
                                    break;
                                case "pidmem":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "mem? " + items[1]);
                                    pidmem = items[1];
                                    break;
                                case "pidcmd":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "cmd? " + items[1]);
                                    cmd = items[1];
                                    break;
                                case "date":
                                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "date anad time? " + items[1]);
                                    datetime_col = items[1];
                                    break;
                            }
                        }
                        if ((nodename != "") && (pid != "") && (pidfile != ""))
                        {
                            ListViewItem listViewItem = listView2.FindItemWithText(pidfile);
                            if ( listViewItem != null)
                            {
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), listViewItem.SubItems[1].Text + " ==== " + listViewItem.SubItems.Count);
                                listViewItem.SubItems[0].Text = nodename;
                                listViewItem.SubItems[1].Text = pidfile;
                                listViewItem.SubItems[2].Text = pid;
                                listViewItem.SubItems[3].Text = pidtime;
                                listViewItem.SubItems[4].Text = pidmem;
                                listViewItem.SubItems[5].Text = cmd;
                                listViewItem.SubItems[6].Text = datetime_col;
                            }
                            else
                            {
                                string[] row = { nodename, pidfile, pid, pidtime.ToString(), pidmem.ToString(), cmd.ToString(), datetime_col.ToString() };
                                var listViewItemNew = new ListViewItem(row);
                                listView2.Items.Add(listViewItemNew);
                            }
                        }
                    }

                    //Check for rows that are not in the return, and remove them.
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), DaemonReturnCount.ToString() + " --=--=-=-=-=-==-- " + listView2.Items.Count);
                    if ((listView2.Items.Count != DaemonReturnCount) && DaemonReturnCount != 0)
                    {
                        foreach (ListViewItem item in listView2.Items)
                        {
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), e.UserState.ToString());
                            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), item.SubItems[1].Text);

                            if(e.UserState.ToString().Contains(item.SubItems[1].Text))
                            {
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), item.SubItems[1].Text + " Is in the return.");
                            }else
                            {
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), item.SubItems[1].Text + " Is NOT in the return.");
                                WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "ListView CleanUp!");
                                item.Remove();
                            }
                        }
                    }
                    if(DaemonReturnCount == 0)
                    {
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "DaemonReturnCount was 0...");
                        WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "UserStateString: " + e.UserState.ToString());
                    }
                    WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), " \n--------- End Parse Daemon ListView Update Return String -----------\n");
                    break;
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_GetDaemonListViewProgressChanged");
        }
        
        //
        // Process Completed Functions
        //
        
        private void backgroundWorker_UpdateListViewCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker_UpdateListViewCompleted");
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "e.Result: " + e.Result);
            //ImportUpdatesBackgroungWorkersList.RemoveAt(Convert.ToInt32(e.Result));
            //ImportUpdatesQueryArgsList.RemoveAt(Convert.ToInt32(e.Result));
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker_UpdateListViewCompleted");
        }

        private void backgroundWorker1_ImportCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: backgroundWorker1_ImportCompleted");
            //maybe do something?
            if (AutoCloseEnable)
            {
                AutoCloseTimer AutoCloseTimerForm = new AutoCloseTimer();
                AutoCloseTimerForm.TimerSeconds = AutoCloseTimerSeconds.ToString();
                AutoCloseTimerForm.ShowDialog();
            }
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: backgroundWorker1_ImportCompleted");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string GetCurrentMethod()
        {
            StackTrace st = new StackTrace();
            StackFrame sf = st.GetFrame(1);

            return sf.GetMethod().Name;
        }

        private void WiFiDBUploadMainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "Start Call: WiFiDBUploadMainForm_FormClosed");
            WDBSQLiteObj.Dispose(false);
            Application.Exit();
            WDBTraceLogObj.WriteToLog(ThreadName, ObjectName, GetCurrentMethod(), "End Call: WiFiDBUploadMainForm_FormClosed");
        }
    }



    public class ServerNameObj
    {
        public string ServerName { get; set; }
    }

    public class ServerObj
    {
        public int ID { get; set; }
        public string ServerAddress { get; set; }
        public string ApiPath { get; set; }
        public string Username { get; set; }
        public string ApiKey { get; set; }
        public bool Selected { get; set; }
    }

    public class IdComparer : IEqualityComparer<ServerNameObj>
    {
        public int GetHashCode(ServerNameObj co)
        {
            if (co == null)
            {
                return 0;
            }
            return co.ServerName.GetHashCode();
        }

        public bool Equals(ServerNameObj x1, ServerNameObj x2)
        {
            if (object.ReferenceEquals(x1, x2))
            {
                return true;
            }
            if (object.ReferenceEquals(x1, null) ||
                object.ReferenceEquals(x2, null))
            {
                return false;
            }
            return x1.ServerName == x2.ServerName;
        }
    }
}
