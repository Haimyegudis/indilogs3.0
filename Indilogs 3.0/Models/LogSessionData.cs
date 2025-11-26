using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Models
{
    public class LogSessionData
    {
        public string FileName { get; set; }

        // רשימות הנתונים
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();
        public List<EventEntry> Events { get; set; } = new List<EventEntry>();
        public List<BitmapImage> Screenshots { get; set; } = new List<BitmapImage>();

        // נתונים כלליים
        public string SetupInfo { get; set; }

        // הוספנו את המאפיינים החסרים שגרמו לשגיאות
        public string PressConfiguration { get; set; }
        public string VersionsInfo { get; set; }

        public override string ToString()
        {
            return FileName;
        }
    }
}