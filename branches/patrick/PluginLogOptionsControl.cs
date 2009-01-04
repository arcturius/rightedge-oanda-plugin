using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class PluginLogOptionsControl : UserControl
    {
        OAPluginLogOptions _log_opts = new OAPluginLogOptions();
        public OAPluginLogOptions LogOptions { set { _log_opts = value; refreshControlValues(); } get { return _log_opts; } }

        public PluginLogOptionsControl(OAPluginLogOptions l)
        {
            InitializeComponent();
            _log_opts = l;
            refreshControlValues();
        }

        public PluginLogOptionsControl()
        {
            InitializeComponent();
            refreshControlValues();
        }

        private void refreshControlValues()
        {
            textBox_FileName.Text = _log_opts.LogFileName;
            checkBox_LogDebug.Checked = _log_opts.LogDebug;
            checkBox_LogErrors.Checked = _log_opts.LogErrors;
            checkBox_ShowErrors.Checked = _log_opts.ShowErrors;
            checkBox_LogExceptions.Checked = _log_opts.LogExceptions;
        }

        private void selectFileName()
        {
            OpenFileDialog fd = new OpenFileDialog();

            fd.FileName = _log_opts.LogFileName;
            fd.Filter = "data files (*.xml;*.csv)|*.xml;*.csv|log files (*.log)|*.log|all files (*.*)|*.*";

            if (fd.FileName.EndsWith(".xml")) { fd.FilterIndex = 1; }
            else if (fd.FileName.EndsWith(".csv")) { fd.FilterIndex = 1; }
            else if (fd.FileName.EndsWith(".log")) { fd.FilterIndex = 2; }
            else { fd.FilterIndex = 3; }

            fd.CheckFileExists = false;
            fd.CheckPathExists = true;

            DialogResult fdres = fd.ShowDialog();
            if (fdres == DialogResult.OK)
            {
                _log_opts.LogFileName = fd.FileName;
                textBox_FileName.Text = _log_opts.LogFileName;
            }
        }
        
        private void button_FileSelect_Click(object sender, EventArgs e)
        {
            selectFileName();
        }
        private void textBox_FileName_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == ' ') { selectFileName(); }
        }

        private void checkBox_LogErrors_CheckedChanged(object sender, EventArgs e)
        {
            _log_opts.LogErrors = checkBox_LogErrors.Checked;
        }

        private void checkBox_ShowErrors_CheckedChanged(object sender, EventArgs e)
        {
            _log_opts.ShowErrors = checkBox_ShowErrors.Checked;
        }

        private void checkBox_LogExceptions_CheckedChanged(object sender, EventArgs e)
        {
            _log_opts.LogExceptions = checkBox_LogExceptions.Checked;
        }

        private void checkBox_LogDebug_CheckedChanged(object sender, EventArgs e)
        {
            _log_opts.LogDebug = checkBox_LogDebug.Checked;
        }
    }
}
