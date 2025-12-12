// BILINGUAL-HEADER-START
// EN: File: MechanitAnalyzer.cs - Auto-added bilingual header.
// HE: קובץ: MechanitAnalyzer.cs - כותרת דו-לשונית שנוספה אוטומטית.

using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public class MechanitAnalyzer : ILogAnalyzer
    {
        public string Name => "Mechanit Process Analyzer";

        // --- קבועים לזיהוי הסטייטים ---
        private const string ENTRY_STATE = "MECH_INIT";
        private const string SUCCESS_STATE = "STANDBY";
        private const string MANAGER_THREAD = "Manager";

        public List<AnalysisResult> Analyze(IEnumerable<LogEntry> logs)
        {
            var results = new List<AnalysisResult>();

            // חובה למיין לפי זמן כדי לנתח תהליך
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();

            List<LogEntry> currentSessionLogs = new List<LogEntry>();
            bool inMechanitSession = false;
            string lastInternalPhase = "Start"; // מעקב אחרי השלב הפנימי
            int runCounter = 1;

            for (int i = 0; i < sortedLogs.Count; i++)
            {
                var log = sortedLogs[i];

                // 1. זיהוי כניסה ל-MECH_INIT (תחילת תהליך)
                // דוגמה: PlcMngr: OFF -> MECH_INIT
                if (!inMechanitSession && IsPlcManagerTransition(log, out string toState) && toState == ENTRY_STATE)
                {
                    inMechanitSession = true;
                    currentSessionLogs.Clear();
                    currentSessionLogs.Add(log);
                    lastInternalPhase = "Init"; // איפוס שלב פנימי
                    continue;
                }

                if (inMechanitSession)
                {
                    currentSessionLogs.Add(log);

                    // 2. מעקב אחרי השלב הפנימי (MechInit Phase Tracking)
                    // דוגמה: MechInit: PHASE_1 -> PHASE_2
                    if (IsMechInitTransition(log, out string phaseName))
                    {
                        lastInternalPhase = phaseName;
                    }

                    // 3. זיהוי יציאה מ-MECH_INIT (סיום תהליך)
                    // דוגמה: PlcMngr: MECH_INIT -> STANDBY (הצלחה) או -> GO_TO_OFF (כישלון)
                    if (IsPlcManagerExit(log, out string exitState))
                    {
                        var result = AnalyzeSession(currentSessionLogs, exitState, lastInternalPhase, runCounter++);
                        results.Add(result);

                        inMechanitSession = false;
                    }
                }
            }

            // טיפול במקרה שהלוג נגמר באמצע התהליך
            if (inMechanitSession)
            {
                var result = AnalyzeSession(currentSessionLogs, "LOG ENDED", lastInternalPhase, runCounter);
                result.Status = AnalysisStatus.Failure;
                result.ErrorsFound.Add("Log file ended while process was still running.");
                results.Add(result);
            }

            return results;
        }

        private AnalysisResult AnalyzeSession(List<LogEntry> sessionLogs, string endState, string lastPhase, int runIndex)
        {
            var startTime = sessionLogs.First().Date;
            var endTime = sessionLogs.Last().Date;
            var duration = (endTime - startTime).TotalSeconds;

            var result = new AnalysisResult
            {
                // שינוי שם התהליך כפי שביקשת
                ProcessName = $"Mechanit #{runIndex} ({startTime:HH:mm:ss})",
                Steps = new List<AnalysisStep>(),
                ErrorsFound = new List<string>()
            };

            // --- לוגיקת הכרעה: הצלחה או כישלון ---
            bool isSuccess = (endState == SUCCESS_STATE);

            if (isSuccess)
            {
                result.Status = AnalysisStatus.Success;
                result.Summary = $"Success! Reached {SUCCESS_STATE} in {duration:F1}s.";
            }
            else
            {
                result.Status = AnalysisStatus.Failure;
                result.Summary = $"FAILED. Ended in '{endState}' after {duration:F1}s.\nLast Internal Phase: {lastPhase}";

                // חיפוש ה"אקדח המעשן" (Root Cause)
                FindRootCauses(sessionLogs, result);
            }

            // איסוף מידע נוסף (Warnings) גם אם הצלחנו
            // אופציונלי: אפשר להוסיף כאן בדיקת Timeout שלא הכשילה את התהליך אם תרצה

            return result;
        }

        private void FindRootCauses(List<LogEntry> logs, AnalysisResult result)
        {
            // אנחנו מחפשים שגיאות בעיקר בחלק האחרון של הלוג (נניח 10 שניות אחרונות או הכל)
            // אבל נסרוק את הכל כי הסשן מוגדר היטב

            foreach (var log in logs)
            {
                // 1. שגיאות מפורשות (Error Level)
                if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    // סינון רעשים אם צריך, כרגע מציג הכל
                    result.ErrorsFound.Add($"[Error] {log.Date:HH:mm:ss} [{log.Logger}]: {log.Message}");
                }

                // 2. אירועי כישלון ספציפיים (Enqueue event)
                // דוגמה: Enqueue event POSITION_SNS_INVALID ...
                if (log.Message.IndexOf("Enqueue event", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.ErrorsFound.Add($"[Event] {log.Date:HH:mm:ss}: {log.Message}");
                }

                // 3. חריגות קוד
                if (log.Message.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.ErrorsFound.Add($"[Exception] {log.Date:HH:mm:ss}: {log.Message}");
                }
            }

            if (result.ErrorsFound.Count == 0)
            {
                result.ErrorsFound.Add("Unknown Failure (No explicit Errors or Events found in logs).");
            }
        }

        // ------------------------------------
        // Helpers for Parsing
        // ------------------------------------

        // זיהוי מעבר ראשי: PlcMngr: X -> Y
        // מחזיר את Y (היעד)
        private bool IsPlcManagerTransition(LogEntry log, out string toState)
        {
            toState = null;
            if (log.ThreadName == MANAGER_THREAD &&
                log.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                log.Message.Contains("->"))
            {
                var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    toState = parts[1].Trim();
                    return true;
                }
            }
            return false;
        }

        // זיהוי יציאה ספציפית מ-MECH_INIT
        // PlcMngr: MECH_INIT -> Y
        private bool IsPlcManagerExit(LogEntry log, out string nextState)
        {
            nextState = null;
            if (log.ThreadName == MANAGER_THREAD &&
                log.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase) &&
                log.Message.Contains("->"))
            {
                var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    string fromState = parts[0].Replace("PlcMngr:", "").Trim();
                    if (fromState == ENTRY_STATE)
                    {
                        nextState = parts[1].Trim();
                        return true;
                    }
                }
            }
            return false;
        }

        // זיהוי מעבר פנימי: MechInit: PHASE_X -> PHASE_Y
        // מחזיר את PHASE_Y (השלב החדש)
        private bool IsMechInitTransition(LogEntry log, out string phaseName)
        {
            phaseName = null;
            // ה-Thread הוא Manager, אבל ההודעה מתחילה ב "MechInit:"
            if (log.ThreadName == MANAGER_THREAD &&
                log.Message.StartsWith("MechInit:", StringComparison.OrdinalIgnoreCase) &&
                log.Message.Contains("->"))
            {
                var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    phaseName = parts[1].Trim();
                    return true;
                }
            }
            return false;
        }
    }
}