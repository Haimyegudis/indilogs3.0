using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IndiLogs_3._0.Models.Analysis
{
    public enum AnalysisStatus { Success, Warning, Failure }

    public class AnalysisResult
    {
        public string ProcessName { get; set; }
        public AnalysisStatus Status { get; set; }
        public string Summary { get; set; }
        public List<AnalysisStep> Steps { get; set; } = new List<AnalysisStep>();
        public List<string> ErrorsFound { get; set; } = new List<string>();

        // עזר ל-UI
        public Brush StatusColor => Status == AnalysisStatus.Success ? Brushes.Green :
                                    Status == AnalysisStatus.Warning ? Brushes.Orange : Brushes.Red;
    }

    public class AnalysisStep
    {
        public string StepName { get; set; }     // למשל: "Init Motors"
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationMs => (EndTime - StartTime).TotalMilliseconds;
        public string Status { get; set; }       // "OK", "TIMEOUT", "ERROR"
        public LogEntry StartLog { get; set; }   // מצביע לשורה שהתחילה את השלב
        public LogEntry EndLog { get; set; }     // מצביע לשורה שסיימה
    }
}