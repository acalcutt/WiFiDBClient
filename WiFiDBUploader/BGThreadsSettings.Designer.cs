namespace WiFiDBUploader
{
    partial class BGThreadsSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
        
        public string DaemonUpdateThreadSeconds
        {
            get { return DaemonUpdateSecondsTextBox.Text; }
            set { DaemonUpdateSecondsTextBox.Text = value; }
        }

        public string ImportUpdateThreadSeconds
        {
            get { return ImportUpdateSecondsTextBox.Text; }
            set { ImportUpdateSecondsTextBox.Text = value; }
        }

        public string AutoImportThreadSeconds
        {
            get { return AutoImportTextBox.Text; }
            set { AutoImportTextBox.Text = value; }
        }
        public bool ImportUpdateThreadEnable
        {
            get { return ImportUpdateEnableCheckBox.Checked; }
            set { ImportUpdateEnableCheckBox.Checked = value; }
        }

        public bool DaemonUpdateThreadEnable
        {
            get { return DaemonThreadEnableCheckBox.Checked; }
            set { DaemonThreadEnableCheckBox.Checked = value; }
        }

        public bool AutoImportThreadEnable
        {
            get { return AutoImportCheckBox.Checked; }
            set { AutoImportCheckBox.Checked = value; }
        }
        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.DaemonUpdateSecondsTextBox = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.DaemonThreadEnableCheckBox = new System.Windows.Forms.CheckBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.ImportUpdateSecondsTextBox = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.ImportUpdateEnableCheckBox = new System.Windows.Forms.CheckBox();
            this.AutoOKButton = new System.Windows.Forms.Button();
            this.AutoCancelButton = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.AutoImportTextBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.AutoImportCheckBox = new System.Windows.Forms.CheckBox();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.DaemonUpdateSecondsTextBox);
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.DaemonThreadEnableCheckBox);
            this.groupBox2.Location = new System.Drawing.Point(12, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(326, 85);
            this.groupBox2.TabIndex = 0;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Daemon Stats";
            // 
            // DaemonUpdateSecondsTextBox
            // 
            this.DaemonUpdateSecondsTextBox.Location = new System.Drawing.Point(207, 41);
            this.DaemonUpdateSecondsTextBox.Name = "DaemonUpdateSecondsTextBox";
            this.DaemonUpdateSecondsTextBox.Size = new System.Drawing.Size(100, 20);
            this.DaemonUpdateSecondsTextBox.TabIndex = 4;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(7, 44);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(194, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Interval for Daemon Updates (Seconds)";
            // 
            // DaemonThreadEnableCheckBox
            // 
            this.DaemonThreadEnableCheckBox.AutoSize = true;
            this.DaemonThreadEnableCheckBox.Location = new System.Drawing.Point(7, 20);
            this.DaemonThreadEnableCheckBox.Name = "DaemonThreadEnableCheckBox";
            this.DaemonThreadEnableCheckBox.Size = new System.Drawing.Size(177, 17);
            this.DaemonThreadEnableCheckBox.TabIndex = 0;
            this.DaemonThreadEnableCheckBox.Text = "Enable Daemon Update Thread";
            this.DaemonThreadEnableCheckBox.UseVisualStyleBackColor = true;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.ImportUpdateSecondsTextBox);
            this.groupBox3.Controls.Add(this.label2);
            this.groupBox3.Controls.Add(this.ImportUpdateEnableCheckBox);
            this.groupBox3.Location = new System.Drawing.Point(12, 103);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(326, 78);
            this.groupBox3.TabIndex = 1;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Import Updates";
            // 
            // ImportUpdateSecondsTextBox
            // 
            this.ImportUpdateSecondsTextBox.Location = new System.Drawing.Point(196, 36);
            this.ImportUpdateSecondsTextBox.Name = "ImportUpdateSecondsTextBox";
            this.ImportUpdateSecondsTextBox.Size = new System.Drawing.Size(100, 20);
            this.ImportUpdateSecondsTextBox.TabIndex = 3;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 39);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(183, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Interval for Import Updates (Seconds)";
            // 
            // ImportUpdateEnableCheckBox
            // 
            this.ImportUpdateEnableCheckBox.AutoSize = true;
            this.ImportUpdateEnableCheckBox.Location = new System.Drawing.Point(6, 19);
            this.ImportUpdateEnableCheckBox.Name = "ImportUpdateEnableCheckBox";
            this.ImportUpdateEnableCheckBox.Size = new System.Drawing.Size(166, 17);
            this.ImportUpdateEnableCheckBox.TabIndex = 1;
            this.ImportUpdateEnableCheckBox.Text = "Enable Import Update Thread";
            this.ImportUpdateEnableCheckBox.UseVisualStyleBackColor = true;
            // 
            // AutoOKButton
            // 
            this.AutoOKButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.AutoOKButton.Location = new System.Drawing.Point(182, 271);
            this.AutoOKButton.Name = "AutoOKButton";
            this.AutoOKButton.Size = new System.Drawing.Size(75, 23);
            this.AutoOKButton.TabIndex = 9;
            this.AutoOKButton.Text = "OK";
            this.AutoOKButton.UseVisualStyleBackColor = true;
            // 
            // AutoCancelButton
            // 
            this.AutoCancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.AutoCancelButton.Location = new System.Drawing.Point(263, 271);
            this.AutoCancelButton.Name = "AutoCancelButton";
            this.AutoCancelButton.Size = new System.Drawing.Size(75, 23);
            this.AutoCancelButton.TabIndex = 8;
            this.AutoCancelButton.Text = "Cancel";
            this.AutoCancelButton.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.AutoImportTextBox);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.AutoImportCheckBox);
            this.groupBox1.Location = new System.Drawing.Point(12, 187);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(326, 78);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Auto Import";
            // 
            // textBox1
            // 
            this.AutoImportTextBox.Location = new System.Drawing.Point(196, 36);
            this.AutoImportTextBox.Name = "textBox1";
            this.AutoImportTextBox.Size = new System.Drawing.Size(100, 20);
            this.AutoImportTextBox.TabIndex = 3;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(7, 39);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(165, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Interval for Auto Import (Seconds)";
            // 
            // checkBox1
            // 
            this.AutoImportCheckBox.AutoSize = true;
            this.AutoImportCheckBox.Location = new System.Drawing.Point(6, 19);
            this.AutoImportCheckBox.Name = "checkBox1";
            this.AutoImportCheckBox.Size = new System.Drawing.Size(153, 17);
            this.AutoImportCheckBox.TabIndex = 1;
            this.AutoImportCheckBox.Text = "Enable Auto Import Thread";
            this.AutoImportCheckBox.UseVisualStyleBackColor = true;
            // 
            // BGThreadsSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(352, 303);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.AutoOKButton);
            this.Controls.Add(this.AutoCancelButton);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.Name = "BGThreadsSettings";
            this.Text = "Background Thread Settings";
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox DaemonUpdateSecondsTextBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox DaemonThreadEnableCheckBox;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.TextBox ImportUpdateSecondsTextBox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox ImportUpdateEnableCheckBox;
        private System.Windows.Forms.Button AutoOKButton;
        private System.Windows.Forms.Button AutoCancelButton;
        private System.Windows.Forms.CheckBox AutoImportCheckBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox AutoImportTextBox;
        private System.Windows.Forms.GroupBox groupBox1;
    }
}