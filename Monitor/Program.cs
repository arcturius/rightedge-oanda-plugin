using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Monitor
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AccountValuesMonitorForm f = new AccountValuesMonitorForm();
            f.Text = "Account Values Monitor (" + (IntPtr.Size * 8).ToString() + "bit mode)";
            Application.Run(f);
        }
    }
}
