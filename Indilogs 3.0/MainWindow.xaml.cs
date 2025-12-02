using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePosition;
        private bool _isDragging;

        public MainWindow()
        {
            InitializeComponent();

            // אירוע טעינה ראשוני
            this.Loaded += MainWindow_Loaded;

            // בדיקת ארגומנטים (פתיחה דרך "פתח באמצעות")
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                var files = new string[args.Length - 1];
                Array.Copy(args, 1, files, 0, files.Length);
                this.Loaded += (s, e) => HandleExternalArguments(files);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // חיבור לאירוע גלילה מה-ViewModel
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToLog += MapsToLogRow;
            }
        }

        public void HandleExternalArguments(string[] args)
        {
            if (args != null && args.Length > 0 && DataContext is MainViewModel vm)
            {
                vm.OnFilesDropped(args);
            }
        }

        // --- Drag & Drop ---
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

        // --- Auto Scroll (Jump to Log) ---
        private void MapsToLogRow(LogEntry log)
        {
            if (MainLogsGrid == null || log == null || !MainLogsGrid.Items.Contains(log)) return;

            try
            {
                MainLogsGrid.SelectedItem = log;
                MainLogsGrid.ScrollIntoView(log);
                MainLogsGrid.UpdateLayout();

                var row = MainLogsGrid.ItemContainerGenerator.ContainerFromItem(log) as DataGridRow;
                if (row != null) row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            catch { }
        }

        // --- Copy to Clipboard (Ctrl+C) ---
        private void MainLogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                CopySelectedLogsToClipboard();
            }
        }

        private void CopySelectedLogsToClipboard()
        {
            if (MainLogsGrid.SelectedItems.Count == 0) return;

            var sb = new StringBuilder();
            var selectedLogs = MainLogsGrid.SelectedItems.Cast<LogEntry>().OrderBy(l => l.Date).ToList();

            int maxTime = 24;
            int maxLevel = Math.Max(5, selectedLogs.Max(l => (l.Level ?? "").Length));
            int maxThread = Math.Max(10, selectedLogs.Max(l => (l.ThreadName ?? "").Length));

            foreach (var log in selectedLogs)
            {
                string time = log.Date.ToString("yyyy-MM-dd HH:mm:ss.fff").PadRight(maxTime);
                string level = (log.Level ?? "").PadRight(maxLevel + 2);
                string thread = (log.ThreadName ?? "").PadRight(maxThread + 2);
                string msg = log.Message ?? "";
                sb.AppendLine($"{time} {level} {thread} {msg}");
            }

            try { Clipboard.SetText(sb.ToString()); }
            catch (Exception ex) { MessageBox.Show("Copy failed: " + ex.Message); }
        }

        // --- Search Box Focus ---
        private void SearchTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }

        // --- Tree View Right Click (Select Item) ---
        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = VisualUpwardSearch(e.OriginalSource as DependencyObject);
            if (treeViewItem != null)
            {
                treeViewItem.Focus();
                e.Handled = true;
            }
        }

        static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        // --- APP Logs Sorting ---
        private void AppLogsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // מונע את המיון האיטי של WPF

            if (DataContext is MainViewModel vm)
            {
                ListSortDirection direction = (e.Column.SortDirection != ListSortDirection.Ascending) ? ListSortDirection.Ascending : ListSortDirection.Descending;
                e.Column.SortDirection = direction;

                string sortBy = e.Column.SortMemberPath;
                vm.SortAppLogs(sortBy, direction == ListSortDirection.Ascending);
            }
        }

        // --- Screenshots Zoom/Pan ---
        private ScrollViewer GetScreenshotScrollViewer() => this.FindName("ScreenshotScrollViewer") as ScrollViewer;

        private void OnScreenshotMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && DataContext is MainViewModel vm)
            {
                if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
                else vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer ?? GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            _lastMousePosition = e.GetPosition(scrollViewer);
            _isDragging = true;
            scrollViewer.CaptureMouse();
            if (sender is FrameworkElement el) el.Cursor = Cursors.Hand;
        }

        private void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var scrollViewer = sender as ScrollViewer ?? GetScreenshotScrollViewer();
            if (scrollViewer == null) return;

            Point current = e.GetPosition(scrollViewer);
            double dX = _lastMousePosition.X - current.X;
            double dY = _lastMousePosition.Y - current.Y;

            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + dX);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + dY);
            _lastMousePosition = current;
        }

        private void OnImageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer ?? GetScreenshotScrollViewer();
            if (scrollViewer == null) return;
            _isDragging = false;
            scrollViewer.ReleaseMouseCapture();
            if (sender is FrameworkElement el) el.Cursor = Cursors.Arrow;
        }
    }
}