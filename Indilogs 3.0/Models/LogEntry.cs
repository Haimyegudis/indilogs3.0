using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    public class LogEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Level { get; set; }
        public DateTime Date { get; set; }
        public string ThreadName { get; set; }
        public string Message { get; set; }
        public string Logger { get; set; }
        public string ProcessName { get; set; }

        private bool _isMarked;
        public bool IsMarked
        {
            get => _isMarked;
            set
            {
                if (_isMarked != value)
                {
                    _isMarked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RowBackground));
                }
            }
        }

        private Color? _customColor;
        public Color? CustomColor
        {
            get => _customColor;
            set
            {
                if (_customColor != value)
                {
                    _customColor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RowBackground));
                }
            }
        }

        public Brush RowBackground
        {
            get
            {
                if (IsMarked)
                    return new SolidColorBrush(Color.FromRgb(204, 153, 255));

                if (CustomColor.HasValue)
                    return new SolidColorBrush(CustomColor.Value);

                return Brushes.Transparent;
            }
        }

        public void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}