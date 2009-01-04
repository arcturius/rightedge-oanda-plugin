using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class TradeEntityForm : Form, ICustomComparableEditorForm
    {
        private TradeEntity _entity = null;
        public TradeEntity Entity { get { return _entity; } }

        public ICustomComparable Value
        {
            get { return _entity; }
            set
            {
                if (value.GetType() != typeof(TradeEntity))
                {
                    throw new ArgumentException();
                }
                SetEntity((TradeEntity)value);
            }
        }

        public TradeEntityForm(TradeEntity src)
        {
            InitializeComponent();
            SetEntity(src);
        }
        public TradeEntityForm()
        {
            InitializeComponent();
        }
        public void SetEntity(TradeEntity src)
        {
            _entity = src;
            tradeEntityControl1.Entity = _entity;
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
