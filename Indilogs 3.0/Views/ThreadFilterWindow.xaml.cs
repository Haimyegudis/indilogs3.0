using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IndiLogs_3._0.Views
{
    public partial class ThreadFilterWindow : Window
    {
        // שינוי: רשימה במקום משתנה בודד
        public List<string> SelectedThreads { get; private set; }
        public bool ShouldClear { get; private set; }
        private List<string> _allThreads;

        public ThreadFilterWindow(IEnumerable<string> threads)
        {
            InitializeComponent();
            _allThreads = threads.OrderBy(t => t).ToList();
            ThreadsList.ItemsSource = _allThreads;
            SearchBox.Focus();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(filter))
            {
                ThreadsList.ItemsSource = _allThreads;
            }
            else
            {
                ThreadsList.ItemsSource = _allThreads.Where(t => t.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // שינוי: איסוף כל הפריטים שנבחרו
            if (ThreadsList.SelectedItems.Count > 0)
            {
                SelectedThreads = ThreadsList.SelectedItems.Cast<string>().ToList();
                DialogResult = true;
                Close();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ShouldClear = true;
            DialogResult = true;
            Close();
        }

        private void ThreadsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // דאבל קליק עדיין יבחר את הפריט הנוכחי וייצא
            if (ThreadsList.SelectedItem != null)
            {
                SelectedThreads = new List<string> { ThreadsList.SelectedItem.ToString() };
                DialogResult = true;
                Close();
            }
        }
    }
}