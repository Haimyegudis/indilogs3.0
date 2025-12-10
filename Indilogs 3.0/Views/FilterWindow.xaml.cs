using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class FilterWindow : Window
    {
        public FilterEditorViewModel ViewModel { get; private set; }

        public FilterWindow()
        {
            InitializeComponent();
            ViewModel = new FilterEditorViewModel();
            DataContext = ViewModel;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RootNodes.Clear();
            ViewModel.RootNodes.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}