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
    // הוסף את המאפיין הזה למחלקה MachineStateSegment בראש הקובץ GraphService.cs
    public class MachineStateSegment
    {
        public string Name { get; set; }
        public double Start { get; set; } // Double לגרף
        public double End { get; set; }   // Double לגרף
        public OxyColor Color { get; set; }

        // --- תוספת: זמן אמיתי לתצוגה ברשימה ---
        public DateTime StartTimeValue => OxyPlot.Axes.DateTimeAxis.ToDateTime(Start);
    }

    public class GraphService
    {
        private readonly Regex _ioRegex = new Regex(@"IO_Mon\s*:\s*([^,]+),\s*([^=]+)=([-+]?\d*\.?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly HashSet<string> _axisParams = new HashSet<string> { "SetP", "ActP", "SetV", "ActV", "Trq", "LagErr" };

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
                var hierarchyHelper = new Dictionary<string, GraphNode>();
                var stateSegments = new List<MachineStateSegment>();

                void AddToTree(string category, string component, string paramName, string fullKey)
                {
                    if (!hierarchyHelper.TryGetValue(category, out var catNode))
                    {
                        catNode = new GraphNode { Name = category, IsLeaf = false };
                        hierarchyHelper[category] = catNode;
                    }
                    var compNode = catNode.Children.FirstOrDefault(c => c.Name == component);
                    if (compNode == null)
                    {
                        compNode = new GraphNode { Name = component, IsLeaf = false };
                        catNode.Children.Add(compNode);
                    }
                    if (!compNode.Children.Any(c => c.Name == paramName))
                    {
                        compNode.Children.Add(new GraphNode { Name = paramName, FullPath = fullKey, IsLeaf = true });
                    }
                }

                var sortedLogs = logs.Where(l => !string.IsNullOrEmpty(l.Message)).OrderBy(l => l.Date).ToList();
                if (sortedLogs.Count == 0) return (dataStore, rootNodes, stateSegments);

                LogEntry lastStateLog = null;
                string currentStateName = "UNDEFINED";

                foreach (var log in sortedLogs)
                {
                    string msg = log.Message;
                    double timeVal = DateTimeAxis.ToDouble(log.Date);

                    // --- תיקון: שימוש ב-IndexOf במקום Contains עבור .NET 4.8 ---

                    // 1. IO_Mon
                    if (msg.IndexOf("IO_Mon:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var match = _ioRegex.Match(msg);
                        if (match.Success)
                        {
                            if (double.TryParse(match.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                            {
                                string key = $"IO_Mon.{match.Groups[1].Value.Trim()}.{match.Groups[2].Value.Trim()}";
                                AddPoint(dataStore, key, timeVal, val);
                                AddToTree("IO_Mon", match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim(), key);
                            }
                        }
                    }
                    // 2. AxisMon
                    else if (msg.IndexOf("AxisMon:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var parts = msg.Split(',');
                        if (parts.Length > 2)
                        {
                            string prefixPart = parts[0];
                            string subsys = prefixPart.IndexOf(':') > 0 ? prefixPart.Split(':')[1].Trim() : prefixPart.Trim();
                            string motor = parts[1].Trim();
                            for (int i = 2; i < parts.Length; i++)
                            {
                                var pair = parts[i].Split('=');
                                if (pair.Length == 2 && _axisParams.Contains(pair[0].Trim()))
                                {
                                    if (double.TryParse(pair[1].Trim().Split(' ')[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                                    {
                                        string key = $"AxisMon.{motor}.{pair[0].Trim()}";
                                        AddPoint(dataStore, key, timeVal, val);
                                        AddToTree("AxisMon", motor, pair[0].Trim(), key);
                                    }
                                }
                            }
                        }
                    }
                    // 3. State Parsing
                    else if (log.ThreadName == "Manager" && msg.IndexOf("->") >= 0 && msg.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase))
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

                foreach (var kvp in hierarchyHelper.OrderBy(k => k.Key)) rootNodes.Add(kvp.Value);

                return (dataStore, rootNodes, stateSegments);
            });
        }

        private void AddStateSegment(List<MachineStateSegment> list, string name, DateTime start, DateTime end)
        {
            var color = _stateColors.ContainsKey(name) ? _stateColors[name] : OxyColors.LightGray;
            color = OxyColor.FromAColor(60, color);
            list.Add(new MachineStateSegment { Name = name, Start = DateTimeAxis.ToDouble(start), End = DateTimeAxis.ToDouble(end), Color = color });
        }

        private void AddPoint(Dictionary<string, List<DataPoint>> store, string key, double x, double y)
        {
            if (!store.ContainsKey(key)) store[key] = new List<DataPoint>();
            store[key].Add(new DataPoint(x, y));
        }
    }
}