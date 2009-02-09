using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RightEdgeOandaPlugin
{
    public partial class OandAPluginOptionsForm : Form
    {
        private OAPluginOptions _opts;
        public OAPluginOptions Opts { get { return (_opts); } set { _opts = value; oandAPluginOptionsControl1.Opts = _opts; } }

        public OandAPluginOptionsForm(OAPluginOptions opts)
        {
            InitializeComponent();
            _opts = opts;
            oandAPluginOptionsControl1.Opts = _opts;
        }

        public OandAPluginOptionsForm()
        {
            InitializeComponent();
            _opts = new OAPluginOptions();
            oandAPluginOptionsControl1.Opts = _opts;
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_opts.TradeEntityFileName))
            {
                MessageBox.Show("There is no trade entities file specified!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            System.IO.FileInfo fi=new System.IO.FileInfo(_opts.TradeEntityFileName);
            if (fi.Exists && fi.IsReadOnly)
            {
                MessageBox.Show("The spepcified trade entities file is read only!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            FunctionObjectResult<TradeEntities> eres = TradeEntities.newFromSettings<TradeEntities>(_opts.TradeEntityFileName);
            if (eres.ResultObject == null)
            {//if the file was empty, the results will be empty as well....
                eres.ResultObject = new TradeEntities();//create some generic defaults...
                eres.ResultObject.FileName = _opts.TradeEntityFileName;
            }

            TradeEntitiesForm tef = new TradeEntitiesForm(eres.ResultObject);

            DialogResult dres = tef.ShowDialog();
            if (dres == DialogResult.OK)
            {
                FunctionResult fres = tef.Entities.saveSettings<TradeEntities>();
                if (fres.Error)
                {
                    MessageBox.Show("Unable to save the trade entities!\n\n" + fres.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ButtonOK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void toolStripButton2_Click(object sender, EventArgs ea)
        {
            try
            {
                Process proc = new Process();
                proc.StartInfo.WorkingDirectory = (string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\RightEdgeOandaPlugin", "AppDir", "C:\\");
                proc.StartInfo.FileName = "Monitor.exe";
                if (!proc.Start())
                {
                    MessageBox.Show("There was a problem launching the monitor.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("There was an unhandled exception!\r\n\r\n" + e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }
}
