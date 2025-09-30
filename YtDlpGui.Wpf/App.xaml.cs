using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace YtDlpGui.Wpf
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
            e.Handled = true; // чтобы не падало насмерть
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { MessageBox.Show((e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject.ToString(), "Unhandled exception"); }
            catch { }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try { MessageBox.Show(e.Exception.ToString(), "Unobserved task exception"); } catch { }
            e.SetObserved();
        }
    }
}
