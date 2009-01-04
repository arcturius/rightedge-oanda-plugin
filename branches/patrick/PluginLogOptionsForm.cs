using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class PluginLogOptionsForm : Form
    {
        private PluginLogOptionsControl pluginLogOptionsControl;
        public OAPluginLogOptions LogOptions { set { pluginLogOptionsControl.LogOptions = value; } get { return pluginLogOptionsControl.LogOptions; } }

        public PluginLogOptionsForm(OAPluginLogOptions l)
        {
            InitializeComponent();

            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.SuspendLayout();
            SuspendLayout();
            pluginLogOptionsControl = new PluginLogOptionsControl();
            splitContainer1.Panel1.Controls.Add(pluginLogOptionsControl);
            pluginLogOptionsControl.Dock = System.Windows.Forms.DockStyle.Fill;
            pluginLogOptionsControl.Location = new System.Drawing.Point(0, 0);
            pluginLogOptionsControl.Name = "pluginLogOptionsControl";
            pluginLogOptionsControl.Size = new System.Drawing.Size(242, 151);
            pluginLogOptionsControl.TabIndex = 0;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);

            pluginLogOptionsControl.LogOptions = l;
        }

        public PluginLogOptionsForm()
        {
            InitializeComponent();

            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.SuspendLayout();
            SuspendLayout();
            pluginLogOptionsControl = new PluginLogOptionsControl();
            splitContainer1.Panel1.Controls.Add(pluginLogOptionsControl);
            pluginLogOptionsControl.Dock = System.Windows.Forms.DockStyle.Fill;
            pluginLogOptionsControl.Location = new System.Drawing.Point(0, 0);
            pluginLogOptionsControl.Name = "pluginLogOptionsControl";
            pluginLogOptionsControl.Size = new System.Drawing.Size(242, 151);
            pluginLogOptionsControl.TabIndex = 0;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);

        }

        private void button_OK_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void button_Cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
