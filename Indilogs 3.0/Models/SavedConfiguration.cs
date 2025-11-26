using IndiLogs_3._0.Services;
using System;
using System.Collections.Generic;

namespace IndiLogs_3._0.Models
{
    public class SavedConfiguration
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public List<ColoringCondition> ColoringRules { get; set; }
        public FilterNode FilterRoot { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}