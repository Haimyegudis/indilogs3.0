using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    public class LoggerNode : INotifyPropertyChanged
    {
        public string Name { get; set; }        // שם הצומת (למשל "indigo")
        public string FullPath { get; set; }    // נתיב מלא (למשל "com.indigo")
        public int Count { get; set; }          // כמות לוגים תחת צומת זה
        public ObservableCollection<LoggerNode> Children { get; set; } = new ObservableCollection<LoggerNode>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // --- חדש: לסימון ויזואלי של לוגר מוסתר ---
        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set { _isHidden = value; OnPropertyChanged(); }
        }

        public string DisplayText => $"{Name} ({Count})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}