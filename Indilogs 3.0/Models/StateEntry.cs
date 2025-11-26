using System;
using System.Windows.Media; // חובה עבור Brush

namespace IndiLogs_3._0.Models
{
    public class StateEntry
    {
        public string StateName { get; set; }       // הסטייט הנוכחי (למשל GET_READY)
        public string TransitionTitle { get; set; } // הכותרת המלאה (למשל OFF -> GET_READY)

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public LogEntry LogReference { get; set; }

        // שדות חדשים לסטטוס
        public string Status { get; set; }          // "Success", "Failed", או ריק
        public Brush StatusColor { get; set; }      // צבע הטקסט של הסטטוס

        public string Duration
        {
            get
            {
                if (EndTime.HasValue)
                {
                    var span = EndTime.Value - StartTime;
                    return span.ToString(@"hh\:mm\:ss\.fff");
                }
                return "Current...";
            }
        }
    }
}