using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

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

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
