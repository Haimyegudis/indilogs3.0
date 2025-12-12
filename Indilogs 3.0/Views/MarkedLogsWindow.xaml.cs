using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls; // Added for DataGrid
using System.Windows.Input;

namespace IndiLogs_3._0.Views
{
    public partial class MarkedLogsWindow : Window
    {
        public LogEntry SelectedItem { get; set; }

        public MarkedLogsWindow(IEnumerable<LogEntry> logsToShow, string title)
        {
            InitializeComponent();
            this.Title = title;
            MarkedList.ItemsSource = new ObservableCollection<LogEntry>(logsToShow);
        }

        private void MarkedList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopySelected();
                e.Handled = true;
            }
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
            var items = MarkedList.SelectedItems.Cast<LogEntry>().ToList();
            int maxThreadLength = Math.Max(10, items.Max(i => (i.ThreadName ?? "").Length));

            foreach (var log in items)
            {
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string threadPadded = (log.ThreadName ?? "").PadRight(maxThreadLength);
                sb.AppendLine($"{time}    {threadPadded}    {log.Message}");
            }

            try { Clipboard.SetText(sb.ToString()); }
            catch (Exception ex) { MessageBox.Show($"Failed to copy: {ex.Message}"); }
        }
    }
}