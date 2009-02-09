using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using Microsoft.Win32;

using RightEdgeOandaPlugin;

namespace Monitor
{
    public partial class AccountValuesMonitorForm : Form
    {
        private string _trade_entity_fname = null;
        private AccountValuesStore _avs = null;
        private RefreshThread _rt = null;

        public AccountValuesMonitorForm()
        {
            _rt = new RefreshThread(this);
            InitializeComponent();
            InitFromCommandLine();
        }

        public void InitFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string avfn = null;
            string tefn = null;
            for(int i=0;i<args.Length;i++)
            {
                if (avfn == null && args[i] == "/A")
                {
                    if (++i >= args.Length)
                    {//missing file...end of args...
                        CommandLineHelpForm clh = new CommandLineHelpForm();
                        clh.Show();
                        return;
                    }
                    avfn = args[i];
                }
                if (tefn == null && args[i] == "/T")
                {
                    if (++i >= args.Length)
                    {//missing file...end of args...
                        CommandLineHelpForm clh = new CommandLineHelpForm();
                        clh.Show();
                        return;
                    }
                    tefn = args[i];
                }
            }
            if (!string.IsNullOrEmpty(tefn))
            {
                _trade_entity_fname = tefn;
            }
            if (!string.IsNullOrEmpty(avfn))
            {
                FunctionResult r = setAccountValuesFile(avfn);
                if (r.Error)
                {
                    RespondToError(r);
                    return;
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            if (_rt.IsRunning)
            {
                FunctionResult fr = _rt.Stop();
                if (fr.Error)
                {
                    DialogResult = DialogResult.Abort;
                    RespondToError(fr);
                }
            }
            Close();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FunctionResult r = setAccountValuesFileDialog();
            if (r.Error)
            {
                RespondToError(r);
                return;
            }
        }

        private FunctionResult setAccountValuesFileDialog()
        {
            string s = string.Empty;//select s from a file dialog
            string dd = (string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\RightEdgeOandaPlugin", "DataDir", "C:");
            string p = dd;
            string n = "account_values";
            string x = ".xml";
            if (_avs != null)
            { 
                System.IO.FileInfo fi= new System.IO.FileInfo(_avs.FileName);
                p = fi.DirectoryName;
                n = fi.Name;
                x = fi.Extension;
            }

            OpenFileDialog fd = new OpenFileDialog();
            
            //the custom places don't seem to work...
            fd.AutoUpgradeEnabled = true;
            fd.CustomPlaces.Clear();
            fd.CustomPlaces.Add(dd);
            if (p != dd) { fd.CustomPlaces.Add(p); }
            /////////
            
            fd.RestoreDirectory = true;
            fd.CheckFileExists = true;
            fd.Multiselect = false;
            fd.InitialDirectory = p;
            fd.FileName = n;
            fd.DefaultExt = x;
            fd.Filter = "Account Value Data (*" + x + ")|*" + x + "|All Files (*.*)|*.*";
            fd.FilterIndex = 1;

            DialogResult fdres=fd.ShowDialog();
            if (fdres == DialogResult.Cancel)
            {
                statusLabel_Message.Text = "Account Values file not selected.";
                return new FunctionResult();
            }

            if (fdres != DialogResult.OK)
            {
                FunctionResult res = new FunctionResult();
                res.setError("Account Values file select error.");
                return res;
            }

            s = fd.FileName;
            return setAccountValuesFile(s);
        }
        private FunctionResult setAccountValuesFile(string fname)
        {
            //set the text box /tool tip to the fname
            toolStripTextBox_AccountFileName.Text = fname;
            toolStripTextBox_AccountFileName.ToolTipText = fname;

            //clean and refresh the data display

            if (toolStripButton_AutoRefresh.Checked)
            {
                toolStripButton_AutoRefresh.Checked = false;
            }
            FunctionResult r = _rt.Stop();
            if (r.Error) { return r; }

            statusLabel_State.Image = global::Monitor.Properties.Resources.Status_Off;
            statusLabel_State.ToolTipText = "Auto refresh off.";


            //create the new _avs
            FunctionObjectResult<AccountValuesStore> avsres = AccountValuesStore.newFromSettings<AccountValuesStore>(fname);
            if (avsres.Error)
            {
                return FunctionResult.newError(avsres.Message);
            }
            _avs = avsres.ResultObject;

            return RepopulateDataGrid(true);
        }

        public delegate FunctionResult RepopDelegate(bool set_state_off);
        public FunctionResult RepopulateDataGrid(bool set_state_off)
        {
            dataGridView1.Rows.Clear();

            if (set_state_off)
            {
                statusLabel_State.Image = global::Monitor.Properties.Resources.Status_Off;
                statusLabel_State.ToolTipText = "Auto refresh off.";
            }
            else
            {
                statusLabel_State.Image = global::Monitor.Properties.Resources.Status_On;
                statusLabel_State.ToolTipText = "Auto refresh on.";
            }

            lock (_avs)
            {
                statusLabel_Message.Text = "Last File Update : '" + _avs.LastFileLoad.ToString() + "'";
                dataGridView1.Rows.Add(_avs.Values.Count);

                //populate the data grid rows...
                int ri = 0;
                foreach (string av_key in _avs.Values.Keys)
                {
                    dataGridView1.Rows[ri].Cells["Account"].Value = _avs.Values[av_key].AccountID;
                    dataGridView1.Rows[ri].Cells["Account"].ToolTipText = _avs.Values[av_key].AccountID;

                    dataGridView1.Rows[ri].Cells["AccountName"].Value = _avs.Values[av_key].AccountName;
                    dataGridView1.Rows[ri].Cells["AccountName"].ToolTipText = _avs.Values[av_key].AccountName;

                    dataGridView1.Rows[ri].Cells["Balance"].Value = _avs.Values[av_key].Balance;
                    dataGridView1.Rows[ri].Cells["Balance"].ToolTipText = _avs.Values[av_key].Balance + " : BTS{" + _avs.Values[av_key].BalanceTimeStamp.ToString() + "}";

                    dataGridView1.Rows[ri].Cells["AvailableMargin"].Value = _avs.Values[av_key].MarginAvailable;
                    dataGridView1.Rows[ri].Cells["AvailableMargin"].ToolTipText = _avs.Values[av_key].MarginAvailable + " : MTS{" + _avs.Values[av_key].MarginTimeStamp.ToString() + "}";

                    dataGridView1.Rows[ri].Cells["UsedMargin"].Value = _avs.Values[av_key].MarginUsed;
                    dataGridView1.Rows[ri].Cells["UsedMargin"].ToolTipText = _avs.Values[av_key].MarginUsed + " : MTS{" + _avs.Values[av_key].MarginTimeStamp.ToString() + "}";

                    dataGridView1.Rows[ri].Cells["MarginRate"].Value = _avs.Values[av_key].MarginRate;
                    dataGridView1.Rows[ri].Cells["MarginRate"].ToolTipText = _avs.Values[av_key].MarginRate + " : MTS{" + _avs.Values[av_key].MarginTimeStamp.ToString() + "}";
                    
                    dataGridView1.Rows[ri].Selected = false;
                    ri++;
                }
            }
            dataGridView1.Refresh();

            return new FunctionResult();
        }

        public delegate void RespondToErrorDelegate(FunctionResult res);
        public void RespondToError(FunctionResult res)
        {
            toolStripButton_AutoRefresh.Checked = false;

            statusLabel_State.Image = global::Monitor.Properties.Resources.Status_Error;
            statusLabel_State.ToolTipText = "Error.";
            statusLabel_Message.Text = res.Message;

            MessageBox.Show(res.Message, "Monitor Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        public void RespondToEntityError(FunctionResult res)
        {
            statusLabel_Message.Text = res.Message;
            MessageBox.Show(res.Message, "Monitor Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void toolStripButton_AutoRefresh_Click(object sender, EventArgs e)
        {
            bool is_checked = toolStripButton_AutoRefresh.Checked;
            if (is_checked)
            {
                string fn = toolStripTextBox_AccountFileName.Text;
                if (string.IsNullOrEmpty(fn))
                {
                    RespondToError(FunctionResult.newError("No account values file name specified."));
                    return;
                }
                //startup auto refresh thread
                statusLabel_State.Image = global::Monitor.Properties.Resources.Status_On;
                statusLabel_State.ToolTipText = "Auto refresh on.";
                statusLabel_Message.Text = "Auto refresh on.";
                FunctionResult r = _rt.Start(_avs);
                if (r.Error)
                {
                    RespondToError(r);
                    return;
                }
            }
            else
            {//going disabled
                if (_rt.IsRunning)
                {//shutdown auto refresh thread
                    FunctionResult r = _rt.Stop();
                    if (r.Error)
                    {
                        RespondToError(r);
                        return;
                    }
                    statusLabel_State.Image = global::Monitor.Properties.Resources.Status_Off;
                    statusLabel_State.ToolTipText = "Auto refresh off.";
                    statusLabel_Message.Text = "Auto refresh off.";
                }
            }
        }

        private void toolStripButton_OpenFile_Click(object sender, EventArgs e)
        {
            FunctionResult r = setAccountValuesFileDialog();
            if (r.Error)
            {
                RespondToError(r);
                return;
            }
        }

        private void toolStripTextBox_FileName_TextChanged(object sender, EventArgs e)
        {
            FunctionResult r = setAccountValuesFile(toolStripTextBox_AccountFileName.Text);
            if (r.Error)
            {
                RespondToError(r);
                return;
            }
        }


        private FunctionResult editTradeEntitiesFile()
        {
            if (string.IsNullOrEmpty(_trade_entity_fname))
            {
                return FunctionResult.newError("There is no trade entities file specified!");
            }
            System.IO.FileInfo fi = new System.IO.FileInfo(_trade_entity_fname);
            if (fi.Exists && fi.IsReadOnly)
            {
                return FunctionResult.newError("The spepcified trade entities file is read only!");
            }

            FunctionObjectResult<TradeEntities> eres = TradeEntities.newFromSettings<TradeEntities>(_trade_entity_fname);
            if (eres.ResultObject == null)
            {//if the file was empty, the results will be empty as well....
                eres.ResultObject = new TradeEntities();//create some generic defaults...
                eres.ResultObject.FileName = _trade_entity_fname;
            }

            TradeEntitiesForm tef = new TradeEntitiesForm(eres.ResultObject);

            DialogResult dres = tef.ShowDialog();
            if (dres == DialogResult.OK)
            {
                FunctionResult fres = tef.Entities.saveSettings<TradeEntities>();
                if (fres.Error)
                {
                    return FunctionResult.newError("Unable to save the trade entities!\n\n" + fres.Message);
                }
                statusLabel_Message.Text = "Trade Entities file saved.";
            }
            else
            {
                statusLabel_Message.Text = "Trade Entities file not saved.";
            }
            return new FunctionResult();
        }

        private FunctionResult selectTradeEntitiesFile()
        {
            string s = string.Empty;//select s from a file dialog
            string dd = (string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\RightEdgeOandaPlugin", "DataDir", "C:");
            string p = dd;
            string n = "trade_entities";
            string x = ".xml";
            if (_trade_entity_fname != null)
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(_trade_entity_fname);
                p = fi.DirectoryName;
                n = fi.Name;
                x = fi.Extension;
            }

            OpenFileDialog fd = new OpenFileDialog();
            
            //the custom places don't seem to work...
            fd.AutoUpgradeEnabled = true;
            fd.CustomPlaces.Clear();
            fd.CustomPlaces.Add(dd);
            if (p != dd) { fd.CustomPlaces.Add(p); }
            /////////
            
            fd.RestoreDirectory = true;
            fd.CheckFileExists = true;
            fd.Multiselect = false;
            fd.InitialDirectory = p;
            fd.FileName = n;
            fd.DefaultExt = x;
            fd.Filter = "Trade Entity Data (*" + x + ")|*" + x + "|All Files (*.*)|*.*";
            fd.FilterIndex = 1;

            DialogResult fdres=fd.ShowDialog();
            if (fdres == DialogResult.Cancel)
            {
                statusLabel_Message.Text = "Trade Entities file not selected.";
                return new FunctionResult();
            }

            if (fdres != DialogResult.OK)
            {
                return FunctionResult.newError("Trade Entities file select error.");
            }

            _trade_entity_fname = fd.FileName;
            toolStripTextBox_EntityFileName.Text = _trade_entity_fname;
            statusLabel_Message.Text = "Trade Entities file selected.";
            return new FunctionResult();
        }

        private void toolStripTextBox_EntityFileName_TextChanged(object sender, EventArgs e)
        {
            _trade_entity_fname = toolStripTextBox_EntityFileName.Text;
        }

        private void toolStripButton_SelectEntities_Click(object sender, EventArgs e)
        {
            FunctionResult fr = selectTradeEntitiesFile();
            if (fr.Error)
            { RespondToEntityError(fr); }
        }

        private void toolStripButton_EditEntities_Click(object sender, EventArgs e)
        {
            FunctionResult fr = editTradeEntitiesFile();
            if (fr.Error)
            { RespondToEntityError(fr); }
        }

        private void openTradeEntitiesFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FunctionResult fr = selectTradeEntitiesFile();
            if (fr.Error)
            { RespondToEntityError(fr); }
        }    
    }

    public class RefreshThread
    {
        public RefreshThread(AccountValuesMonitorForm p) { _parent = p; }
        public RefreshThread(AccountValuesMonitorForm p, AccountValuesStore avs) { _parent = p; _avs = avs; }

        private AccountValuesMonitorForm _parent = null;
        private AccountValuesStore _avs = null;
        private Thread _refresh_thread = null;
        public bool IsRunning { get { return (_refresh_thread != null); } }

        public FunctionResult Start(AccountValuesStore avs)
        {
            _avs = avs;
            return Start();
        }

        public FunctionResult Start()
        {
            FunctionResult r;
            if (_refresh_thread != null)
            {
                r = Stop();
                if (r.Error) { return r; }
            }

            //create and startup the thread on avs...
            _refresh_thread = new Thread(new ThreadStart(threadMain));
            _refresh_thread.Name = "RefreshThread";
            _refresh_thread.IsBackground = true;
            _refresh_thread.Start();

            return new FunctionResult();
        }
        
        private void threadMain()
        {
            bool need_invoke;
            FunctionResult r;
            try
            {
                do
                {
                    Thread.Sleep(750);//sleep for some time...
                    need_invoke = false;

                    lock (_avs)
                    {
                        DateTime last_ts = _avs.LastFileModification;
                        r = _avs.refresh<AccountValuesStore>();
                        if (!r.Error && _avs.LastFileModification != last_ts)
                        { need_invoke = true; }
                    }//release lock before invokes...
                    
                    if (r.Error)
                    {//respond to error
                        _parent.Invoke(new AccountValuesMonitorForm.RespondToErrorDelegate(_parent.RespondToError), r);
                    }
                    else if (need_invoke)
                    {//marshall gui to update it's controls...
                        FunctionResult ires = (FunctionResult)_parent.Invoke(new AccountValuesMonitorForm.RepopDelegate(_parent.RepopulateDataGrid),false);
                        if (ires.Error)
                        {
                            _parent.Invoke(new AccountValuesMonitorForm.RespondToErrorDelegate(_parent.RespondToError), ires);
                        }
                    }
                } while (true);
            }
            catch (ThreadAbortException)
            { }
            catch (Exception e)
            {
                _parent.Invoke(new AccountValuesMonitorForm.RespondToErrorDelegate(_parent.RespondToError), FunctionResult.newError(e.Message));
            }
        }

        public FunctionResult Stop()
        {
            if (_refresh_thread == null) { if (_avs != null) { _avs = null; } return new FunctionResult(); }

            //shutdown the thread...
            if (_refresh_thread.IsAlive) { _refresh_thread.Abort(); }
            
            if (!_refresh_thread.Join(2000))
            {
                _refresh_thread = null;
                _avs = null;
                return FunctionResult.newError("Unable to join refresh thread on shutdown.");
            }
            _refresh_thread = null;
            _avs = null;
            return new FunctionResult();
        }
    }
}
