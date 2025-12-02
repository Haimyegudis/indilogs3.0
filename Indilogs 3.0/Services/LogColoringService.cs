using IndiLogs_3._0.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    public class LogColoringService
    {
        // Cache compiled Regex to avoid recompiling every line
        private readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();

        /// <summary>
        /// מחיל צבעי ברירת מחדל.
        /// isAppLog = true -> צובע רק שגיאות באדום (עבור APP). כל השאר מתאפס.
        /// isAppLog = false -> צובע את הסט המלא (Manager, PlcMngr וכו') עבור LOGS.
        /// </summary>
        public async Task ApplyDefaultColorsAsync(IEnumerable<LogEntry> logs, bool isAppLog)
        {
            await Task.Run(() =>
            {
                // שימוש ב-Parallel לביצועים גבוהים
                Parallel.ForEach(logs, log =>
                {
                    // --- תיקון קריטי: מחקנו את הבדיקה if (log.IsMarked) return; ---
                    // אנחנו מחשבים את הצבע גם לשורות מסומנות, כדי שאם תבטל סימון, הצבע הנכון יופיע מיד.

                    // 1. איפוס צבע קיים (מוחק צבעים ישנים)
                    log.CustomColor = null;

                    // 2. חוק משותף: שגיאה (Error) תמיד אדומה
                    if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        log.CustomColor = Color.FromRgb(180, 50, 50);
                        return; // סיימנו עם השורה הזו
                    }

                    // 3. אם זה APP Log - עוצרים כאן (רק שגיאות נצבעות בדיפולט, כל השאר נקי)
                    if (isAppLog) return;

                    // 4. חוקים ל-MAIN LOGS בלבד (הסט המלא שהיה לך קודם)
                    if (Contains(log.Message, "PlcMngr:"))
                        log.CustomColor = Color.FromRgb(100, 150, 200); // Blue
                    else if (Contains(log.Message, "MechInit:"))
                        log.CustomColor = Color.FromRgb(80, 150, 80);   // Green
                    else if (Contains(log.ThreadName, "STIRCtrl"))
                        log.CustomColor = Color.FromRgb(200, 120, 50);  // Orange
                    else if (Contains(log.Logger, "Manager"))
                        log.CustomColor = Color.FromRgb(180, 150, 50);  // Gold/Mustard
                });
            });
        }

        public async Task ApplyCustomColoringAsync(IEnumerable<LogEntry> logs, List<ColoringCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0) return;

            // הכנה מוקדמת של Regex לביצועים
            var preparedConditions = PrepareConditions(conditions);

            await Task.Run(() =>
            {
                Parallel.ForEach(logs, log =>
                {
                    // --- תיקון קריטי: מחקנו את הבדיקה if (log.IsMarked) return; ---
                    // כעת הצבע מחושב תמיד ושמור ב-CustomColor.
                    // הלוגיקה ב-LogEntry.RowBackground תדאג להציג סגול אם השורה מסומנת,
                    // או את הצבע המותאם אישית אם היא לא.

                    // החוקים הידניים דורסים את הדיפולט אם יש התאמה
                    foreach (var condition in preparedConditions)
                    {
                        if (EvaluateConditionOptimized(log, condition))
                        {
                            log.CustomColor = condition.Rule.Color;
                            break; // מצאנו התאמה, יוצאים מהלולאה הפנימית
                        }
                    }
                });
            });
        }

        // --- Helpers ---

        private bool Contains(string source, string text)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // מבנה עזר לאופטימיזציה (מונע יצירת Regex מחדש לכל שורה)
        private struct PreparedCondition
        {
            public ColoringCondition Rule;
            public Regex CachedRegex;
            public string FieldLower;
            public string OpLower;
        }

        private List<PreparedCondition> PrepareConditions(List<ColoringCondition> rawRules)
        {
            var list = new List<PreparedCondition>();
            foreach (var r in rawRules)
            {
                var pc = new PreparedCondition
                {
                    Rule = r,
                    FieldLower = r.Field?.ToLower(),
                    OpLower = r.Operator?.ToLower()
                };

                if (pc.OpLower == "regex" && !string.IsNullOrEmpty(r.Value))
                {
                    try { pc.CachedRegex = new Regex(r.Value, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
                    catch { }
                }
                list.Add(pc);
            }
            return list;
        }

        private bool EvaluateConditionOptimized(LogEntry log, PreparedCondition cond)
        {
            string textToCheck = null;
            switch (cond.FieldLower)
            {
                case "message": textToCheck = log.Message; break;
                case "level": textToCheck = log.Level; break;
                case "threadname": textToCheck = log.ThreadName; break;
                case "logger": textToCheck = log.Logger; break;
            }

            if (string.IsNullOrEmpty(textToCheck)) return false;

            string val = cond.Rule.Value;
            switch (cond.OpLower)
            {
                case "contains":
                    if (string.IsNullOrEmpty(val)) return false;
                    return textToCheck.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;
                case "equals":
                    return string.Equals(textToCheck, val, StringComparison.OrdinalIgnoreCase);
                case "begins with":
                    return textToCheck.StartsWith(val, StringComparison.OrdinalIgnoreCase);
                case "ends with":
                    return textToCheck.EndsWith(val, StringComparison.OrdinalIgnoreCase);
                case "regex":
                    return cond.CachedRegex != null && cond.CachedRegex.IsMatch(textToCheck);
                default:
                    return false;
            }
        }
    }
}