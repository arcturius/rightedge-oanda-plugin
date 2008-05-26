using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace OandA_RightEdge_Plugin
{
    public partial class OandAPluginOptionsControl : UserControl
    {
        private OAPluginOptions _opts;
        public OAPluginOptions Opts { get { return (_opts); } set { _opts = value; propertyGrid1.SelectedObject = _opts; } }

        public OandAPluginOptionsControl(OAPluginOptions opts)
        {
            InitializeComponent();
            _opts = opts;
            propertyGrid1.SelectedObject = _opts;
        }
        public OandAPluginOptionsControl()
        {
            InitializeComponent();
            _opts = new OAPluginOptions();
            propertyGrid1.SelectedObject = _opts;
        }
    }
}
