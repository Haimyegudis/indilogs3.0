// BILINGUAL-HEADER-START
// EN: File: VisualAnalysisViewModel.cs - ViewModel for visual log analysis interaction.
// HE: קובץ: VisualAnalysisViewModel.cs - ViewModel לאינטראקציה וניתוח ויזואלי.

using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class VisualAnalysisViewModel : INotifyPropertyChanged
    {
        private readonly VisualGraphService _graphService;
        private PlotModel _analysisModel;
        private LogEntry _selectedLogDetail;
        private string _statusMessage = "Ready";

        // רשימת הלוגים שמוצגת בחלק התחתון (המסוננת לפי הסטייט שנבחר)
        public ObservableCollection<LogEntry> ScopeLogs { get; set; } = new ObservableCollection<LogEntry>();

        // אירוע שמודיע לחלון הראשי לנווט לשורה ספציפית
        public event Action<LogEntry> RequestNavigateToLog;

        public VisualAnalysisViewModel()
        {
            _graphService = new VisualGraphService();
            AnalysisModel = new PlotModel { Title = "No Data Loaded", TextColor = OxyColors.White };
        }

        public PlotModel AnalysisModel
        {
            get => _analysisModel;
            set { _analysisModel = value; OnPropertyChanged(); }
        }

        public LogEntry SelectedLogDetail
        {
            get => _selectedLogDetail;
            set
            {
                _selectedLogDetail = value;
                OnPropertyChanged();
                // אם המשתמש בחר לוג ברשימה למטה, אפשר להודיע לחלון הראשי
                if (value != null) RequestNavigateToLog?.Invoke(value);
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // טעינת נתונים ראשונית
        public async Task LoadDataAsync(List<StateEntry> states, List<LogEntry> allLogs)
        {
            if (states == null || !states.Any())
            {
                StatusMessage = "No states found to visualize.";
                return;
            }

            StatusMessage = "Building visualization...";

            // סינון שגיאות קריטיות להצגה על הגרף
            var errors = allLogs.Where(l => l.Level == "Error" || l.Message.Contains("Exception")).ToList();

            AnalysisModel = await _graphService.CreateGanttModelAsync(states, errors);

            // רישום לאירוע לחיצה על הגרף
            AnalysisModel.MouseDown += (s, e) => OnGraphMouseDown(e, states, allLogs);

            AnalysisModel.InvalidatePlot(true);
            StatusMessage = "Visualization Ready. Click on a bar to see logs.";
        }

        private void OnGraphMouseDown(OxyMouseDownEventArgs e, List<StateEntry> states, List<LogEntry> allLogs)
        {
            if (e.HitTestResult == null || e.HitTestResult.Item == null) return;

            // בדיקה אם לחצו על בר (סטייט)
            if (e.HitTestResult.Item is IntervalBarItem barItem)
            {
                var timeStart = OxyPlot.Axes.DateTimeAxis.ToDateTime(barItem.Start);
                var timeEnd = OxyPlot.Axes.DateTimeAxis.ToDateTime(barItem.End);

                // סינון הלוגים לרשימה התחתונה
                var relevantLogs = allLogs.Where(l => l.Date >= timeStart && l.Date <= timeEnd).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScopeLogs.Clear();
                    foreach (var log in relevantLogs) ScopeLogs.Add(log);
                    StatusMessage = $"Selected State Range: {timeStart:HH:mm:ss} - {timeEnd:HH:mm:ss} ({relevantLogs.Count} logs)";
                });
            }

            // בדיקה אם לחצו על שגיאה (נקודה)
            if (e.HitTestResult.Item is ScatterPoint point)
            {
                var time = OxyPlot.Axes.DateTimeAxis.ToDateTime(point.X);
                // מציאת הלוג המדויק
                var exactLog = allLogs.FirstOrDefault(l => Math.Abs((l.Date - time).TotalMilliseconds) < 100); // 100ms tolerance
                if (exactLog != null)
                {
                    RequestNavigateToLog?.Invoke(exactLog);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}