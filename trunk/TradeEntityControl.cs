using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class TradeEntityControl : UserControl
    {
        private TradeEntity _entity = null;
        public TradeEntity Entity { set { _entity = value; propertyGrid1.SelectedObject = _entity; } get { return _entity; } }

        public TradeEntityControl()
        {
            InitializeComponent();
        }
    }
}
