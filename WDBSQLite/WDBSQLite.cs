﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WDBSQLite
{
    public class WDBSQLite
    {
        public SQLiteConnection conn;
        public WDBSQLite(string Path, string UI)
        {
            if (!File.Exists(Path))
            {
                Debug.WriteLine("Start of Create SQLite DB. " + Path);
                if (UI.ToLower() == "client")
                {
                    this.conn = CreateClientDB(Path);
                }
                else if (UI.ToLower() == "uploader")
                {
                    this.conn = CreateUploadDB(Path);
                }
                Debug.WriteLine("End of Create SQLite DB." + Path);
            }
            else
            {
                Debug.WriteLine("SQLite data source=" + Path);
                conn = new SQLiteConnection("data source=" + Path);
                conn.Open();
            }
        }

        public void Close()
        {
            this.conn.Dispose();
        }

        private SQLiteConnection CreateUploadDB(string DbPath)
        {
            SQLiteConnection conn;
            SQLiteCommand cmd;

            SQLiteConnection.CreateFile(DbPath);
            Debug.Write("Created Database: " + DbPath);

            conn = new SQLiteConnection("data source=" + DbPath);
            conn.Open();

            cmd = new SQLiteCommand(conn);

            cmd.CommandText = @"CREATE TABLE ImportView (
ID INTEGER PRIMARY KEY AUTOINCREMENT,
ImportID INT,
Username VARCHAR(255),
ImportTitle VARCHAR(255),
Date_Time VARCHAR(255),
FileSize VARCHAR(255),
FileName VARCHAR(255),
FileHash VARCHAR(255),
Status VARCHAR(255),
Message VARCHAR(255)

)";
            cmd.ExecuteNonQuery();
            Debug.Write("Successfully created `ImportView` Table");

            cmd.Dispose();

            return conn;
        }

        private SQLiteConnection CreateClientDB(string DbPath)
        {
            SQLiteConnection conn;
            SQLiteCommand cmd;

            SQLiteConnection.CreateFile(DbPath);
            Debug.Write("Created Database: " + DbPath);

            conn = new SQLiteConnection("data source=" + DbPath);
            conn.Open();

            cmd = new SQLiteCommand(conn);

            cmd.CommandText = @"CREATE TABLE ImportView (
ID INTEGER PRIMARY KEY AUTOINCREMENT,
Username VARCHAR(255),
ImportTitle VARCHAR(255),
Date_Time VARCHAR(255),
FileSize VARCHAR(255),
FileName VARCHAR(255),
FileHash VARCHAR(255),
Status VARCHAR(255),
Message VARCHAR(255)

)";
            cmd.ExecuteNonQuery();
            Debug.Write("Successfully created `ImportView` Table");

            cmd.Dispose();

            return conn;
        }
    }
}