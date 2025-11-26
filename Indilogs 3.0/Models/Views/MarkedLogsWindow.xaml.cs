using IndiLogs_3._0.Models;
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndiLogs_3._0.Views
{
    public partial class MarkedLogsWindow : Window
    {
        public MarkedLogsWindow()
        {
            InitializeComponent();

            this.Closing += (s, e) => {
                e.Cancel = true;
                this.Hide();
            };
        }

        private void MarkedList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + C (Copy)
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopySelected();
                e.Handled = true;
            }
            // Ctrl + A (Select All)
            else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                MarkedList.SelectAll();
                e.Handled = true;
            }
        }

        private void CopySelected()
        {
            if (MarkedList.SelectedItems.Count == 0) return;

            var sb = new StringBuilder();

            // המרה לרשימת LogEntry ומיון לפי תאריך (אם צריך)
            var items = MarkedList.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();

            // מציאת האורך המקסימלי של שם ה-Thread כדי ליישר לפי השרשור הכי ארוך
            int maxThreadLength = Math.Max(10, items.Max(i => (i.ThreadName ?? "").Length));

            foreach (var log in items)
            {
                // עיצוב התאריך
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff");

                // עיצוב ה-Thread:
                // 1. לוקחים את השם (או מחרוזת ריקה אם אין)
                // 2. PadRight מוסיף רווחים עד לאורך המקסימלי כדי שכל ה-Threads ייגמרו באותה נקודה ויזואלית
                string threadPadded = (log.ThreadName ?? "").PadRight(maxThreadLength);

                // הוספנו 4 רווחים קבועים בין העמודות כדי להבטיח קריאות ו"אוויר"
                // מבנה: [תאריך]    [Thread מרופד]    [הודעה]
                sb.AppendLine($"{time}    {threadPadded}    {log.Message}");
            }

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}");
            }
        }
    }
}