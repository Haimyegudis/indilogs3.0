using IndiLogs_3._0.Models.Analysis; // הוסף את ה-using הזה
using System.Collections.Generic;
using System.Collections.ObjectModel; // הוסף
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Models
{
    public class LogSessionData
    {
        public string FileName { get; set; }
        public string FilePath { get; set; } // נשמור גם נתיב מלא למקרה הצורך

        // רשימות הנתונים
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public List<EventEntry> Events { get; set; } = new List<EventEntry>();
        public List<BitmapImage> Screenshots { get; set; } = new List<BitmapImage>();

        // נתונים שהמשתמש מייצר או שמחושבים (Cache)
        public ObservableCollection<LogEntry> MarkedLogs { get; set; } = new ObservableCollection<LogEntry>();
        public List<StateEntry> CachedStates { get; set; }
        public List<AnalysisResult> CachedAnalysis { get; set; }

        // נתונים כלליים
        public string SetupInfo { get; set; }
        public string PressConfiguration { get; set; }
        public string VersionsInfo { get; set; }

        public override string ToString()
        {
            return FileName; // זה מה שירוץ ב-ListBox אם לא נגדיר Template
        }
    }
}