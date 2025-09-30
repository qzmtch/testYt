using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using YtDlpGui.Wpf.Dialogs;

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

        private void ShowCrash(Exception ex)
        {
            try
            {
                string text = ex?.ToString() ?? "(null exception)";
                // Пишем лог рядом с exe
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           $"Crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, text, new UTF8Encoding(false));
                // Копируем в буфер
                try { Clipboard.SetText(text); } catch { }

                // Показываем окно
                var dlg = new CrashDialog(text)
                {
                    Owner = Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                };
                dlg.ShowDialog();
            }
            catch
            {
                // Фолбэк: хотя бы MessageBox
                try { MessageBox.Show(ex?.ToString() ?? "", "Unhandled exception"); } catch { }
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ShowCrash(e.Exception);
            e.Handled = true;
            Shutdown(1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { ShowCrash(e.ExceptionObject as Exception); } catch { }
            Shutdown(1);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try { ShowCrash(e.Exception); } catch { }
            e.SetObserved();
            Shutdown(1);
        }
    }
}
