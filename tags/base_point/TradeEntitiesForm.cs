using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class TradeEntitiesForm : Form, ICustomComparableEditorForm
    {
        private TradeEntities _entities = null;
        public TradeEntities Entities { get { return _entities; } }
        public ICustomComparable Value
        {
            get { return _entities; }
            set
            {
                if (value.GetType() != typeof(TradeEntities))
                {
                    throw new ArgumentException();
                }
                SetEntities((TradeEntities)value);
            }
        }
        public TradeEntitiesForm(TradeEntities src)
        {
            InitializeComponent();
            SetEntities(src);
        }
        public TradeEntitiesForm()
        {
            InitializeComponent();
        }
        public void SetEntities(TradeEntities src)
        {
            _entities = src;
            tradeEntitiesControl1.Entities = _entities;
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
