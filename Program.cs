using System;
using System.Windows.Forms;

namespace AppTime
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // There was previously no handling here at all, so a startup exception
            // (e.g. thrown while building MainForm) would just silently end the
            // process with no dialog and no console output - nothing to tell the user
            // (or us) what went wrong. Catching it and showing the details is a
            // straightforward fix regardless of what the underlying cause turns out
            // to be.
            Application.ThreadException += (_, e) => ShowStartupError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowStartupError(e.ExceptionObject as Exception);

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowStartupError(ex);
            }
        }

        private static void ShowStartupError(Exception? ex)
        {
            MessageBox.Show(
                $"AppTime hit an unexpected error and needs to close.\n\n{ex}",
                "AppTime - Unexpected Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}