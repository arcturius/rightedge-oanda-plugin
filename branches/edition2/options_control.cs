using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class OandAPluginOptionsControl : UserControl
    {
        private OAPluginOptions _opts;
        public OAPluginOptions Opts
        {
            get { return (_opts); }
            set
            {
                _opts = value;
                propertyGrid1.SelectedObject = _opts;
                propertyGrid1.CollapseAllGridItems();
            }
        }

        public OandAPluginOptionsControl(OAPluginOptions opts)
        {
            InitializeComponent();
            _opts = opts;
            propertyGrid1.SelectedObject = _opts;
            propertyGrid1.CollapseAllGridItems();
        }
        public OandAPluginOptionsControl()
        {
            InitializeComponent();
            _opts = new OAPluginOptions();
            propertyGrid1.SelectedObject = _opts;
            propertyGrid1.CollapseAllGridItems();
        }
    }
}
