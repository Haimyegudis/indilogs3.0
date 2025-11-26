using IndiLogs_3._0.Models;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class LogDetailsWindow : Window
    {
        public LogDetailsWindow(LogEntry log)
        {
            InitializeComponent();

            if (log != null)
            {
                TimeText.Text = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff");
                LevelText.Text = log.Level;
                ThreadText.Text = log.ThreadName;
                LoggerText.Text = log.Logger;
                MessageText.Text = log.Message;
            }

            // לחיצה על ESC תסגור את החלון
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}