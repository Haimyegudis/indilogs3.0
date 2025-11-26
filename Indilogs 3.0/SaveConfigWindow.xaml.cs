using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SaveConfigWindow : Window
    {
        public string ConfigName { get; private set; }
        private readonly HashSet<string> _existingNames;

        // זה הבנאי ש-MainViewModel מחפש (מקבל רשימת שמות)
        public SaveConfigWindow(IEnumerable<string> existingNames = null)
        {
            InitializeComponent();
            NameTextBox.Focus();

            // יצירת HashSet לחיפוש מהיר (Case Insensitive)
            _existingNames = existingNames != null
                ? new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
        }

        // בנאי ברירת מחדל (למקרה שה-XAML דורש אותו, למרות שבקוד אנחנו משתמשים בשני)
        public SaveConfigWindow() : this(null) { }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a configuration name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // בדיקת כפילות - כאן אנחנו משתמשים ברשימה שקיבלנו
            if (_existingNames.Contains(name))
            {
                MessageBox.Show($"A configuration named '{name}' already exists.\nPlease choose a different name.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // לא סוגרים את החלון כדי לתת למשתמש לתקן
            }

            ConfigName = name;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}