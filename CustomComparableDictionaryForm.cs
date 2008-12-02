using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RightEdgeOandaPlugin
{
    public partial class CustomComparableDictionaryForm<T> : Form, ICustomComparableDictionaryForm<T>
        where T : ICustomComparable, INamedObject, new()
    {
        private IDictionary<string, T> _d;
        public IDictionary<string, T> Values { set { _d = value; customComparableDictionaryControl1.Values = value; } get { return _d; } }

        public CustomComparableDictionaryForm()
        {
            InitializeComponent();
        }

        private void ButtonOK_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            Close();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
