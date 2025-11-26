using System;
using System.Collections.Generic;
using System.Linq;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public class GetReadyAnalyzer : ILogAnalyzer
    {
        public string Name => "Get Ready Process Analyzer";

        // --- קבועים לזיהוי הסטייטים ---
        private const string ENTRY_STATE = "GET_READY";
        private const string SUCCESS_STATE = "DYNAMIC_READY";
        private const string MANAGER_THREAD = "Manager";

        public List<AnalysisResult> Analyze(IEnumerable<LogEntry> logs)
        {
            var results = new List<AnalysisResult>();

            // חובה למיין לפי זמן
            var sortedLogs = logs.OrderBy(l => l.Date).ToList();

            List<LogEntry> currentSessionLogs = new List<LogEntry>();
            bool inSession = false;
            string lastInternalPhase = "Start";
            int runCounter = 1;

            for (int i = 0; i < sortedLogs.Count; i++)
            {
                var log = sortedLogs[i];

                // 1. זיהוי כניסה ל-GET_READY (תחילת תהליך)
                // דוגמה: PlcMngr: STANDBY -> GET_READY
                if (!inSession && IsPlcManagerTransition(log, out string toState) && toState == ENTRY_STATE)
                {
                    inSession = true;
                    currentSessionLogs.Clear();
                    currentSessionLogs.Add(log);
                    lastInternalPhase = "Init";
                    continue;
                }

                if (inSession)
                {
                    currentSessionLogs.Add(log);

                    // 2. מעקב אחרי השלב הפנימי (GetReady OR PostGrToDynamic)
                    // דוגמה: GetReady: PHASE_1 -> PHASE_2
                    // דוגמה: PostGrToDynamic: PHASE_1 -> PHASE_EXIT
                    if (IsInternalPhaseTransition(log, out string phaseName))
                    {
                        lastInternalPhase = phaseName;
                    }

                    // 3. זיהוי יציאה מ-GET_READY (סיום תהליך)
                    // דוגמה: PlcMngr: GET_READY -> DYNAMIC_READY (הצלחה)
                    // דוגמה: PlcMngr: GET_READY -> GO_TO_STANDBY (כישלון)
                    if (IsPlcManagerExit(log, out string exitState))
                    {
                        var result = AnalyzeSession(currentSessionLogs, exitState, lastInternalPhase, runCounter++);
                        results.Add(result);

                        inSession = false;
                    }
                }
            }

            // טיפול במקרה שהלוג נגמר באמצע התהליך
            if (inSession)
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
                ProcessName = $"GetReady #{runIndex} ({startTime:HH:mm:ss})",
                Steps = new List<AnalysisStep>(), // אפשר להרחיב בעתיד לפירוט שלבים
                ErrorsFound = new List<string>()
            };

            // --- לוגיקת הכרעה ---
            if (endState == SUCCESS_STATE)
            {
                result.Status = AnalysisStatus.Success;
                result.Summary = $"Success! Reached {SUCCESS_STATE} in {duration:F1}s.";
            }
            else
            {
                result.Status = AnalysisStatus.Failure;
                string failReason = endState == "GO_TO_OFF" ? "Critical Safety/Emergency" : "Process Aborted";

                result.Summary = $"FAILED ({failReason}). Ended in '{endState}' after {duration:F1}s.\n" +
                                 $"Last Internal Phase: {lastPhase}";

                // חיפוש ה"אקדח המעשן" (Root Cause)
                FindRootCauses(sessionLogs, result);
            }

            return result;
        }

        private void FindRootCauses(List<LogEntry> logs, AnalysisResult result)
        {
            foreach (var log in logs)
            {
                // 1. שגיאות מפורשות (Error Level)
                if (string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    // מתעלמים משגיאות שנוצרות כתוצאה מהכישלון עצמו (כמו Fault "Off" is not valid)
                    if (!log.Message.Contains("is not valid at current state"))
                    {
                        result.ErrorsFound.Add($"[Error] {log.Date:HH:mm:ss} [{log.Logger}]: {log.Message}");
                    }
                }

                // 2. אירועי כישלון ספציפיים (Enqueue event)
                // דוגמה: Enqueue event BLANKET_TENSIONER_REACHED_LIMIT...
                if (log.Message.IndexOf("Enqueue event", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // מתעלמים מאירוע שינוי סטייט שקורה תמיד בכישלון
                    if (!log.Message.Contains("PLC_FAILURE_STATE_CHANGE"))
                    {
                        result.ErrorsFound.Add($"[Event] {log.Date:HH:mm:ss}: {log.Message}");
                    }
                }

                // 3. חריגות
                if (log.Message.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.ErrorsFound.Add($"[Exception] {log.Date:HH:mm:ss}: {log.Message}");
                }
            }

            if (result.ErrorsFound.Count == 0)
            {
                result.ErrorsFound.Add("Unknown Failure (No explicit Errors found). Check manual logs.");
            }
        }

        // ------------------------------------
        // Helpers for Parsing
        // ------------------------------------

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

        // מזהה מעבר של אחד מהתהליכים הפנימיים: GetReady או PostGrToDynamic
        private bool IsInternalPhaseTransition(LogEntry log, out string phaseName)
        {
            phaseName = null;

            if (log.ThreadName != MANAGER_THREAD) return false;
            if (!log.Message.Contains("->")) return false;

            bool isGetReady = log.Message.StartsWith("GetReady:", StringComparison.OrdinalIgnoreCase);
            bool isPostGr = log.Message.StartsWith("PostGrToDynamic:", StringComparison.OrdinalIgnoreCase);

            if (isGetReady || isPostGr)
            {
                var parts = log.Message.Split(new[] { "->" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    string prefix = isGetReady ? "GR" : "PostGR";
                    string rawPhase = parts[1].Trim();
                    phaseName = $"{prefix}: {rawPhase}"; // למשל: "GR: PHASE_2_POST_GR"
                    return true;
                }
            }
            return false;
        }
    }
}