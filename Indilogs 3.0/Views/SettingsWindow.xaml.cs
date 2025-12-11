using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            new HelpWindow().Show();
            Close();
        }

        private void OpenFonts_Click(object sender, RoutedEventArgs e)
        {
            // ודא ש-FontsWindow קיים בפרויקט שלך
            new FontsWindow { DataContext = this.DataContext }.ShowDialog();
        }
    }
}