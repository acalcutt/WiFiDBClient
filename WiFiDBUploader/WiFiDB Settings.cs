using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WiFiDBUploader
{
    public partial class WiFiDB_Settings : Form
    {
        public WiFiDB_Settings()
        {
            InitializeComponent();
        }
        
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            //Debug.WriteLine("Selected Item: " + comboBox1.SelectedItem.ToString());
            _SelectedServer = comboBox1.SelectedItem.ToString();

            //Debug.WriteLine("Server Count List: " + ServerList.Count);
            //Debug.WriteLine("Server out: " + ServerList[0].ServerAddress.ToString());
        }
        
        private void AddServer_Click(object sender, EventArgs e)
        {
            AddServer AddServerForm = new AddServer();

            if (AddServerForm.ShowDialog() == DialogResult.OK)
            {
                ServerObj Server = new ServerObj();

                Server.ID = _ServerList.Count + 1;
                Server.ServerAddress = AddServerForm.ServerAddress;
                Server.ApiPath = AddServerForm.ApiPath;
                Server.Username = AddServerForm.Username;
                Server.ApiKey = AddServerForm.ApiKey;
                Server.Selected = false;
                ServerList.Add(Server);

                this.comboBox1.Items.AddRange(new object[] { Server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "") });
            }
            this.DialogResult = DialogResult.None;
        }

        private void RemoveServer_Click(object sender, EventArgs e)
        {
            int RemoveIndex = ServerList.FindIndex(ServerList => ServerList.ServerAddress.ToString().Replace("https://", "").Replace("http://", "").Equals(_SelectedServer, StringComparison.Ordinal));
            if (RemoveIndex < 0) return;
            ServerList.RemoveAt(RemoveIndex);
            this.comboBox1.Items.Remove(_SelectedServer);
        }

        private void EditServer_Click(object sender, EventArgs e)
        {
            EditServer EditServerForm = new EditServer();

            //Debug.WriteLine("Server out: " + ServerList[0].ServerAddress.ToString());

            foreach(ServerObj server in ServerList)
            {
                if(server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "") == _SelectedServer)
                {
                    EditServerForm.ServerAddress = server.ServerAddress;
                    EditServerForm.ApiPath = server.ApiPath;
                    EditServerForm.Username = server.Username;
                    EditServerForm.ApiKey = server.ApiKey;
                }
            }
            
            EditServerForm.InitForm();

            if (EditServerForm.ShowDialog() == DialogResult.OK)
            {
                foreach (ServerObj server in ServerList)
                {
                    if (server.ServerAddress.ToString().Replace("https://", "").Replace("http://", "") == _SelectedServer)
                    {
                        server.ServerAddress = EditServerForm.ServerAddress;
                        server.ApiPath = EditServerForm.ApiPath;
                        server.Username = EditServerForm.Username;
                        server.ApiKey = EditServerForm.ApiKey;
                    }
                }
            }
            this.DialogResult = DialogResult.None;
        }
    }
}
