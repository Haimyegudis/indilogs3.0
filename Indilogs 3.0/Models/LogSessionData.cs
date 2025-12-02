using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services.Analysis; // וודא שה-namespace הזה קיים אצלך או הסר אם לא
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Models
{
    public class LogSessionData
    {
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();

        // --- תוספת חדשה ---
        public List<LogEntry> AppDevLogs { get; set; } = new List<LogEntry>();
        // ------------------

        public List<EventEntry> Events { get; set; } = new List<EventEntry>();
        public List<BitmapImage> Screenshots { get; set; } = new List<BitmapImage>();
        public ObservableCollection<LogEntry> MarkedLogs { get; set; } = new ObservableCollection<LogEntry>();

        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string SetupInfo { get; set; }
        public string PressConfiguration { get; set; }
        public string VersionsInfo { get; set; }

        // Caches
        public List<StateEntry> CachedStates { get; set; }
        public List<AnalysisResult> CachedAnalysis { get; set; }
    }
}