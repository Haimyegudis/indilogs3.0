using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class FilterOutWindow : Window
    {
        public string TextToRemove { get; private set; }

        public FilterOutWindow(string initialText)
        {
            InitializeComponent();
            TextToFilterTextBox.Text = initialText ?? "";
            TextToFilterTextBox.SelectAll();
            TextToFilterTextBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            TextToRemove = TextToFilterTextBox.Text;

            if (string.IsNullOrWhiteSpace(TextToRemove))
            {
                MessageBox.Show("Please enter text to filter out", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // הסרנו את ה-MessageBox.Show שמבקש אישור
            // הפעולה כעת מידית
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