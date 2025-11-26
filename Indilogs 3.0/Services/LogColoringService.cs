using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions; // הוספנו עבור Regex
using System.Threading.Tasks;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    public class LogColoringService
    {
        public async Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs)
        {
            await Task.Run(() =>
            {
                foreach (var log in logs)
                {
                    if (log.IsMarked) continue;

                    // --- שורה חדשה קריטית: איפוס הצבע לפני בדיקת תנאים ---
                    log.CustomColor = null;
                    // -----------------------------------------------------

                    // 1. Error = אדום כהה
                    if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        log.CustomColor = Color.FromRgb(180, 50, 50);
                    }
                    // 2. PlcMngr: = כחול שמיים
                    else if (log.Message != null &&
                             log.Message.IndexOf("PlcMngr:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log.CustomColor = Color.FromRgb(100, 150, 200);
                    }
                    // 3. MechInit: = ירוק כהה
                    else if (log.Message != null &&
                             log.Message.IndexOf("MechInit:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log.CustomColor = Color.FromRgb(80, 150, 80);
                    }
                    // 4. STIRCtrl (ThreadName) = כתום כהה
                    else if (log.ThreadName != null &&
                             log.ThreadName.IndexOf("STIRCtrl", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log.CustomColor = Color.FromRgb(200, 120, 50);
                    }
                    // 5. Logger = Manager = צהוב זהב
                    else if (log.Logger != null &&
                             log.Logger.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log.CustomColor = Color.FromRgb(180, 150, 50);
                    }
                }
            });
        }

        public async Task ApplyCustomColoringAsync(
            IEnumerable<LogEntry> logs,
            List<ColoringCondition> conditions)
        {
            await Task.Run(() =>
            {
                if (conditions == null || conditions.Count == 0) return;

                foreach (var log in logs)
                {
                    if (log.IsMarked) continue;

                    foreach (var condition in conditions)
                    {
                        if (EvaluateCondition(log, condition))
                        {
                            log.CustomColor = condition.Color;
                            break;
                        }
                    }
                }
            });
        }

        private bool EvaluateCondition(LogEntry log, ColoringCondition condition)
        {
            switch (condition.Field?.ToLower())
            {
                case "level":
                    return string.Equals(log.Level, condition.Value, StringComparison.OrdinalIgnoreCase);

                case "threadname":
                    if (string.IsNullOrEmpty(log.ThreadName)) return false;
                    return CheckOperator(log.ThreadName, condition.Operator, condition.Value);

                case "message":
                    if (string.IsNullOrEmpty(log.Message)) return false;
                    return CheckOperator(log.Message, condition.Operator, condition.Value);

                case "logger":
                    if (string.IsNullOrEmpty(log.Logger)) return false;
                    return CheckOperator(log.Logger, condition.Operator, condition.Value);

                default:
                    return false;
            }
        }

        // --- עדכון: תמיכה ב-Regex ---
        private bool CheckOperator(string text, string op, string val)
        {
            if (string.IsNullOrEmpty(op)) return false;

            switch (op.ToLower())
            {
                case "contains":
                    return text.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;
                case "equals":
                    return string.Equals(text, val, StringComparison.OrdinalIgnoreCase);
                case "begins with":
                    return text.StartsWith(val, StringComparison.OrdinalIgnoreCase);
                case "ends with":
                    return text.EndsWith(val, StringComparison.OrdinalIgnoreCase);
                case "regex":
                    try
                    {
                        return Regex.IsMatch(text, val, RegexOptions.IgnoreCase);
                    }
                    catch
                    {
                        return false; // Regex לא תקין, מתעלמים
                    }
                default:
                    return false;
            }
        }
    }
}