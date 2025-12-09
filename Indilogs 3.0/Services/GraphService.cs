using IndiLogs_3._0.Models;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    // מחלקת עזר לייצוג מקטע של סטייט בגרף
    public class MachineStateSegment
    {
        public string Name { get; set; }
        public double Start { get; set; } // Double עבור OxyPlot
        public double End { get; set; }   // Double עבור OxyPlot
        public OxyColor Color { get; set; }

        // המרה לזמן אמיתי עבור תצוגה ברשימה
        public DateTime StartTimeValue => DateTimeAxis.ToDateTime(Start);
    }

    public class GraphService
    {
        // Regex גנרי למציאת ערכים מספריים (מותאם לפורמט IO_Mon וגם לפורמטים כלליים Key:Value)
        private readonly Regex _generalSignalRegex = new Regex(@"([a-zA-Z0-9_.]+)\s*[:=]\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.Compiled);

        // מילון צבעים לסטייטים נפוצים
        private readonly Dictionary<string, OxyColor> _stateColors = new Dictionary<string, OxyColor>(StringComparer.OrdinalIgnoreCase)
        {
            { "OFF", OxyColors.Gray },
            { "STANDBY", OxyColors.Orange },
            { "GET_READY", OxyColors.Yellow },
            { "DYNAMIC_READY", OxyColors.LightGreen },
            { "PRINTING", OxyColors.Green },
            { "Diagnostic", OxyColors.Purple },
            { "Error", OxyColors.Red }
        };

        public async Task<Tuple<Dictionary<string, List<DataPoint>>, ObservableCollection<GraphNode>, List<MachineStateSegment>>> ParseLogsToGraphDataAsync(IEnumerable<LogEntry> logs)
        {
            return await Task.Run(() =>
            {
                var dataStore = new Dictionary<string, List<DataPoint>>();
                var stateSegments = new List<MachineStateSegment>();
                var rootNodes = new ObservableCollection<GraphNode>();

                if (logs == null || !logs.Any())
                    return Tuple.Create(dataStore, rootNodes, stateSegments);

                // מיון לפי זמן
                var sortedLogs = logs.OrderBy(l => l.Date).ToList();

                // משתנים למעקב אחרי סטייטים
                LogEntry lastStateLog = null;
                string currentStateName = null;

                foreach (var log in sortedLogs)
                {
                    double time = DateTimeAxis.ToDouble(log.Date);

                    // --- 1. זיהוי ופירסור אותות (Signals) ---
                    var matches = _generalSignalRegex.Matches(log.Message);
                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count == 3)
                        {
                            string key = match.Groups[1].Value.Trim();
                            string valueStr = match.Groups[2].Value;

                            // ניקוי רעשים מהמפתח אם צריך
                            if (key.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                                key = key.Replace("IO_Mon:", "").Trim();

                            if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                            {
                                AddPoint(dataStore, key, time, value);
                            }
                        }
                    }

                    // --- 2. זיהוי ופירסור סטייטים (States) ---
                    // בדיקה אם זה לוג של Manager שמכיל מעבר "->"
                    bool isManager = (log.Logger != null && log.Logger.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                     (log.ThreadName != null && log.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase));

                    if (isManager && log.Message.Contains("->"))
                    {
                        var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                        string fromStateRaw = parts[0].Replace("PlcMngr:", "").Trim();
                        string toStateRaw = parts.Length > 1 ? parts[1].Trim() : "";

                        // לוגיקה לטיפול בסטייטים חתוכים או מלאים
                        if (!string.IsNullOrEmpty(fromStateRaw) && !string.IsNullOrEmpty(toStateRaw))
                        {
                            // מעבר רגיל: סוגרים את הסטייט הקודם ומתחילים חדש
                            if (lastStateLog != null)
                            {
                                AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, log.Date);
                            }
                            else if (currentStateName == null)
                            {
                                // מקרה קצה: לוג ראשון הוא מעבר, נניח שההתחלה הייתה ה-FROM
                                // אופציונלי: להוסיף סגמנט התחלתי קצר
                            }

                            currentStateName = toStateRaw;
                            lastStateLog = log;
                        }
                        else if (string.IsNullOrEmpty(fromStateRaw) && !string.IsNullOrEmpty(toStateRaw))
                        {
                            // מקרה: "-> StateB" (התחלה חתוכה)
                            // נסגור את מה שהיה קודם (אם היה)
                            if (lastStateLog != null)
                            {
                                AddStateSegment(stateSegments, currentStateName ?? "Unknown", lastStateLog.Date, log.Date);
                            }

                            currentStateName = toStateRaw;
                            lastStateLog = log;
                        }
                        else if (!string.IsNullOrEmpty(fromStateRaw) && string.IsNullOrEmpty(toStateRaw))
                        {
                            // מקרה: "StateA ->" (סוף חתוך)
                            if (lastStateLog != null)
                            {
                                AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, log.Date);
                            }
                            // הסטייט הבא לא ידוע כרגע
                            currentStateName = "Transitioning...";
                            lastStateLog = log;
                        }
                    }
                }

                // סגירת הסטייט האחרון עד סוף הלוג
                if (lastStateLog != null && sortedLogs.Count > 0)
                {
                    AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, sortedLogs.Last().Date);
                }

                // --- 3. בניית עץ הסיגנלים (UI Tree) ---
                var hierarchyHelper = new Dictionary<string, GraphNode>();
                foreach (var signalKey in dataStore.Keys)
                {
                    BuildComponentTree(rootNodes, hierarchyHelper, signalKey);
                }

                return Tuple.Create(dataStore, rootNodes, stateSegments);
            });
        }

        private void AddStateSegment(List<MachineStateSegment> list, string name, DateTime start, DateTime end)
        {
            if (string.IsNullOrEmpty(name)) return;

            // בחירת צבע
            OxyColor color = OxyColors.LightGray; // Default

            // בדיקה במילון (Case Insensitive)
            var key = _stateColors.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (key != null) color = _stateColors[key];
            else if (name.Contains("Error") || name.Contains("Failure")) color = OxyColors.Red;

            // שקיפות כדי לא להסתיר את הגרף
            color = OxyColor.FromAColor(60, color);

            list.Add(new MachineStateSegment
            {
                Name = name,
                Start = DateTimeAxis.ToDouble(start),
                End = DateTimeAxis.ToDouble(end),
                Color = color
            });
        }

        private void AddPoint(Dictionary<string, List<DataPoint>> store, string key, double x, double y)
        {
            if (!store.ContainsKey(key)) store[key] = new List<DataPoint>();

            // אופטימיזציה קטנה: לא להוסיף נקודה אם היא זהה לקודמת בדיוק (חוסך זיכרון)
            // אלא אם כן עבר הרבה זמן. כאן נשמור הכל ליתר ביטחון.
            store[key].Add(new DataPoint(x, y));
        }

        private void BuildComponentTree(ObservableCollection<GraphNode> rootNodes, Dictionary<string, GraphNode> hierarchyHelper, string fullPath)
        {
            var parts = fullPath.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            ObservableCollection<GraphNode> currentCollection = rootNodes;
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                currentPath = i == 0 ? part : currentPath + "." + part;
                bool isLeaf = (i == parts.Length - 1);

                // חיפוש האם הצומת קיים ברמה הנוכחית
                var node = currentCollection.FirstOrDefault(n => n.Name == part);
                if (node == null)
                {
                    node = new GraphNode
                    {
                        Name = part,
                        FullPath = isLeaf ? fullPath : currentPath, // אם זה עלה, נשתמש במפתח המקורי
                        IsLeaf = isLeaf,
                        IsExpanded = false
                    };
                    currentCollection.Add(node);

                    // שמירה במילון עזר אם צריך גישה ישירה (לא חובה ללוגיקה הבסיסית אבל עוזר)
                    if (!hierarchyHelper.ContainsKey(currentPath)) hierarchyHelper[currentPath] = node;
                }

                currentCollection = node.Children;
            }
        }
    }
}