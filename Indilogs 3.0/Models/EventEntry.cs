using System;

namespace IndiLogs_3._0.Models
{
    public class EventEntry
    {
        public DateTime Time { get; set; }
        public string Name { get; set; }
        public string State { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; } // Parameters/Subsystem
    }
}