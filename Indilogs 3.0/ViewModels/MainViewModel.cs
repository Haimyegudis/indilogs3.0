using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // --- Services ---
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;
        private StatesWindow _statesWindow;
        private bool _isTimeFocusActive = false;
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };
        private AnalysisReportWindow _analysisWindow;
        public ObservableCollection<LogSessionData> LoadedSessions { get; set; }
        private LogSessionData _selectedSession;
        public LogSessionData SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession != value)
                {
                    _selectedSession = value;
                    OnPropertyChanged();
                    SwitchToSession(_selectedSession); // פונקציה חדשה שנטמיע מיד
                }
            }
        }
        // --- Live Monitoring Variables ---
        private CancellationTokenSource _liveCts;
        private string _liveFilePath;
        private int _totalLogsReadFromFile;
        private long _lastKnownFileSize;
        private const string LIVE_FILE_NAME = "no-sn.engineGroupA.file";
        private const int POLLING_INTERVAL_MS = 3000;
        private MarkedLogsWindow _markedLogsWindow;
        private DispatcherTimer _searchDebounceTimer;
        public ICommand FilterToStateCommand { get; }
        public ICommand RunAnalysisCommand { get; }
        public ICommand OpenStatesWindowCommand { get; }

        // --- Fonts & UI Properties ---
        public ObservableCollection<string> AvailableFonts { get; set; }
        private string _selectedFont;
        public string SelectedFont
        {
            get => _selectedFont;
            set { if (_selectedFont != value) { _selectedFont = value; OnPropertyChanged(); UpdateContentFont(_selectedFont); } }
        }

        private bool _isBold;
        public bool IsBold
        {
            get => _isBold;
            set { if (_isBold != value) { _isBold = value; OnPropertyChanged(); UpdateContentFontWeight(value); } }
        }
        private string _selectedTimeUnit = "Seconds";
        public string SelectedTimeUnit
        {
            get => _selectedTimeUnit;
            set { _selectedTimeUnit = value; OnPropertyChanged(); }
        }
        private void SwitchToSession(LogSessionData session)
        {
            if (session == null) return;

            IsBusy = true;

            // 1. עדכון המקור הראשי (עבור פילטרים עתידיים)
            _allLogsCache = session.Logs;

            // עדכון הטאב הראשי (Logs)
            Logs = session.Logs;

            // 2. === התיקון: הפעלת הפילטר הדיפולטי עבור הטאב LOGS FILTERED ===
            // אנו משתמשים בפונקציה הקיימת IsDefaultLog כדי לסנן את הלוגים החשובים
            var defaultFilteredLogs = session.Logs.Where(l => IsDefaultLog(l)).ToList();

            // דחיפת התוצאות לרשימה המוצגת ב-UI
            FilteredLogs.ReplaceAll(defaultFilteredLogs);

            // (אופציונלי) אם אתה רוצה שהלוג הראשון ייבחר אוטומטית
            if (FilteredLogs.Count > 0) SelectedLog = FilteredLogs[0];

            // 3. עדכון שאר הנתונים (Events, Screenshots)
            // המרה מ-List רגיל (שבסשן) ל-ObservableCollection (שה-UI צריך)
            Events = new ObservableCollection<EventEntry>(session.Events);
            OnPropertyChanged(nameof(Events));

            Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots);
            OnPropertyChanged(nameof(Screenshots));

            // 4. נתונים נלווים
            MarkedLogs = session.MarkedLogs;
            OnPropertyChanged(nameof(MarkedLogs));

            SetupInfo = session.SetupInfo;
            PressConfig = session.PressConfiguration;

            // עדכון כותרת החלון
            if (!string.IsNullOrEmpty(session.VersionsInfo))
                WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            // איפוס מצב חיפוש/פילטר ב-UI כדי למנוע בלבול
            // (אלא אם כן תרצה לשמור את מילות החיפוש בין קבצים)
            SearchText = "";
            IsFilterActive = false;
            IsFilterOutActive = false;

            IsBusy = false;
        }
        // --- Live Mode Properties ---
        private bool _isLiveMode;
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set { _isLiveMode = value; OnPropertyChanged(); }
        }

        // פונקציית עזר קטנה לניקוי הטקסט
        private string ExtractStateName(string raw)
        {
            return raw.Trim();
        }
        // --- פונקציה חדשה לסינון לפי סטייט ---
        private void FilterToState(object obj)
        {
            if (obj is StateEntry state)
            {
                IsBusy = true;
                StatusMessage = $"Focusing state: {state.StateName}...";

                Task.Run(() =>
                {
                    DateTime start = state.StartTime;
                    // אם אין זמן סיום, לוקחים עד הסוף
                    DateTime end = state.EndTime ?? DateTime.MaxValue;

                    // שלב 1: שליפת כל הלוגים בטווח הזמן של הסטייט
                    var rawTimeSlice = _allLogsCache
                        .Where(l => l.Date >= start && l.Date <= end)
                        .ToList();

                    // שלב 2: חישוב הלוגים לטאב "Filtered Logs" (סינון דיפולטי)
                    var smartFiltered = rawTimeSlice
                        .Where(l => IsDefaultLog(l))
                        .ToList();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // עדכון ה-Cache כדי שמנגנון הפילטר הראשי ישתמש בו
                        _lastFilteredCache = rawTimeSlice;

                        // עדכון הטאב המשני (Logs Filtered)
                        if (FilteredLogs != null)
                        {
                            FilteredLogs.ReplaceAll(smartFiltered);
                        }

                        // --- כאן השינוי הגדול ---
                        // איפוס פילטר מתקדם (כי אנחנו בפוקוס זמן)
                        _savedFilterRoot = null;

                        // סימון שאנחנו במצב של פוקוס זמן/סטייט (כדי ש-ToggleFilterView ידע להשתמש ב-Cache)
                        _isTimeFocusActive = true;

                        // הדלקת הצ'קבוקס - זה יפעיל אוטומטית את ToggleFilterView(true)
                        IsFilterActive = true;

                        StatusMessage = $"State: {state.StateName} | Showing {rawTimeSlice.Count} logs";
                        IsBusy = false;
                    });
                });
            }
        }
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPaused));
            }
        }

        public bool IsPaused => !IsRunning;

        // --- Search & Highlight Properties ---
        private bool _isSearchPanelVisible;
        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            set
            {
                _isSearchPanelVisible = value;
                OnPropertyChanged();
                if (!value) SearchText = string.Empty;
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();

                    // במקום לפלטר מיד - מאתחלים את הטיימר
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        // --- Context Time Property (For Slider) ---
        private int _contextSeconds = 10;
        public int ContextSeconds
        {
            get => _contextSeconds;
            set
            {
                if (_contextSeconds != value)
                {
                    _contextSeconds = value;
                    OnPropertyChanged();
                }
            }
        }
        private void OpenStatesWindow(object obj)
        {
            // 1. בדיקה אם החלון כבר פתוח - רק להביא לפוקוס
            if (_statesWindow != null && _statesWindow.IsVisible)
            {
                _statesWindow.Activate();
                if (_statesWindow.WindowState == WindowState.Minimized)
                    _statesWindow.WindowState = WindowState.Normal;
                return;
            }

            // 2. בדיקה שיש סשן ולוגים
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any())
            {
                MessageBox.Show("No logs loaded.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 3. === בדיקת Cache: האם כבר חישבנו סטייטים לקובץ הזה? ===
            if (SelectedSession.CachedStates != null && SelectedSession.CachedStates.Count > 0)
            {
                // פתיחה מיידית של החלון עם הנתונים השמורים
                _statesWindow = new StatesWindow(SelectedSession.CachedStates, this);
                _statesWindow.Owner = Application.Current.MainWindow; // קיבוע לחלון הראשי
                _statesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; // פתיחה במרכז
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                _statesWindow.Show();
                return;
            }

            // 4. אם אין Cache, מתחילים חישוב
            IsBusy = true;
            StatusMessage = "Analyzing States...";

            // העתקת רשימת הלוגים לשימוש ב-Task (למניעת בעיות גישה בין Threads)
            var logsToProcess = SelectedSession.Logs.ToList();

            Task.Run(() =>
            {
                var statesList = new List<StateEntry>();

                // סינון רק לוגים רלוונטיים של מעברי סטייט
                var transitionLogs = logsToProcess
                    .Where(l => l.ThreadName != null &&
                                l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) &&
                                l.Message != null &&
                                l.Message.IndexOf("PlcMngr:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                l.Message.Contains("->"))
                    .OrderBy(l => l.Date)
                    .ToList();

                if (transitionLogs.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Ready";
                        MessageBox.Show("No state transitions found in this log.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

                DateTime logEndLimit = logsToProcess.Last().Date;

                // לולאת הניתוח
                for (int i = 0; i < transitionLogs.Count; i++)
                {
                    var currentLog = transitionLogs[i];
                    var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;

                    string fromStateRaw = parts[0].Replace("PlcMngr:", "").Trim();
                    string toStateRaw = parts[1].Trim();

                    var entry = new StateEntry
                    {
                        StateName = toStateRaw,
                        TransitionTitle = $"{fromStateRaw} -> {toStateRaw}",
                        StartTime = currentLog.Date,
                        LogReference = currentLog,
                        Status = "",
                        StatusColor = Brushes.Gray
                    };

                    // חישוב זמן סיום (הלוג הבא הוא הסוף של הנוכחי)
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLog = transitionLogs[i + 1];
                        entry.EndTime = nextLog.Date;

                        // בדיקות לוגיות להצלחה/כישלון (GetReady / MechInit)
                        var nextParts = nextLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextParts.Length >= 2)
                        {
                            string nextDestination = nextParts[1].Trim();

                            if (entry.StateName.Equals("GET_READY", StringComparison.OrdinalIgnoreCase) ||
                                entry.StateName.Equals("GR", StringComparison.OrdinalIgnoreCase))
                            {
                                if (nextDestination.Equals("DYNAMIC_READY", StringComparison.OrdinalIgnoreCase))
                                { entry.Status = "SUCCESS"; entry.StatusColor = Brushes.LightGreen; }
                                else { entry.Status = "FAILED"; entry.StatusColor = Brushes.Red; }
                            }
                            else if (entry.StateName.Equals("MECH_INIT", StringComparison.OrdinalIgnoreCase))
                            {
                                if (nextDestination.Equals("STANDBY", StringComparison.OrdinalIgnoreCase))
                                { entry.Status = "SUCCESS"; entry.StatusColor = Brushes.LightGreen; }
                                else { entry.Status = "FAILED"; entry.StatusColor = Brushes.Red; }
                            }
                        }
                    }
                    else
                    {
                        // הסטייט האחרון בלוג
                        entry.EndTime = logEndLimit;
                        entry.Status = "Current";
                    }
                    statesList.Add(entry);
                }

                // מיון הסופי (מהחדש לישן או ההפך, לבחירתך. כאן זה מהחדש לישן לתצוגה נוחה)
                var displayList = statesList.OrderByDescending(s => s.StartTime).ToList();

                // חזרה ל-UI Thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsBusy = false;
                    StatusMessage = "Ready";

                    // שמירה ב-Cache של הסשן הנוכחי
                    if (SelectedSession != null)
                    {
                        SelectedSession.CachedStates = displayList;
                    }

                    // יצירת החלון והצגתו
                    _statesWindow = new StatesWindow(displayList, this);
                    _statesWindow.Owner = Application.Current.MainWindow; // קיבוע לחלון הראשי
                    _statesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; // פתיחה במרכז
                    _statesWindow.Closed += (s, e) => _statesWindow = null;
                    _statesWindow.Show();
                });
            });
        }
        // --- Caches ---
        private ObservableRangeCollection<LogEntry> _liveLogsCollection;
        private IList<LogEntry> _allLogsCache;
        private IList<LogEntry> _lastFilteredCache = new List<LogEntry>();

        // רשימות הפילטרים
        private List<string> _negativeFilters = new List<string>();
        private List<string> _activeThreadFilters = new List<string>();

        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();
        private FilterNode _savedFilterRoot = null;

        public event Action<LogEntry> RequestScrollToLog;

        // --- Collections ---
        private IEnumerable<LogEntry> _logs;
        public IEnumerable<LogEntry> Logs { get => _logs; set { _logs = value; OnPropertyChanged(); } }

        public ObservableRangeCollection<LogEntry> FilteredLogs { get; set; }
        public ObservableCollection<EventEntry> Events { get; set; }
        public ObservableCollection<BitmapImage> Screenshots { get; set; }
        public ObservableCollection<string> LoadedFiles { get; set; }
        public ObservableCollection<SavedConfiguration> SavedConfigs { get; set; }
        public ObservableCollection<LogEntry> MarkedLogs { get; set; }

        // --- Selected Items & State ---
        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; OnPropertyChanged(); } }

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle { get => _windowTitle; set { _windowTitle = value; OnPropertyChanged(); } }

        private string _setupInfo;
        public string SetupInfo { get => _setupInfo; set { _setupInfo = value; OnPropertyChanged(); } }

        private string _pressConfig;
        public string PressConfig { get => _pressConfig; set { _pressConfig = value; OnPropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private double _currentProgress;
        public double CurrentProgress { get => _currentProgress; set { _currentProgress = value; OnPropertyChanged(); } }

        private string _statusMessage;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        private LogEntry _selectedLog;
        public LogEntry SelectedLog { get => _selectedLog; set { _selectedLog = value; OnPropertyChanged(); } }

        private SavedConfiguration _selectedConfig;
        public SavedConfiguration SelectedConfig
        {
            get => _selectedConfig;
            set { _selectedConfig = value; OnPropertyChanged(); }
        }

        // --- Filter States ---
        private bool _isFilterActive;
        public bool IsFilterActive
        {
            get => _isFilterActive;
            set { if (_isFilterActive != value) { _isFilterActive = value; OnPropertyChanged(); ToggleFilterView(value); } }
        }

        private bool _isFilterOutActive = false;
        public bool IsFilterOutActive
        {
            get => _isFilterOutActive;
            set
            {
                if (_isFilterOutActive != value)
                {
                    _isFilterOutActive = value;
                    OnPropertyChanged();
                    ToggleFilterView(IsFilterActive);
                }
            }
        }

        private bool _isDarkMode;
        public bool IsDarkMode { get => _isDarkMode; set { _isDarkMode = value; ApplyTheme(value); OnPropertyChanged(); } }

        private double _gridFontSize = 12;
        public double GridFontSize { get => _gridFontSize; set { _gridFontSize = value; OnPropertyChanged(); } }

        private double _screenshotZoom = 400;
        public double ScreenshotZoom { get => _screenshotZoom; set { _screenshotZoom = value; OnPropertyChanged(); } }

        // --- Commands ---
        public ICommand LoadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand MarkRowCommand { get; }
        public ICommand NextMarkedCommand { get; }
        public ICommand PrevMarkedCommand { get; }
        public ICommand JumpToLogCommand { get; }
        public ICommand OpenJiraCommand { get; }
        public ICommand OpenKibanaCommand { get; }
        public ICommand OpenOutlookCommand { get; }
        public ICommand OpenGraphViewerCommand { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand CloseSearchCommand { get; }
        public ICommand OpenFilterWindowCommand { get; }
        public ICommand OpenColoringWindowCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand RemoveConfigCommand { get; }
        public ICommand ApplyConfigCommand { get; }
        public ICommand FilterOutCommand { get; }
        public ICommand FilterOutThreadCommand { get; }
        public ICommand OpenThreadFilterCommand { get; }
        public ICommand FilterContextCommand { get; }
        public ICommand UndoFilterOutCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ViewLogDetailsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleBoldCommand { get; }
        public ICommand OpenFontsWindowCommand { get; }
        public ICommand OpenMarkedLogsWindowCommand { get; }

        // Live Commands
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }

        public MainViewModel()
        {
            _logService = new LogFileService();
            _coloringService = new LogColoringService();
            RunAnalysisCommand = new RelayCommand(RunAnalysis);
            _allLogsCache = new List<LogEntry>();
            Logs = new List<LogEntry>();
            LoadedSessions = new ObservableCollection<LogSessionData>();
            FilteredLogs = new ObservableRangeCollection<LogEntry>();
            Events = new ObservableCollection<EventEntry>();
            Screenshots = new ObservableCollection<BitmapImage>();
            LoadedFiles = new ObservableCollection<string>();
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            FilterToStateCommand = new RelayCommand(FilterToState);
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);
            SelectedFont = "Segoe UI";
            LoadCommand = new RelayCommand(LoadFile);
            ClearCommand = new RelayCommand(ClearLogs);
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);
            OpenStatesWindowCommand = new RelayCommand(OpenStatesWindow);
            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);
            OpenGraphViewerCommand = new RelayCommand(OpenGraphViewer);

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500); // מחכה חצי שנייה אחרי סיום ההקלדה
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            ToggleSearchCommand = new RelayCommand(o => { IsSearchPanelVisible = !IsSearchPanelVisible; });
            CloseSearchCommand = new RelayCommand(o => { IsSearchPanelVisible = false; SearchText = ""; });

            OpenFilterWindowCommand = new RelayCommand(OpenFilterWindow);
            OpenColoringWindowCommand = new RelayCommand(OpenColoringWindow);
            SaveConfigCommand = new RelayCommand(SaveConfiguration);
            LoadConfigCommand = new RelayCommand(LoadConfigurationFromFile);
            RemoveConfigCommand = new RelayCommand(RemoveConfiguration, o => SelectedConfig != null);
            ApplyConfigCommand = new RelayCommand(ApplyConfiguration);

            FilterOutCommand = new RelayCommand(FilterOut);
            FilterOutThreadCommand = new RelayCommand(FilterOutThread);
            OpenThreadFilterCommand = new RelayCommand(OpenThreadFilter);
            FilterContextCommand = new RelayCommand(FilterContext);
            UndoFilterOutCommand = new RelayCommand(UndoFilterOut);

            ViewLogDetailsCommand = new RelayCommand(ViewLogDetails);
            ToggleThemeCommand = new RelayCommand(o => IsDarkMode = !IsDarkMode);
            ToggleBoldCommand = new RelayCommand(o => IsBold = !IsBold);
            OpenSettingsCommand = new RelayCommand(OpenSettingsWindow);
            OpenFontsWindowCommand = new RelayCommand(OpenFontsWindow);
            OpenMarkedLogsWindowCommand = new RelayCommand(OpenMarkedLogsWindow);

            ZoomInCommand = new RelayCommand(o => { if (SelectedTabIndex == 3) ScreenshotZoom = Math.Min(5000, ScreenshotZoom + 100); else GridFontSize = Math.Min(30, GridFontSize + 1); });
            ZoomOutCommand = new RelayCommand(o => { if (SelectedTabIndex == 3) ScreenshotZoom = Math.Max(100, ScreenshotZoom - 100); else GridFontSize = Math.Max(8, GridFontSize - 1); });

            LivePlayCommand = new RelayCommand(LivePlay);
            LivePauseCommand = new RelayCommand(LivePause);
            LiveClearCommand = new RelayCommand(LiveClear);

            ApplyTheme(false);
            LoadSavedConfigurations();
        }
        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop(); // עוצר את הטיימר
            ToggleFilterView(IsFilterActive); // מפעיל את הפילטר פעם אחת בלבד
        }
        // בתוך MainViewModel.cs


        // --- TIME FOCUS FILTER ---
        private void RunAnalysis(object obj)
        {
            // 1. בדיקות תקינות
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any())
            {
                MessageBox.Show("No logs loaded to analyze.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. בדיקה אם החלון כבר פתוח
            if (_analysisWindow != null && _analysisWindow.IsVisible)
            {
                _analysisWindow.Activate();
                if (_analysisWindow.WindowState == WindowState.Minimized)
                    _analysisWindow.WindowState = WindowState.Normal;
                return;
            }

            // 3. === בדיקת Cache: האם כבר ניתחנו את הקובץ הזה? ===
            if (SelectedSession.CachedAnalysis != null && SelectedSession.CachedAnalysis.Any())
            {
                // פתיחה מיידית של החלון עם התוצאות השמורות
                OpenAnalysisWindow(SelectedSession.CachedAnalysis);
                return;
            }

            // 4. אם אין תוצאות שמורות - מריצים ניתוח חדש
            IsBusy = true;
            StatusMessage = "Initializing Analysis...";

            // מעתיקים את הלוגים לרשימה נפרדת כדי למנוע בעיות של גישה בין Threads
            var logsToAnalyze = SelectedSession.Logs.ToList();

            Task.Run(() =>
            {
                try
                {
                    var allResults = new List<AnalysisResult>();

                    // הרצת Mechanit Analyzer
                    ReportProgress(10, "Running Mechanit Analyzer...");
                    var mechAnalyzer = new MechanitAnalyzer();
                    var mechResults = mechAnalyzer.Analyze(logsToAnalyze);
                    if (mechResults != null) allResults.AddRange(mechResults);

                    // הרצת GetReady Analyzer
                    ReportProgress(50, "Running GetReady Analyzer...");
                    var grAnalyzer = new GetReadyAnalyzer();
                    var grResults = grAnalyzer.Analyze(logsToAnalyze);
                    if (grResults != null) allResults.AddRange(grResults);

                    ReportProgress(90, "Finalizing Report...");

                    // סיום וחזרה ל-UI Thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Ready";

                        if (allResults.Count == 0)
                        {
                            MessageBox.Show("No processes found (Mechanit/GetReady).", "Analysis Result");
                        }
                        else
                        {
                            // === שמירת התוצאות ב-Cache של הסשן הנוכחי ===
                            if (SelectedSession != null)
                            {
                                SelectedSession.CachedAnalysis = allResults;
                            }

                            // פתיחת החלון
                            OpenAnalysisWindow(allResults);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        MessageBox.Show($"TASK ERROR:\n{ex.Message}", "Error");
                    });
                }
            });
        }

        // פונקציית עזר לפתיחת החלון (כדי למנוע שכפול קוד)
        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results);
            _analysisWindow.Owner = Application.Current.MainWindow;
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            _analysisWindow.Show();
        }
        // פונקציית עזר לעדכון מהיר של ה-UI מה-Thread המשני
        private void ReportProgress(double percent, string msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentProgress = percent;
                StatusMessage = msg;
            });
        }
        private void FilterContext(object obj)
        {
            if (SelectedLog == null) return;

            IsBusy = true;

            // חישוב הטווח בשניות בהתאם ליחידה שנבחרה
            double multiplier = SelectedTimeUnit == "Minutes" ? 60 : 1;
            double rangeInSeconds = ContextSeconds * multiplier;

            string unitLabel = SelectedTimeUnit == "Minutes" ? "min" : "sec";
            StatusMessage = $"Applying Focus Time (+/- {ContextSeconds} {unitLabel})...";

            DateTime targetTime = SelectedLog.Date;
            DateTime start = targetTime.AddSeconds(-rangeInSeconds);
            DateTime end = targetTime.AddSeconds(rangeInSeconds);

            Task.Run(() =>
            {
                var contextLogs = _allLogsCache.Where(l =>
                    l.Date >= start &&
                    l.Date <= end
                ).OrderByDescending(l => l.Date).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _lastFilteredCache = contextLogs;

                    // איפוס פילטר מתקדם והדלקת דגל Time Focus
                    _savedFilterRoot = null;
                    _isTimeFocusActive = true;

                    IsFilterActive = true;
                    ToggleFilterView(true);

                    StatusMessage = $"Focus Time: +/- {rangeInSeconds}s | {contextLogs.Count} logs shown";
                    IsBusy = false;
                });
            });
        }

        private void JumpToLog(object obj)
        {
            if (obj is LogEntry log)
            {
                SelectedLog = log;
                RequestScrollToLog?.Invoke(log);
            }
        }

        private void MarkRow(object obj)
        {
            if (SelectedLog != null)
            {
                SelectedLog.IsMarked = !SelectedLog.IsMarked;

                if (SelectedLog.IsMarked)
                {
                    MarkedLogs.Add(SelectedLog);
                    var sorted = MarkedLogs.OrderBy(x => x.Date).ToList();
                    MarkedLogs.Clear();
                    foreach (var l in sorted) MarkedLogs.Add(l);
                }
                else
                {
                    MarkedLogs.Remove(SelectedLog);
                }
            }
        }

        private void ClearLogs(object obj)
        {
            _isTimeFocusActive = false;
            if (_allLogsCache != null) _allLogsCache.Clear();
            _lastFilteredCache.Clear();
            _negativeFilters.Clear();
            _activeThreadFilters.Clear();
            Logs = new List<LogEntry>();
            FilteredLogs.Clear();
            Events.Clear();
            Screenshots.Clear();
            LoadedFiles.Clear();
            MarkedLogs.Clear();
            CurrentProgress = 0; SetupInfo = ""; PressConfig = ""; ScreenshotZoom = 400;
            IsFilterOutActive = false;
            LoadedSessions.Clear();
            SelectedSession = null;

            // איפוס תצוגה
            Logs = new List<LogEntry>();
            FilteredLogs.Clear();
            Events.Clear();
            Screenshots.Clear();
            MarkedLogs = new ObservableCollection<LogEntry>();
        }

        private void OpenMarkedLogsWindow(object obj)
        {
            // בדיקה אם החלון קיים (למקרה נדיר שהוא פתוח ולא סגור)
            if (_markedLogsWindow != null && _markedLogsWindow.IsVisible)
            {
                _markedLogsWindow.Activate();
                return;
            }

            // יצירה מחדש - מבטיח מיקום נכון בכל פעם
            _markedLogsWindow = new MarkedLogsWindow { DataContext = this };
            _markedLogsWindow.Owner = Application.Current.MainWindow; // נדבק לחלון הראשי
            _markedLogsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; // מתמרכז אליו
            _markedLogsWindow.Closed += (s, e) => _markedLogsWindow = null; // איפוס המשתנה בסגירה
            _markedLogsWindow.Show();
        }

        private async void ProcessFiles(string[] filePaths)
        {
            StopLiveMonitoring(); // אם היינו בלייב, עוצרים

            IsBusy = true;
            StatusMessage = "Processing files...";

            try
            {
                // הערה: אנחנו *לא* קוראים ל-ClearLogs() כדי לא למחוק קבצים קודמים!

                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                // שימוש בשירות הטעינה הקיים
                // שיפור קטן: LogFileService מחזיר סשן אחד מאוחד.
                // אם המשתמש בחר כמה קבצים בבת אחת, נרצה אולי לטעון כל אחד בנפרד?
                // הקוד הנוכחי שלך ב-Service מאחד הכל. אם תרצה להפריד, תצטרך לולאה כאן.
                // נניח כרגע שכל הטעינה היא "סשן אחד" (כמו ZIP או קבוצת לוגים קשורה).

                var newSession = await _logService.LoadSessionAsync(filePaths, progress);

                // הגדרת שם לסשן (שם הקובץ הראשון או שם ה-ZIP)
                newSession.FileName = System.IO.Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1) newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                // החלת צבעים (חשוב לעשות את זה לפני ההוספה)
                StatusMessage = "Applying Colors...";
                await _coloringService.ApplyDefaultColorsAsync(newSession.Logs);

                // הוספה לרשימה בצד שמאל
                LoadedSessions.Add(newSession);

                // מעבר אוטומטי לקובץ החדש שנטען
                SelectedSession = newSession;

                CurrentProgress = 100;
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading files: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void StartLiveMonitoring(string path)
        {
            ClearLogs(null);
            LoadedFiles.Add(Path.GetFileName(path));

            _liveFilePath = path;
            _lastKnownFileSize = 0;
            _totalLogsReadFromFile = 0;

            _liveLogsCollection = new ObservableRangeCollection<LogEntry>();
            _allLogsCache = _liveLogsCollection;
            Logs = _liveLogsCollection;

            IsLiveMode = true;
            IsRunning = true;
            WindowTitle = "IndiLogs 3.0 - LIVE MONITORING";
            StatusMessage = "Live monitoring started...";

            _liveCts = new CancellationTokenSource();
            Task.Run(() => PollingLoop(_liveCts.Token));
        }

        private void StopLiveMonitoring()
        {
            if (_liveCts != null)
            {
                _liveCts.Cancel();
                _liveCts = null;
            }

            IsLiveMode = false;
            IsRunning = false;
            _liveFilePath = null;
            WindowTitle = "IndiLogs 3.0";
        }

        private async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsRunning && File.Exists(_liveFilePath))
                    {
                        FileInfo fi = new FileInfo(_liveFilePath);
                        long currentLen = fi.Length;

                        if (currentLen > _lastKnownFileSize)
                        {
                            await FetchNewData(currentLen);
                        }
                        else if (currentLen < _lastKnownFileSize)
                        {
                            _lastKnownFileSize = 0;
                            _totalLogsReadFromFile = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Polling Error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(POLLING_INTERVAL_MS, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task FetchNewData(long currentFileLength)
        {
            List<LogEntry> newItems = null;

            await Task.Run(() =>
            {
                try
                {
                    using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var ms = new MemoryStream())
                        {
                            fs.CopyTo(ms);
                            ms.Position = 0;

                            var allLogs = _logService.ParseLogStream(ms);
                            int totalCount = allLogs.Count;
                            int newLogsCount = totalCount - _totalLogsReadFromFile;

                            if (newLogsCount > 0)
                            {
                                newItems = allLogs.Skip(_totalLogsReadFromFile).ToList();
                                _totalLogsReadFromFile = totalCount;
                                _lastKnownFileSize = currentFileLength;
                            }
                            else
                            {
                                _lastKnownFileSize = currentFileLength;
                            }
                        }
                    }
                }
                catch { }
            });

            if (newItems != null && newItems.Count > 0)
            {
                await _coloringService.ApplyDefaultColorsAsync(newItems);
                if (_savedColoringRules.Count > 0)
                    await _coloringService.ApplyCustomColoringAsync(newItems, _savedColoringRules);

                newItems.Reverse();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _liveLogsCollection.InsertRange(0, newItems);

                    // עדכון הלוגים במסך בזמן אמת - כרגע פשוט מוסיף, לא מפעיל פילטר מורכב על הלייב
                    List<LogEntry> itemsForFilteredTab = newItems.Where(IsDefaultLog).ToList();

                    if (itemsForFilteredTab != null && itemsForFilteredTab.Count > 0)
                    {
                        FilteredLogs.InsertRange(0, itemsForFilteredTab);
                        SelectedLog = FilteredLogs[0];
                    }
                });
            }
        }

        private bool IsDefaultLog(LogEntry l)
        {
            if (string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Message != null && l.Message.StartsWith("PlcMngr:", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Events", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Logger != null && l.Logger.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (l.ThreadName != null && l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private void LivePlay(object obj)
        {
            IsRunning = true;
            StatusMessage = "Live monitoring active.";
        }

        private void LivePause(object obj)
        {
            IsRunning = false;
            StatusMessage = "Live monitoring paused.";
        }

        private void LiveClear(object obj)
        {
            IsRunning = false;
            if (_allLogsCache != null) _allLogsCache.Clear();
            FilteredLogs.Clear();

            Task.Run(async () =>
            {
                if (!File.Exists(_liveFilePath)) return;
                try
                {
                    using (var fs = new FileStream(_liveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var ms = new MemoryStream())
                        {
                            await fs.CopyToAsync(ms);
                            ms.Position = 0;
                            var logs = _logService.ParseLogStream(ms);
                            _totalLogsReadFromFile = logs.Count;
                            _lastKnownFileSize = fs.Length;
                        }
                    }
                }
                catch { }
            });

            StatusMessage = "Cleared. Press Play to resume from now.";
        }

        private void OpenGraphViewer(object obj)
        {
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IoRecorderViewer.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
                }
                else
                {
                    MessageBox.Show($"File not found:\n{exePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenSettingsWindow(object obj)
        {
            var win = new SettingsWindow { DataContext = this };

            // הגדרה קריטית: משייך את החלון לחלון הראשי כדי שלא ייעלם מאחוריו
            if (Application.Current.MainWindow != null && Application.Current.MainWindow != win)
            {
                win.Owner = Application.Current.MainWindow;
            }

            bool positioned = false;

            if (obj is FrameworkElement element)
            {
                try
                {
                    // חישוב המיקום הפיזי על המסך
                    Point point = element.PointToScreen(new Point(0, 0));

                    // המרה מפיקסלים פיזיים ליחידות לוגיות (WPF Units) - תיקון למסכים עם Scaling
                    var source = PresentationSource.FromVisual(element);
                    if (source != null && source.CompositionTarget != null)
                    {
                        var transform = source.CompositionTarget.TransformFromDevice;
                        var corner = transform.Transform(point);

                        win.Left = corner.X - 150; // הזזה קטנה שמאלה כדי שהחלון לא יברח מהמסך
                        win.Top = corner.Y + element.ActualHeight + 5;
                        positioned = true;
                    }
                }
                catch
                {
                    // אם החישוב נכשל, לא נורא - נפתח במרכז
                }
            }

            // אם לא הצלחנו למקם (או שהכפתור לא נשלח), נפתח במרכז המסך של האפליקציה
            if (!positioned)
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            // וידוא שהחלון לא יוצא מגבולות המסך (במקרה של הזזה ידנית)
            if (win.Left < 0) win.Left = 0;
            if (win.Top < 0) win.Top = 0;

            win.Show();
        }
        private void OpenFontsWindow(object obj) { new FontsWindow { DataContext = this }.ShowDialog(); }

        private void UpdateContentFont(string fontName) { if (!string.IsNullOrEmpty(fontName) && Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName)); }
        private void UpdateContentFontWeight(bool isBold) { if (Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontWeight", isBold ? FontWeights.Bold : FontWeights.Normal); }

        public void OnFilesDropped(string[] files) { if (files != null && files.Length > 0) ProcessFiles(files); }
        private void LoadFile(object obj) { var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|All files (*.*)|*.*" }; if (dialog.ShowDialog() == true) ProcessFiles(dialog.FileNames); }

        // --- FILTER LOGIC (UPDATED) ---

        // הפונקציה הראשית שמחליטה מה להציג
        private void ToggleFilterView(bool show)
        {
            IEnumerable<LogEntry> currentLogs;

            if (show)
            {
                // בדיקה: האם יש פילטר מתקדם פעיל?
                bool hasAdvancedFilter = _savedFilterRoot != null &&
                                         _savedFilterRoot.Children != null &&
                                         _savedFilterRoot.Children.Count > 0;

                // === התיקון: מכבדים את התוצאות השמורות גם אם זה Advanced Filter וגם אם זה Time Focus ===
                if (hasAdvancedFilter || _isTimeFocusActive)
                {
                    currentLogs = _lastFilteredCache ?? new List<LogEntry>();
                }
                else
                {
                    currentLogs = _allLogsCache;
                }

                // ... המשך הפונקציה נשאר זהה ...
                if (_activeThreadFilters.Any())
                {
                    currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));
                }

                if (!string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2)
                {
                    currentLogs = currentLogs.Where(l =>
                       (l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (l.ThreadName != null && l.ThreadName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                   );
                }
            }
            else
            {
                currentLogs = _allLogsCache;
            }

            // --- Filter Out (רץ תמיד) ---
            if (IsFilterOutActive && _negativeFilters.Any())
            {
                currentLogs = currentLogs.Where(l =>
                {
                    foreach (var f in _negativeFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            string threadPart = f.Substring(7);
                            if (l.ThreadName != null && l.ThreadName.IndexOf(threadPart, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                        else
                        {
                            if (l.Message != null && l.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                    }
                    return true;
                });
            }

            Logs = currentLogs.ToList();
        }
        private void FilterOut(object p)
        {
            if (SelectedLog == null) return;
            var w = new FilterOutWindow(SelectedLog.Message);
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.TextToRemove))
            {
                _negativeFilters.Add(w.TextToRemove);
                IsFilterOutActive = true;
                ToggleFilterView(IsFilterActive);
            }
        }

        // --- Filter Out Thread (Updated to use Window + Prefix + IndexOf logic) ---
        private void FilterOutThread(object obj)
        {
            if (SelectedLog == null || string.IsNullOrEmpty(SelectedLog.ThreadName)) return;

            // פותח חלון דיאלוג עם שם ה-Thread
            var win = new FilterOutWindow(SelectedLog.ThreadName);

            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.TextToRemove))
            {
                // --- מחיקת השורות הבעייתיות ---
                // _savedFilterRoot = ... <-- זה לא רלוונטי כאן, זה שייך לפילטר המתקדם
                // _isTimeFocusActive = false; <-- לא חובה לאפס את זה בפילטר שלילי, הוא יכול לעבוד מעל TimeFocus

                string threadToHide = win.TextToRemove;
                string filterKey = "THREAD:" + threadToHide; // קידומת מזהה

                if (!_negativeFilters.Contains(filterKey))
                {
                    _negativeFilters.Add(filterKey);
                    IsFilterOutActive = true; // מדליק את הצ'קבוקס
                    ToggleFilterView(IsFilterActive); // רענון התצוגה
                }
            }
        }

        // --- Open Thread Filter (Updated for Multi-Select & Auto-Clear) ---
        private void OpenThreadFilter(object obj)
        {
            if (_allLogsCache == null || !_allLogsCache.Any()) return;

            var threads = _allLogsCache
                          .Select(l => l.ThreadName)
                          .Where(t => !string.IsNullOrEmpty(t))
                          .Distinct()
                          .OrderBy(t => t)
                          .ToList();

            var win = new ThreadFilterWindow(threads);
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear)
                {
                    _activeThreadFilters.Clear();

                    // אם אין פילטר מתקדם פעיל, נכבה את מצב הפילטר הראשי
                    if (_savedFilterRoot == null)
                    {
                        IsFilterActive = false;
                    }
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    _activeThreadFilters = win.SelectedThreads;
                    IsFilterActive = true;
                }

                ToggleFilterView(IsFilterActive);
            }
        }

        private async void OpenFilterWindow(object obj)
        {
            var win = new FilterWindow();
            if (_savedFilterRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(_savedFilterRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                _savedFilterRoot = win.ViewModel.RootNodes.FirstOrDefault();

                bool hasAdvancedFilter = _savedFilterRoot != null && _savedFilterRoot.Children.Count > 0;
                bool hasThreadFilter = _activeThreadFilters.Any();
                bool shouldBeActive = hasAdvancedFilter || hasThreadFilter;

                IsBusy = true;
                await Task.Run(() =>
                {
                    if (hasAdvancedFilter)
                    {
                        var res = _allLogsCache.Where(l => EvaluateFilterNode(l, _savedFilterRoot)).ToList();
                        _lastFilteredCache = res;
                    }
                    else
                    {
                        _lastFilteredCache.Clear();
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsFilterActive = shouldBeActive;
                        ToggleFilterView(IsFilterActive);
                    });
                });
                IsBusy = false;
            }
        }

        private async void OpenColoringWindow(object obj)
        {
            try
            {
                var win = new ColoringWindow();
                var rulesCopy = _savedColoringRules.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    _savedColoringRules = win.ResultConditions;
                    IsBusy = true; StatusMessage = "Applying colors...";
                    await _coloringService.ApplyDefaultColorsAsync(_allLogsCache);
                    await _coloringService.ApplyCustomColoringAsync(_allLogsCache, _savedColoringRules);
                    Application.Current.Dispatcher.Invoke(() => { if (Logs != null) foreach (var log in Logs) log.OnPropertyChanged("RowBackground"); });
                    StatusMessage = $"Applied {_savedColoringRules.Count} color rules"; IsBusy = false;
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); IsBusy = false; }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node)
        {
            // הגנה מפני קריסה
            if (node == null) return true;

            // =========================================================
            // מקרה 1: בדיקת תנאי בודד (עלה בעץ - Condition)
            // =========================================================
            if (node.Type == NodeType.Condition)
            {
                string val = "";

                // שליפת הערך מהלוג לפי השדה שנבחר
                switch (node.Field)
                {
                    case "Level": val = log.Level; break;
                    case "ThreadName": val = log.ThreadName; break;
                    case "Logger": val = log.Logger; break;
                    case "ProcessName": val = log.ProcessName; break;
                    default: val = log.Message; break; // ברירת מחדל: Message
                }

                if (string.IsNullOrEmpty(val)) return false;

                string op = node.Operator;       // למשל: Contains
                string criteria = node.Value;    // למשל: "Error"

                // ביצוע ההשוואה
                if (op == "Equals") return val.Equals(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Begins With") return val.StartsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Ends With") return val.EndsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Regex")
                {
                    try { return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                    catch { return false; }
                }
                // ברירת מחדל: Contains
                return val.IndexOf(criteria, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // =========================================================
            // מקרה 2: בדיקת קבוצה (Group) - רקורסיה
            // =========================================================
            else
            {
                // קבוצה ריקה:
                // אם זה AND, מחזירים True (כי אין משהו שיכשיל).
                // אם זה OR, מחזירים False (כי אין משהו שיקיים).
                // אבל כדי לא להסתיר לוגים סתם, נהוג שקבוצה ריקה היא "שקופה" (True).
                if (node.Children == null || node.Children.Count == 0) return true;

                string op = node.LogicalOperator; // AND, OR, NOT AND, NOT OR

                // שלב א': זיהוי לוגיקת הבסיס (האם אנחנו בודקים "כולם" או "אחד")
                // "NOT AND" מתבסס על לוגיקה של AND (שמוכחשת בסוף)
                // "NOT OR" מתבסס על לוגיקה של OR (שמוכחשת בסוף)

                bool isBaseOr = op.Contains("OR"); // תופס גם את "OR" וגם את "NOT OR"
                bool baseResult;

                if (isBaseOr)
                {
                    // --- לוגיקת OR בסיסית ---
                    // מספיק שילד אחד יחזיר True כדי שהבסיס יהיה True.
                    baseResult = false;
                    foreach (var child in node.Children)
                    {
                        // קריאה רקורסיבית! (בודק גם קבוצות בתוך קבוצות)
                        if (EvaluateFilterNode(log, child))
                        {
                            baseResult = true;
                            break; // מצאנו אחד נכון, לא צריך להמשיך לבדוק
                        }
                    }
                }
                else
                {
                    // --- לוגיקת AND בסיסית --- (תופס גם AND וגם NOT AND)
                    // מספיק שילד אחד יחזיר False כדי שהבסיס יהיה False.
                    baseResult = true;
                    foreach (var child in node.Children)
                    {
                        // קריאה רקורסיבית!
                        if (!EvaluateFilterNode(log, child))
                        {
                            baseResult = false;
                            break; // מצאנו אחד שגוי, ה-AND נכשל
                        }
                    }
                }

                // שלב ב': בדיקת היפוך (NOT)
                // אם האופרטור הוא NOT AND או NOT OR, הופכים את התוצאה
                if (op.StartsWith("NOT"))
                {
                    return !baseResult;
                }

                return baseResult;
            }
        }

        private void LoadSavedConfigurations()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
            if (Directory.Exists(path))
                foreach (var f in Directory.GetFiles(path, "*.json"))
                {
                    try
                    {
                        var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(f));
                        c.FilePath = f;
                        SavedConfigs.Add(c);
                    }
                    catch { }
                }
        }

        private async void ApplyConfiguration(object parameter)
        {
            if (parameter is SavedConfiguration c)
            {
                IsBusy = true;
                StatusMessage = $"Applying config: {c.Name}...";

                // 1. החלת חוקי צביעה
                if (c.ColoringRules != null)
                {
                    _savedColoringRules = c.ColoringRules;
                    await _coloringService.ApplyDefaultColorsAsync(_allLogsCache);
                    await _coloringService.ApplyCustomColoringAsync(_allLogsCache, c.ColoringRules);

                    // רענון התצוגה לצבעים
                    if (Logs != null)
                        foreach (var log in Logs) log.OnPropertyChanged("RowBackground");
                }

                // 2. החלת פילטרים
                if (c.FilterRoot != null)
                {
                    _savedFilterRoot = c.FilterRoot;

                    // חישוב התוצאות של הפילטר השמור
                    var res = await Task.Run(() => _allLogsCache.Where(l => EvaluateFilterNode(l, c.FilterRoot)).ToList());

                    _lastFilteredCache = res;

                    // אנחנו לא בפוקוס זמן אלא בפילטר מתקדם
                    _isTimeFocusActive = false;

                    // הדלקת הצ'קבוקס (מפעיל את ToggleFilterView)
                    IsFilterActive = true;
                }

                IsBusy = false;
                StatusMessage = "Configuration applied.";
            }
        }
        private void RemoveConfiguration(object parameter)
        {
            var configToDelete = SelectedConfig;

            if (configToDelete != null && MessageBox.Show($"Delete '{configToDelete.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (File.Exists(configToDelete.FilePath)) File.Delete(configToDelete.FilePath);
                SavedConfigs.Remove(configToDelete);
            }
        }

        private void SaveConfiguration(object obj)
        {
            var existingNames = SavedConfigs.Select(c => c.Name).ToList();
            var dlg = new SaveConfigWindow(existingNames);

            if (dlg.ShowDialog() == true)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
                Directory.CreateDirectory(dir);
                var cfg = new SavedConfiguration
                {
                    Name = dlg.ConfigName,
                    ColoringRules = _savedColoringRules,
                    FilterRoot = _savedFilterRoot,
                    CreatedDate = DateTime.Now,
                    FilePath = Path.Combine(dir, dlg.ConfigName + ".json")
                };
                File.WriteAllText(cfg.FilePath, JsonConvert.SerializeObject(cfg));
                SavedConfigs.Add(cfg);
            }
        }

        private void LoadConfigurationFromFile(object obj)
        {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(dlg.FileName));
                    c.FilePath = dlg.FileName;
                    SavedConfigs.Add(c);
                }
                catch { }
            }
        }

        private void UndoFilterOut(object parameter) { }

        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log) new LogDetailsWindow(log).Show();
        }

        private void ApplyTheme(bool isDark)
        {
            var dict = Application.Current.Resources;
            if (isDark)
            {
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Color.FromRgb(37, 37, 38)));
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(45, 45, 48)));
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Colors.White));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(160, 160, 160)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(63, 63, 70)));
            }
            else
            {
                UpdateResource(dict, "BgDark", new SolidColorBrush(Color.FromRgb(243, 243, 243)));
                UpdateResource(dict, "BgPanel", new SolidColorBrush(Colors.White));
                UpdateResource(dict, "BgCard", new SolidColorBrush(Color.FromRgb(250, 250, 250)));
                UpdateResource(dict, "TextPrimary", new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                UpdateResource(dict, "TextSecondary", new SolidColorBrush(Color.FromRgb(100, 100, 100)));
                UpdateResource(dict, "BorderColor", new SolidColorBrush(Color.FromRgb(220, 220, 220)));
            }
        }

        private void UpdateResource(ResourceDictionary dict, string key, object value)
        {
            if (dict.Contains(key)) dict.Remove(key);
            dict.Add(key, value);
        }

        private void GoToNextMarked(object obj)
        {
            if (!Logs.Any()) return;
            var list = Logs.ToList();
            int current = SelectedLog != null ? list.IndexOf(SelectedLog) : -1;
            var next = list.Skip(current + 1).FirstOrDefault(l => l.IsMarked) ?? list.FirstOrDefault(l => l.IsMarked);
            if (next != null) { SelectedLog = next; RequestScrollToLog?.Invoke(next); }
        }

        private void GoToPrevMarked(object obj)
        {
            if (!Logs.Any()) return;
            var list = Logs.ToList();
            int current = SelectedLog != null ? list.IndexOf(SelectedLog) : list.Count;
            var prev = list.Take(current).LastOrDefault(l => l.IsMarked) ?? list.LastOrDefault(l => l.IsMarked);
            if (prev != null) { SelectedLog = prev; RequestScrollToLog?.Invoke(prev); }
        }

        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        private void OpenKibana(object obj) { }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}