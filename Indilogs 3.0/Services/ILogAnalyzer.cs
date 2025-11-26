using System.Collections.Generic;
using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Services.Analysis
{
    public interface ILogAnalyzer
    {
        string Name { get; }
        List<AnalysisResult> Analyze(IEnumerable<LogEntry> logs);
    }
}