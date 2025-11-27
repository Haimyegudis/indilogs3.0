using IndiLogs_3._0.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class FilterEditorViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<FilterNode> RootNodes { get; set; }

        // פקודות לעריכת העץ
        public ICommand AddGroupCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveNodeCommand { get; }

        // פקודות לשינוי האופרטור הלוגי
        public ICommand SetAndCommand { get; }
        public ICommand SetOrCommand { get; }
        public ICommand SetNotAndCommand { get; }
        public ICommand SetNotOrCommand { get; }

        public FilterEditorViewModel()
        {
            RootNodes = new ObservableCollection<FilterNode>();
            // יצירת קבוצת שורש (Root) כברירת מחדל
            RootNodes.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });

            // --- אתחול הפקודות ---

            // הוספת קבוצה חדשה תחת ה-Node הנוכחי
            AddGroupCommand = new RelayCommand(node => 
            {
                if (node is FilterNode fn) 
                    fn.Children.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });
            });

            // הוספת תנאי חדש תחת ה-Node הנוכחי
            AddConditionCommand = new RelayCommand(node => 
            {
                if (node is FilterNode fn)
                    fn.Children.Add(new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains" });
            });

            // מחיקת Node (רקורסיבית)
            RemoveNodeCommand = new RelayCommand(RemoveNode);

            // --- לוגיקה לשינוי סוג הקבוצה ---
            SetAndCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "AND"; 
            });

            SetOrCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "OR"; 
            });

            SetNotAndCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "NOT AND"; 
            });

            SetNotOrCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "NOT OR"; 
            });
        }

        private void RemoveNode(object param)
        {
            if (param is FilterNode nodeToRemove)
            {
                // אם מנסים למחוק את השורש, רק מנקים את הילדים שלו (כדי שתמיד יישאר שורש)
                if (RootNodes.Contains(nodeToRemove))
                {
                    nodeToRemove.Children.Clear();
                }
                else
                {
                    RemoveNodeRecursive(RootNodes, nodeToRemove);
                }
            }
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