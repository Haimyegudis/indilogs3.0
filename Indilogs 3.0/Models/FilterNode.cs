using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models
{
    public enum NodeType { Group, Condition }

    public class FilterNode : INotifyPropertyChanged
    {
        private NodeType _type;
        private string _logicalOperator = "AND";
        private string _field = "Message";
        private string _operator = "Contains";
        private string _value = "";

        public NodeType Type { get => _type; set { _type = value; OnPropertyChanged(); } }
        public string LogicalOperator { get => _logicalOperator; set { _logicalOperator = value; OnPropertyChanged(); } }
        public ObservableCollection<FilterNode> Children { get; set; } = new ObservableCollection<FilterNode>();
        public string Field { get => _field; set { _field = value; OnPropertyChanged(); } }
        public string Operator { get => _operator; set { _operator = value; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        public FilterNode DeepClone()
        {
            var clone = new FilterNode
            {
                Type = this.Type,
                LogicalOperator = this.LogicalOperator,
                Field = this.Field,
                Operator = this.Operator,
                Value = this.Value,
                Children = new ObservableCollection<FilterNode>()
            };
            foreach (var child in this.Children) clone.Children.Add(child.DeepClone());
            return clone;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}