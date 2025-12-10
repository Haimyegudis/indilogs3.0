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

namespace IndiLogs_3._0.Services
{
    public class GraphService
    {
        // Regex קפדני לזיהוי פרמטרים (תומך ברווחים, מינוס, ומספרים מדעיים)
        private readonly Regex _paramRegex = new Regex(@"([a-zA-Z0-9_]+)\s*=\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)", RegexOptions.Compiled);

        private readonly HashSet<string> _axisParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr", "Vel", "Pos", "Acc", "Current"
        };

        private readonly Dictionary<string, OxyColor> _stateColors = new Dictionary<string, OxyColor>(StringComparer.OrdinalIgnoreCase)
        {
            { "INIT", OxyColor.Parse("#FFE135") }, { "POWER_DISABLE", OxyColor.Parse("#FF6B6B") }, { "OFF", OxyColor.Parse("#808080") },
            { "SERVICE", OxyColor.Parse("#8B4513") }, { "MECH_INIT", OxyColor.Parse("#FFA500") }, { "STANDBY", OxyColor.Parse("#FFFF00") },
            { "GET_READY", OxyColor.Parse("#FFA500") }, { "READY", OxyColor.Parse("#90EE90") }, { "PRE_PRINT", OxyColor.Parse("#26C6DA") },
            { "PRINT", OxyColor.Parse("#228B22") }, { "POST_PRINT", OxyColor.Parse("#4169E1") }, { "PAUSE", OxyColor.Parse("#FFA726") },
            { "RECOVERY", OxyColor.Parse("#EC407A") }, { "SML_OFF", OxyColor.Parse("#C62828") }, { "DYNAMIC_READY", OxyColor.Parse("#32CD32") }
        };

