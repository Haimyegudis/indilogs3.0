using IndiLogs_3._0.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Linq;

namespace IndiLogs_3._0.ViewModels
{
    public class FilterEditorViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<FilterNode> RootNodes { get; set; }

        public ICommand AddGroupCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveNodeCommand { get; }

        public FilterEditorViewModel()
        {
            RootNodes = new ObservableCollection<FilterNode>();
            RootNodes.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });

            AddGroupCommand = new RelayCommand(AddGroup);
            AddConditionCommand = new RelayCommand(AddCondition);
            RemoveNodeCommand = new RelayCommand(RemoveNode);
        }

        private void AddGroup(object param)
        {
            if (param is FilterNode parentNode)
                parentNode.Children.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });
        }

        private void AddCondition(object param)
        {
            if (param is FilterNode parentNode)
                parentNode.Children.Add(new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains" });
        }

        private void RemoveNode(object param)
        {
            if (param is FilterNode nodeToRemove)
                RemoveNodeRecursive(RootNodes, nodeToRemove);
        }

        private bool RemoveNodeRecursive(ObservableCollection<FilterNode> nodes, FilterNode target)
        {
            if (nodes.Contains(target))
            {
                nodes.Remove(target);
                return true;
            }

            foreach (var node in nodes)
            {
                if (node.Type == NodeType.Group)
                {
                    if (RemoveNodeRecursive(node.Children, target)) return true;
                }
            }
            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}