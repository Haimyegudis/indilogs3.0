using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IndiLogs_3._0
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePosition;
        private bool _isDragging;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // חיבור אירוע גלילה מה-ViewModel
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToLog += MapsToLogRow;
            }
        }

        // --- Drag and Drop Logic ---
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is MainViewModel vm)
                {
                    vm.OnFilesDropped(files);
                }
            }
        }

        // --- Screenshot Zoom & Pan Logic ---
        private void OnScreenshotMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is MainViewModel vm)
                {
                    if (e.Delta > 0)
                        vm.ZoomInCommand.Execute(null);
                    else
                        vm.ZoomOutCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            _lastMousePosition = e.GetPosition(scrollViewer);
            _isDragging = true;
            scrollViewer.CaptureMouse();
        }

        private void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            Point currentPosition = e.GetPosition(scrollViewer);
            double deltaX = _lastMousePosition.X - currentPosition.X;
            double deltaY = _lastMousePosition.Y - currentPosition.Y;

            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + deltaX);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);

            _lastMousePosition = currentPosition;
        }

        private void OnImageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            _isDragging = false;
            scrollViewer.ReleaseMouseCapture();
        }

        // --- Auto Scroll Logic (Jump to Log) ---
        private void MapsToLogRow(LogEntry log)
        {
            if (MainLogsGrid == null || log == null || !MainLogsGrid.Items.Contains(log)) return;

            MainLogsGrid.SelectedItem = log;
            MainLogsGrid.ScrollIntoView(log);
            MainLogsGrid.UpdateLayout();

            var row = MainLogsGrid.ItemContainerGenerator.ContainerFromItem(log) as DataGridRow;
            if (row != null)
            {
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        // --- Copy Logic (Ctrl + C) ---
        private void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // בדיקה אם נלחץ Ctrl + C
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true; // מונע העתקה רגילה של תא בודד
                CopySelectedLogsToClipboard();
            }
        }

        private void CopySelectedLogsToClipboard()
        {
            if (MainLogsGrid.SelectedItems.Count == 0) return;

            var sb = new StringBuilder();

            // שליפת השורות שנבחרו (כולל בחירה מרובה עם Shift) ומיון לפי זמן
            var selectedLogs = MainLogsGrid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();

            if (selectedLogs.Count == 0) return;

            // חישוב רוחב עמודות מקסימלי כדי ליישר את הטקסט
            int maxTime = 24; // אורך קבוע של פורמט תאריך
            int maxLevel = Math.Max(5, selectedLogs.Max(l => (l.Level ?? "").Length));
            int maxThread = Math.Max(10, selectedLogs.Max(l => (l.ThreadName ?? "").Length));

            foreach (var log in selectedLogs)
            {
                // יצירת המחרוזת המעוצבת עם ריפוד (Padding)
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff").PadRight(maxTime);
                string level = (log.Level ?? "").PadRight(maxLevel + 2);
                string thread = (log.ThreadName ?? "").PadRight(maxThread + 2);
                string msg = log.Message ?? "";

                // שרשור כל החלקים
                sb.AppendLine($"{time} {level} {thread} {msg}");
            }

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to copy to clipboard: " + ex.Message);
            }
        }

        // --- Search Box Auto Focus ---
        private void SearchTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }
    }
}
            
        
    
