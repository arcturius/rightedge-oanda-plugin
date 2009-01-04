using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class TradeEntityIDForm : Form
    {
        private TradeEntityID _entity_id = null;
        public TradeEntityID EntityID { get { return _entity_id; } set { _entity_id = value; propertyGrid1.SelectedObject = _entity_id; } }

        public TradeEntityIDForm()
        {
            InitializeComponent();
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
    }
}
