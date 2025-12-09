using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    public class GraphNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsLeaf { get; set; }
        public ObservableCollection<GraphNode> Children { get; set; } = new ObservableCollection<GraphNode>();

        // --- שדות חדשים לחיפוש ---
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}