        public async Task<(Dictionary<string, List<DataPoint>>, ObservableCollection<GraphNode>, List<MachineStateSegment>)> ParseLogsToGraphDataAsync(IEnumerable<LogEntry> logs)
        {
            return await Task.Run(() =>
            {
                var dataStore = new Dictionary<string, List<DataPoint>>();
                var rootNodes = new ObservableCollection<GraphNode>();
                var stateSegments = new List<MachineStateSegment>();

                // פונקציית עזר לבניית עץ: בודקת אם צומת קיים ומוסיפה אם לא
                // תומכת בעומק בלתי מוגבל
                void AddPathToTree(string[] pathParts, string fullKey)
                {
                    ObservableCollection<GraphNode> currentCollection = rootNodes;

                    for (int i = 0; i < pathParts.Length; i++)
                    {
                        string partName = pathParts[i];
                        bool isLeaf = (i == pathParts.Length - 1);

                        // חיפוש ברמה הנוכחית
                        var node = currentCollection.FirstOrDefault(n => n.Name == partName);

                        if (node == null)
                        {
                            node = new GraphNode
                            {
                                Name = partName,
                                IsLeaf = isLeaf,
                                FullPath = isLeaf ? fullKey : null,
                                IsExpanded = i == 0 // פותח רק את הרמה הראשונה (IO Monitor / Motor Axis)
                            };

                            // הכנסה ממויינת לפי א-ב
                            int insertIndex = 0;
                            while (insertIndex < currentCollection.Count && string.Compare(currentCollection[insertIndex].Name, partName) < 0)
                            {
                                insertIndex++;
                            }
                            currentCollection.Insert(insertIndex, node);
                        }

                        // צלילה לרמה הבאה
                        currentCollection = node.Children;
                    }
                }

                // מיון לפי זמן כדי שהגרף ייבנה נכון
                var sortedLogs = logs.Where(l => !string.IsNullOrEmpty(l.Message)).OrderBy(l => l.Date).ToList();
                if (sortedLogs.Count == 0) return (dataStore, rootNodes, stateSegments);

                LogEntry lastStateLog = null;
                string currentStateName = "UNDEFINED";

                foreach (var log in sortedLogs)
                {
                    string msg = log.Message;
                    string thread = log.ThreadName ?? "Unknown";
                    double timeVal = DateTimeAxis.ToDouble(log.Date);

                    // =================================================================================
                    // 1. Motor Axis Logic
                    // הפורמט: AxisMon: [Component], [SubComponent], [Key=Val], [Key=Val]...
                    // העץ הרצוי: Motor Axis -> Thread -> Component -> SubComponent -> Param
                    // =================================================================================
                    if (msg.StartsWith("AxisMon:", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = msg.Substring(8).Trim(); // דילוג על "AxisMon:"
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        // חייבים לפחות 3 חלקים: Comp, SubComp, Params...
                        if (parts.Length >= 3)
                        {
                            string component = parts[0].Trim();      // חלק 1: Component (לדוגמה BID_Eng_1_Stn_3)
                            string subComponent = parts[1].Trim();   // חלק 2: SubComponent (לדוגמה BID_EngageRear)

                            // מחברים מחדש את שאר החלקים ומריצים Regex
                            // זה מבטיח שלא נתבלבל אם יש רווחים מוזרים
                            string paramsPart = string.Join(",", parts.Skip(2));
                            var matches = _paramRegex.Matches(paramsPart);

                            foreach (Match m in matches)
                            {
                                string key = m.Groups[1].Value;
                                string valStr = m.Groups[2].Value;

                                if (_axisParams.Contains(key))
                                {
                                    if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                    {
                                        string fullKey = $"{thread}.{component}.{subComponent}.{key}";
                                        AddPoint(dataStore, fullKey, timeVal, val);

                                        // בניית העץ
                                        AddPathToTree(new[] { "Motor Axis", thread, component, subComponent, key }, fullKey);
                                    }
                                }
                            }
                        }
                    }
                    // =================================================================================
                    // 2. IO Monitor Logic
                    // הפורמט: IO_Mon: [Component], [SubComponent=Val], [SubComponent=Val]...
                    // העץ הרצוי: IO Monitor -> Thread -> Component -> SubComponent
                    // =================================================================================
                    else if (msg.StartsWith("IO_Mon:", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = msg.Substring(7).Trim(); // דילוג על "IO_Mon:"
                        var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length >= 2)
                        {
                            // חלק 1: שם הקומפוננטה הראשית (למשל CS_Stn_6)
                            string componentName = parts[0].Trim();

                            // רצים על שאר החלקים שהם זוגות (SubComponent=Value)
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string pair = parts[i].Trim();
                                int eqIdx = pair.LastIndexOf('='); // שימוש ב-LastIndexOf למקרה שיש = בשם (נדיר)

                                if (eqIdx > 0)
                                {
                                    string subComponentName = pair.Substring(0, eqIdx).Trim();
                                    string valStr = pair.Substring(eqIdx + 1).Trim();

                                    // ניקוי הערך (למשל הסרת "(IntrSim)")
                                    if (valStr.Contains(" ")) valStr = valStr.Split(' ')[0];

                                    if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                    {
                                        string fullKey = $"IO.{thread}.{componentName}.{subComponentName}";
                                        AddPoint(dataStore, fullKey, timeVal, val);

                                        // בניית העץ
                                        AddPathToTree(new[] { "IO Monitor", thread, componentName, subComponentName }, fullKey);
                                    }
                                }
                            }
                        }
                    }
                    // =================================================================================
                    // 3. Machine States Logic
                    // =================================================================================
                    else if (log.ThreadName == "Manager" && msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) && msg.Contains("->"))
                    {
                        var parts = msg.Split(new[] { "->" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            if (lastStateLog != null) AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, log.Date);
                            currentStateName = parts[1].Trim();
                            lastStateLog = log;
                        }
                    }
                }

                if (lastStateLog != null && sortedLogs.Count > 0)
                    AddStateSegment(stateSegments, currentStateName, lastStateLog.Date, sortedLogs.Last().Date);

                return (dataStore, rootNodes, stateSegments);
            });
        }

        private void AddStateSegment(List<MachineStateSegment> list, string name, DateTime start, DateTime end)
        {
            var color = _stateColors.ContainsKey(name) ? _stateColors[name] : OxyColors.LightGray;
            if ((end - start).TotalMilliseconds > 10)
            {
                list.Add(new MachineStateSegment { Name = name, Start = DateTimeAxis.ToDouble(start), End = DateTimeAxis.ToDouble(end), Color = color });
            }
        }

        private void AddPoint(Dictionary<string, List<DataPoint>> store, string key, double x, double y)
        {
            if (!store.TryGetValue(key, out var list))
            {
                list = new List<DataPoint>();
                store[key] = list;
            }
            list.Add(new DataPoint(x, y));
        }
    }
}