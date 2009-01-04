using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class CustomComparableDictionaryControl<T> : UserControl
        where T : ICustomComparable, INamedObject, new()
    {
        private IDictionary<string, T> _d = null;
        public IDictionary<string, T> Values { set { _d = value; ResetControls(); } get { return _d; } }
        
        public CustomComparableDictionaryControl()
        {
            InitializeComponent();
        }

        public void ResetControls()
        {
            listView1.BeginUpdate();
            listView1.Clear();
            listView1.MultiSelect = false;
            listView1.LabelWrap = false;
            listView1.View = View.List;

            propertyGrid1.SuspendLayout();
            propertyGrid1.SelectedObject = null;
            propertyGrid1.ResumeLayout();

            if(_d==null)
            {
                listView1.ResumeLayout();
                return;
            }

            foreach (string name in _d.Keys)
            {
                listView1.Items.Add(name);
            }

            listView1.EndUpdate(); 
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            propertyGrid1.SuspendLayout();
            if (listView1.SelectedItems.Count == 0 || _d==null)
            { propertyGrid1.SelectedObject = null; propertyGrid1.ResumeLayout(); return; }

            if (listView1.SelectedItems.Count != 1)
            { propertyGrid1.ResumeLayout(); throw new ArgumentException(); }

            ListViewItem lvi = listView1.SelectedItems[0];

            propertyGrid1.SelectedObject = _d[lvi.Text];
            propertyGrid1.ResumeLayout();
        }

        private void ButtonAdd_Click(object sender, EventArgs e)
        {
            if (_d == null) { return; }

            ////////////////////////////////////////////////////////////
            //FIX ME - this is not generic, it needs to be refactored to use the type argument T or maybe a new type F
            TradeEntityIDForm form = new TradeEntityIDForm();
            form.EntityID = new TradeEntityID();
            ////////////////////////////////////////////////////////////

            DialogResult dres = form.ShowDialog();
            if (dres == DialogResult.Cancel) { return; }

            ////////////////////////////////////////////////////////////
            //FIX ME - the result validation procedure is not generic,
            //it needs to be pushed down into the TradeEntityID class and extracted it into an interface
            TradeEntityID eid = form.EntityID;

            if (string.IsNullOrEmpty(eid.EntityName))
            {
                MessageBox.Show("Entity ID must contain an Entity Name", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.IsNullOrEmpty(eid.SymbolName))
            {
                MessageBox.Show("Entity ID must contain a Symbol Name", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_d.ContainsKey(eid.ID))
            {
                MessageBox.Show("Duplicate Entity ID", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ////////////////////////////////////////////////////////////
            

            //ok to add it, select it and refresh
            T te = new T();
            te.SetName(eid.Name);
            _d.Add(te.Name, te);

            listView1.BeginUpdate();
            ListViewItem lvi = listView1.Items.Add(te.Name);
            lvi.Selected = true;
            listView1.EndUpdate();
        }

        private void ButtonRemove_Click(object sender, EventArgs e)
        {
            propertyGrid1.SuspendLayout();
            if (listView1.SelectedItems.Count == 0 || _d == null)
            { propertyGrid1.SelectedObject = null; propertyGrid1.ResumeLayout(); return; }

            if (listView1.SelectedItems.Count != 1)
            {
                propertyGrid1.ResumeLayout();
                throw new ArgumentException();
            }

            ListViewItem lvi = listView1.SelectedItems[0];

            if (!_d.Remove(lvi.Text))
            {
                propertyGrid1.ResumeLayout();
                throw new ArgumentException();
            }

            listView1.BeginUpdate();
            listView1.Items.Remove(lvi);
            listView1.EndUpdate();

            propertyGrid1.SelectedObject = null;
            propertyGrid1.ResumeLayout();
        }
    }
}
