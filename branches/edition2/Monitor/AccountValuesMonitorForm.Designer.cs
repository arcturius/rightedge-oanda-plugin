namespace Monitor
{
    partial class AccountValuesMonitorForm
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AccountValuesMonitorForm));
            this.toolStripContainer1 = new System.Windows.Forms.ToolStripContainer();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.statusLabel_State = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusLabel_Message = new System.Windows.Forms.ToolStripStatusLabel();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.Account = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.AccountName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Balance = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.AvailableMargin = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UsedMargin = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MarginRate = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openTradeEntitiesFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripButton_AutoRefresh = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton_OpenFile = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel_FileName = new System.Windows.Forms.ToolStripLabel();
            this.toolStripTextBox_AccountFileName = new System.Windows.Forms.ToolStripTextBox();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStrip2 = new System.Windows.Forms.ToolStrip();
            this.toolStripButton_EditEntities = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton_SelectEntities = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripTextBox_EntityFileName = new System.Windows.Forms.ToolStripTextBox();
            this.toolStripSeparator5 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripContainer1.BottomToolStripPanel.SuspendLayout();
            this.toolStripContainer1.ContentPanel.SuspendLayout();
            this.toolStripContainer1.TopToolStripPanel.SuspendLayout();
            this.toolStripContainer1.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.menuStrip1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.toolStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStripContainer1
            // 
            // 
            // toolStripContainer1.BottomToolStripPanel
            // 
            this.toolStripContainer1.BottomToolStripPanel.Controls.Add(this.statusStrip1);
            // 
            // toolStripContainer1.ContentPanel
            // 
            this.toolStripContainer1.ContentPanel.Controls.Add(this.dataGridView1);
            this.toolStripContainer1.ContentPanel.Size = new System.Drawing.Size(693, 110);
            this.toolStripContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.toolStripContainer1.Location = new System.Drawing.Point(0, 0);
            this.toolStripContainer1.Name = "toolStripContainer1";
            this.toolStripContainer1.Size = new System.Drawing.Size(693, 206);
            this.toolStripContainer1.TabIndex = 0;
            this.toolStripContainer1.Text = "toolStripContainer1";
            // 
            // toolStripContainer1.TopToolStripPanel
            // 
            this.toolStripContainer1.TopToolStripPanel.Controls.Add(this.menuStrip1);
            this.toolStripContainer1.TopToolStripPanel.Controls.Add(this.toolStrip1);
            this.toolStripContainer1.TopToolStripPanel.Controls.Add(this.toolStrip2);
            // 
            // statusStrip1
            // 
            this.statusStrip1.Dock = System.Windows.Forms.DockStyle.None;
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel_State,
            this.statusLabel_Message});
            this.statusStrip1.Location = new System.Drawing.Point(0, 0);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.ShowItemToolTips = true;
            this.statusStrip1.Size = new System.Drawing.Size(693, 22);
            this.statusStrip1.TabIndex = 0;
            // 
            // statusLabel_State
            // 
            this.statusLabel_State.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.statusLabel_State.Image = global::Monitor.Properties.Resources.Status_Off;
            this.statusLabel_State.Name = "statusLabel_State";
            this.statusLabel_State.Size = new System.Drawing.Size(16, 17);
            this.statusLabel_State.ToolTipText = "Auto refresh off.";
            // 
            // statusLabel_Message
            // 
            this.statusLabel_Message.AutoToolTip = true;
            this.statusLabel_Message.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.statusLabel_Message.ForeColor = System.Drawing.SystemColors.ControlText;
            this.statusLabel_Message.Name = "statusLabel_Message";
            this.statusLabel_Message.Size = new System.Drawing.Size(662, 17);
            this.statusLabel_Message.Spring = true;
            this.statusLabel_Message.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.AllowUserToOrderColumns = true;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Account,
            this.AccountName,
            this.Balance,
            this.AvailableMargin,
            this.UsedMargin,
            this.MarginRate});
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.Size = new System.Drawing.Size(693, 110);
            this.dataGridView1.TabIndex = 0;
            // 
            // Account
            // 
            this.Account.HeaderText = "Account";
            this.Account.Name = "Account";
            this.Account.ReadOnly = true;
            this.Account.ToolTipText = "Account Number";
            // 
            // AccountName
            // 
            this.AccountName.HeaderText = "Name";
            this.AccountName.Name = "AccountName";
            this.AccountName.ReadOnly = true;
            // 
            // Balance
            // 
            this.Balance.HeaderText = "Balance";
            this.Balance.Name = "Balance";
            this.Balance.ReadOnly = true;
            this.Balance.ToolTipText = "Account Balance";
            // 
            // AvailableMargin
            // 
            this.AvailableMargin.HeaderText = "AvailableMargin";
            this.AvailableMargin.Name = "AvailableMargin";
            this.AvailableMargin.ReadOnly = true;
            this.AvailableMargin.ToolTipText = "Margin Available";
            // 
            // UsedMargin
            // 
            this.UsedMargin.HeaderText = "UsedMargin";
            this.UsedMargin.Name = "UsedMargin";
            this.UsedMargin.ReadOnly = true;
            // 
            // MarginRate
            // 
            this.MarginRate.HeaderText = "MarginRate";
            this.MarginRate.Name = "MarginRate";
            this.MarginRate.ReadOnly = true;
            // 
            // menuStrip1
            // 
            this.menuStrip1.Dock = System.Windows.Forms.DockStyle.None;
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(693, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.openTradeEntitiesFileToolStripMenuItem,
            this.toolStripSeparator1,
            this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(35, 20);
            this.fileToolStripMenuItem.Text = "&File";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.Size = new System.Drawing.Size(206, 22);
            this.openToolStripMenuItem.Text = "Open &Account Values File";
            this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // openTradeEntitiesFileToolStripMenuItem
            // 
            this.openTradeEntitiesFileToolStripMenuItem.Name = "openTradeEntitiesFileToolStripMenuItem";
            this.openTradeEntitiesFileToolStripMenuItem.Size = new System.Drawing.Size(206, 22);
            this.openTradeEntitiesFileToolStripMenuItem.Text = "Open &Trade Entities File";
            this.openTradeEntitiesFileToolStripMenuItem.Click += new System.EventHandler(this.openTradeEntitiesFileToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(203, 6);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(206, 22);
            this.exitToolStripMenuItem.Text = "E&xit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.Dock = System.Windows.Forms.DockStyle.None;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton_AutoRefresh,
            this.toolStripButton_OpenFile,
            this.toolStripSeparator2,
            this.toolStripLabel_FileName,
            this.toolStripTextBox_AccountFileName,
            this.toolStripSeparator3});
            this.toolStrip1.Location = new System.Drawing.Point(3, 24);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(336, 25);
            this.toolStrip1.TabIndex = 1;
            // 
            // toolStripButton_AutoRefresh
            // 
            this.toolStripButton_AutoRefresh.CheckOnClick = true;
            this.toolStripButton_AutoRefresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton_AutoRefresh.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton_AutoRefresh.Image")));
            this.toolStripButton_AutoRefresh.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton_AutoRefresh.Name = "toolStripButton_AutoRefresh";
            this.toolStripButton_AutoRefresh.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton_AutoRefresh.Text = "Auto Refresh";
            this.toolStripButton_AutoRefresh.Click += new System.EventHandler(this.toolStripButton_AutoRefresh_Click);
            // 
            // toolStripButton_OpenFile
            // 
            this.toolStripButton_OpenFile.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton_OpenFile.Image = global::Monitor.Properties.Resources.AccountValuesMonitor_16x16x8;
            this.toolStripButton_OpenFile.ImageTransparentColor = System.Drawing.Color.Silver;
            this.toolStripButton_OpenFile.Name = "toolStripButton_OpenFile";
            this.toolStripButton_OpenFile.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton_OpenFile.Text = "Open Account Values File";
            this.toolStripButton_OpenFile.Click += new System.EventHandler(this.toolStripButton_OpenFile_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripLabel_FileName
            // 
            this.toolStripLabel_FileName.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.toolStripLabel_FileName.Name = "toolStripLabel_FileName";
            this.toolStripLabel_FileName.Size = new System.Drawing.Size(64, 22);
            this.toolStripLabel_FileName.Text = "Current AV:";
            this.toolStripLabel_FileName.ToolTipText = "Current Account Values File";
            // 
            // toolStripTextBox_AccountFileName
            // 
            this.toolStripTextBox_AccountFileName.Name = "toolStripTextBox_AccountFileName";
            this.toolStripTextBox_AccountFileName.Size = new System.Drawing.Size(200, 25);
            this.toolStripTextBox_AccountFileName.ToolTipText = "Account Values File Name";
            this.toolStripTextBox_AccountFileName.TextChanged += new System.EventHandler(this.toolStripTextBox_FileName_TextChanged);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStrip2
            // 
            this.toolStrip2.Dock = System.Windows.Forms.DockStyle.None;
            this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton_EditEntities,
            this.toolStripButton_SelectEntities,
            this.toolStripSeparator4,
            this.toolStripLabel1,
            this.toolStripTextBox_EntityFileName,
            this.toolStripSeparator5});
            this.toolStrip2.Location = new System.Drawing.Point(3, 49);
            this.toolStrip2.Name = "toolStrip2";
            this.toolStrip2.Size = new System.Drawing.Size(335, 25);
            this.toolStrip2.TabIndex = 2;
            // 
            // toolStripButton_EditEntities
            // 
            this.toolStripButton_EditEntities.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton_EditEntities.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton_EditEntities.Image")));
            this.toolStripButton_EditEntities.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton_EditEntities.Name = "toolStripButton_EditEntities";
            this.toolStripButton_EditEntities.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton_EditEntities.Text = "Edit Trade Entities File";
            this.toolStripButton_EditEntities.Click += new System.EventHandler(this.toolStripButton_EditEntities_Click);
            // 
            // toolStripButton_SelectEntities
            // 
            this.toolStripButton_SelectEntities.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton_SelectEntities.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton_SelectEntities.Image")));
            this.toolStripButton_SelectEntities.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton_SelectEntities.Name = "toolStripButton_SelectEntities";
            this.toolStripButton_SelectEntities.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton_SelectEntities.Text = "Select Trade Entities File";
            this.toolStripButton_SelectEntities.Click += new System.EventHandler(this.toolStripButton_SelectEntities_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            this.toolStripSeparator4.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(63, 22);
            this.toolStripLabel1.Text = "Current TE:";
            this.toolStripLabel1.ToolTipText = "Current Trade Entity File";
            // 
            // toolStripTextBox_EntityFileName
            // 
            this.toolStripTextBox_EntityFileName.Name = "toolStripTextBox_EntityFileName";
            this.toolStripTextBox_EntityFileName.Size = new System.Drawing.Size(200, 25);
            this.toolStripTextBox_EntityFileName.ToolTipText = "Trade Entities File Name";
            this.toolStripTextBox_EntityFileName.TextChanged += new System.EventHandler(this.toolStripTextBox_EntityFileName_TextChanged);
            // 
            // toolStripSeparator5
            // 
            this.toolStripSeparator5.Name = "toolStripSeparator5";
            this.toolStripSeparator5.Size = new System.Drawing.Size(6, 25);
            // 
            // AccountValuesMonitorForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(693, 206);
            this.Controls.Add(this.toolStripContainer1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "AccountValuesMonitorForm";
            this.Text = "Account Values Monitor";
            this.toolStripContainer1.BottomToolStripPanel.ResumeLayout(false);
            this.toolStripContainer1.BottomToolStripPanel.PerformLayout();
            this.toolStripContainer1.ContentPanel.ResumeLayout(false);
            this.toolStripContainer1.TopToolStripPanel.ResumeLayout(false);
            this.toolStripContainer1.TopToolStripPanel.PerformLayout();
            this.toolStripContainer1.ResumeLayout(false);
            this.toolStripContainer1.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.toolStrip2.ResumeLayout(false);
            this.toolStrip2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ToolStripContainer toolStripContainer1;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.ToolStripButton toolStripButton_AutoRefresh;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel toolStripLabel_FileName;
        private System.Windows.Forms.ToolStripTextBox toolStripTextBox_AccountFileName;
        private System.Windows.Forms.ToolStripButton toolStripButton_OpenFile;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel_State;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel_Message;
        private System.Windows.Forms.DataGridViewTextBoxColumn Account;
        private System.Windows.Forms.DataGridViewTextBoxColumn AccountName;
        private System.Windows.Forms.DataGridViewTextBoxColumn Balance;
        private System.Windows.Forms.DataGridViewTextBoxColumn AvailableMargin;
        private System.Windows.Forms.DataGridViewTextBoxColumn UsedMargin;
        private System.Windows.Forms.DataGridViewTextBoxColumn MarginRate;
        private System.Windows.Forms.ToolStrip toolStrip2;
        private System.Windows.Forms.ToolStripButton toolStripButton_EditEntities;
        private System.Windows.Forms.ToolStripButton toolStripButton_SelectEntities;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
        private System.Windows.Forms.ToolStripLabel toolStripLabel1;
        private System.Windows.Forms.ToolStripTextBox toolStripTextBox_EntityFileName;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
        private System.Windows.Forms.ToolStripMenuItem openTradeEntitiesFileToolStripMenuItem;
    }
}

