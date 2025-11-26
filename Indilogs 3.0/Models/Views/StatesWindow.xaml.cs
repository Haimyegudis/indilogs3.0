using System.Collections.Generic;
using System.Windows;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Views
{
    public partial class StatesWindow : Window
    {
        public StatesWindow(List<StateEntry> states, object dataContext)
        {
            InitializeComponent();
            // אנו מקבלים את ה-ViewModel כ-DataContext כדי שה-Command של הדאבל קליק יעבוד
            this.DataContext = dataContext;
            StatesGrid.ItemsSource = states;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}