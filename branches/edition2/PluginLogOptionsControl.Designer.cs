namespace RightEdgeOandaPlugin
{
    partial class PluginLogOptionsControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.button_FileSelect = new System.Windows.Forms.Button();
            this.textBox_FileName = new System.Windows.Forms.TextBox();
            this.checkBox_LogErrors = new System.Windows.Forms.CheckBox();
            this.checkBox_ShowErrors = new System.Windows.Forms.CheckBox();
            this.checkBox_LogExceptions = new System.Windows.Forms.CheckBox();
            this.checkBox_LogDebug = new System.Windows.Forms.CheckBox();
            this.groupBox1.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.tableLayoutPanel1);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(231, 209);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Basic Log Options";
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.button_FileSelect, 0, 4);
            this.tableLayoutPanel1.Controls.Add(this.textBox_FileName, 1, 4);
            this.tableLayoutPanel1.Controls.Add(this.checkBox_LogErrors, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.checkBox_ShowErrors, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.checkBox_LogExceptions, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.checkBox_LogDebug, 0, 3);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(3, 16);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 5;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(225, 190);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // button_FileSelect
            // 
            this.button_FileSelect.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.button_FileSelect.Location = new System.Drawing.Point(4, 159);
            this.button_FileSelect.Name = "button_FileSelect";
            this.button_FileSelect.Size = new System.Drawing.Size(24, 24);
            this.button_FileSelect.TabIndex = 4;
            this.button_FileSelect.Text = "...";
            this.button_FileSelect.UseVisualStyleBackColor = true;
            this.button_FileSelect.Click += new System.EventHandler(this.button_FileSelect_Click);
            // 
            // textBox_FileName
            // 
            this.textBox_FileName.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_FileName.Location = new System.Drawing.Point(35, 161);
            this.textBox_FileName.Name = "textBox_FileName";
            this.textBox_FileName.ReadOnly = true;
            this.textBox_FileName.Size = new System.Drawing.Size(187, 20);
            this.textBox_FileName.TabIndex = 5;
            this.textBox_FileName.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.textBox_FileName_KeyPress);
            // 
            // checkBox_LogErrors
            // 
            this.checkBox_LogErrors.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.checkBox_LogErrors.AutoSize = true;
            this.tableLayoutPanel1.SetColumnSpan(this.checkBox_LogErrors, 2);
            this.checkBox_LogErrors.Location = new System.Drawing.Point(3, 10);
            this.checkBox_LogErrors.Name = "checkBox_LogErrors";
            this.checkBox_LogErrors.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.checkBox_LogErrors.Size = new System.Drawing.Size(82, 17);
            this.checkBox_LogErrors.TabIndex = 0;
            this.checkBox_LogErrors.Text = "Log Errors";
            this.checkBox_LogErrors.UseVisualStyleBackColor = true;
            this.checkBox_LogErrors.CheckedChanged += new System.EventHandler(this.checkBox_LogErrors_CheckedChanged);
            // 
            // checkBox_ShowErrors
            // 
            this.checkBox_ShowErrors.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.checkBox_ShowErrors.AutoSize = true;
            this.tableLayoutPanel1.SetColumnSpan(this.checkBox_ShowErrors, 2);
            this.checkBox_ShowErrors.Location = new System.Drawing.Point(3, 48);
            this.checkBox_ShowErrors.Name = "checkBox_ShowErrors";
            this.checkBox_ShowErrors.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.checkBox_ShowErrors.Size = new System.Drawing.Size(91, 17);
            this.checkBox_ShowErrors.TabIndex = 1;
            this.checkBox_ShowErrors.Text = "Show Errors";
            this.checkBox_ShowErrors.UseVisualStyleBackColor = true;
            this.checkBox_ShowErrors.CheckedChanged += new System.EventHandler(this.checkBox_ShowErrors_CheckedChanged);
            // 
            // checkBox_LogExceptions
            // 
            this.checkBox_LogExceptions.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.checkBox_LogExceptions.AutoSize = true;
            this.tableLayoutPanel1.SetColumnSpan(this.checkBox_LogExceptions, 2);
            this.checkBox_LogExceptions.Location = new System.Drawing.Point(3, 86);
            this.checkBox_LogExceptions.Name = "checkBox_LogExceptions";
            this.checkBox_LogExceptions.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.checkBox_LogExceptions.Size = new System.Drawing.Size(107, 17);
            this.checkBox_LogExceptions.TabIndex = 2;
            this.checkBox_LogExceptions.Text = "Log Exceptions";
            this.checkBox_LogExceptions.UseVisualStyleBackColor = true;
            this.checkBox_LogExceptions.CheckedChanged += new System.EventHandler(this.checkBox_LogExceptions_CheckedChanged);
            // 
            // checkBox_LogDebug
            // 
            this.checkBox_LogDebug.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.checkBox_LogDebug.AutoSize = true;
            this.tableLayoutPanel1.SetColumnSpan(this.checkBox_LogDebug, 2);
            this.checkBox_LogDebug.Location = new System.Drawing.Point(3, 124);
            this.checkBox_LogDebug.Name = "checkBox_LogDebug";
            this.checkBox_LogDebug.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.checkBox_LogDebug.Size = new System.Drawing.Size(87, 17);
            this.checkBox_LogDebug.TabIndex = 3;
            this.checkBox_LogDebug.Text = "Log Debug";
            this.checkBox_LogDebug.UseVisualStyleBackColor = true;
            this.checkBox_LogDebug.CheckedChanged += new System.EventHandler(this.checkBox_LogDebug_CheckedChanged);
            // 
            // PluginLogOptionsControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Name = "PluginLogOptionsControl";
            this.Size = new System.Drawing.Size(231, 209);
            this.groupBox1.ResumeLayout(false);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.CheckBox checkBox_LogErrors;
        private System.Windows.Forms.CheckBox checkBox_ShowErrors;
        private System.Windows.Forms.CheckBox checkBox_LogExceptions;
        private System.Windows.Forms.CheckBox checkBox_LogDebug;
        private System.Windows.Forms.Button button_FileSelect;
        private System.Windows.Forms.TextBox textBox_FileName;
    }
}
