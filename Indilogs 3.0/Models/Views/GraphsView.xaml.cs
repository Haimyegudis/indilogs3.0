using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndiLogs_3._0.Views
{
    public partial class GraphsView : UserControl
    {
        public GraphsView()
        {
            InitializeComponent();
        }

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item && item.DataContext is GraphNode node)
            {
                if (node.IsLeaf && DataContext is GraphsViewModel vm)
                {
                    // בדיקה אם CTRL לחוץ - אם כן, הסרה. אחרת, הוספה.
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        vm.RemoveSignalFromChart(node.FullPath);
                    }
                    else
                    {
                        vm.AddSignalToChart(node.FullPath);
                    }
                    e.Handled = true;
                }
            }
        }
    }
}