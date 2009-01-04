using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class TradeEntitiesControl : UserControl
    {
        private TradeEntities _entities = null;
        public TradeEntities Entities { set { _entities = value; propertyGrid1.SelectedObject = _entities; } get { return _entities; } }
        
        public TradeEntitiesControl()
        {
            InitializeComponent();
        }
    }
}
