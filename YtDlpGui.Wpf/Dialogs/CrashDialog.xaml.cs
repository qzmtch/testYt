using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Windows;

namespace YtDlpGui.Wpf.Dialogs
{
    public partial class CrashDialog : Window
    {
        public CrashDialog(string text)
        {
            InitializeComponent();
            ErrorText.Text = text ?? "";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ErrorText.Text ?? ""); } catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                FileName = "Crash.txt",
                Filter = "Текст|*.txt|Все файлы|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            if (sfd.ShowDialog() == true)
            {
                try { File.WriteAllText(sfd.FileName, ErrorText.Text ?? "", new UTF8Encoding(false)); }
                catch { MessageBox.Show("Не удалось сохранить файл.", "Сохранение", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
