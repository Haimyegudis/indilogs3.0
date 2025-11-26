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
        private AnalysisReportWindow _analysisWindow;
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

                    // שלב 1: שליפת *כל* הלוגים בטווח הזמן (ללא שום סינון תוכן)
                    // זה ילך לטאב הראשי (Logs)
                    var rawTimeSlice = _allLogsCache
                        .Where(l => l.Date >= start && l.Date <= end)
                        .ToList();

                    // שלב 2: הפעלת "הפילטר הדיפולטי" על הטווח הזה
                    // זה ילך לטאב המשני (Logs Filtered)
                    var smartFiltered = rawTimeSlice
                        .Where(l => IsDefaultLog(l))
                        .ToList();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // עדכון ה-Cache למקרה שנרצה לעשות חיפוש נוסף בתוך הטווח
                        _lastFilteredCache = rawTimeSlice;

                        // --- איפוס פילטרים ידניים כדי לא להסתיר מידע בטאב הראשי ---
                        _savedFilterRoot = null;
                        _activeThreadFilters.Clear();
                        _negativeFilters.Clear();
                        IsFilterOutActive = false;

                        // אנחנו מכבים את הדגל IsFilterActive כי אנחנו מציגים "הכל" (בטווח הזמן) בטאב הראשי
                        // (או שמשאירים דלוק אבל מוודאים ש-Logs מכיל את המידע הגולמי)
                        IsFilterActive = false;

                        // עדכון הטאב הראשי - מציג הכל!
                        Logs = rawTimeSlice;

                        // עדכון הטאב המסונן - מציג רק את החשובים!
                        if (FilteredLogs != null)
                        {
                            FilteredLogs.ReplaceAll(smartFiltered);
                        }

                        StatusMessage = $"State: {state.StateName} | Showing {rawTimeSlice.Count} logs (Raw)";
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
            // בדיקה 1: האם החלון כבר פתוח?
            if (_statesWindow != null && _statesWindow.IsVisible)
            {
                _statesWindow.Activate(); // הבא לפוקוס
                if (_statesWindow.WindowState == WindowState.Minimized)
                    _statesWindow.WindowState = WindowState.Normal;
                return; // יציאה - לא פותחים חלון חדש ולא מריצים ניתוח מחדש
            }

            if (_allLogsCache == null || !_allLogsCache.Any())
            {
                MessageBox.Show("No logs loaded.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = "Analyzing States...";

            Task.Run(() =>
            {
                // --- (לוגיקת הניתוח נשארת זהה לחלוטין - העתקתי אותה לנוחותך) ---
                var statesList = new List<StateEntry>();
                var sortedAllLogs = _allLogsCache.OrderBy(l => l.Date).ToList();
                DateTime logEndLimit = sortedAllLogs.Last().Date;

                var transitionLogs = _allLogsCache
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
                        MessageBox.Show("No state transitions found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

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

                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLog = transitionLogs[i + 1];
                        entry.EndTime = nextLog.Date;

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
                        entry.EndTime = logEndLimit;
                        entry.Status = "Current";
                    }
                    statesList.Add(entry);
                }

                var displayList = statesList.OrderByDescending(s => s.StartTime).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsBusy = false;
                    StatusMessage = "Ready";

                    // יצירת החלון ושמירת הרפרנס
                    _statesWindow = new StatesWindow(displayList, this);

                    // ברגע שהחלון נסגר - מאפסים את המשתנה כדי שבפעם הבאה יפתח חדש
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
            // בדיקה: אם החלון כבר פתוח - הבא לפוקוס וצא
            if (_analysisWindow != null && _analysisWindow.IsVisible)
            {
                _analysisWindow.Activate();
                if (_analysisWindow.WindowState == WindowState.Minimized)
                    _analysisWindow.WindowState = WindowState.Normal;
                return;
            }

            if (Logs == null || !Logs.Any())
            {
                MessageBox.Show("No logs loaded to analyze.", "Debug Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            StatusMessage = "Initializing Analysis..."; // הודעה התחלתית

            var logsToAnalyze = Logs.ToList();

            Task.Run(() =>
            {
                try
                {
                    var allResults = new List<AnalysisResult>();

                    // עדכון סטטוס למשתמש
                    ReportProgress(10, "Running Mechanit Analyzer...");
                    var mechAnalyzer = new MechanitAnalyzer();
                    var mechResults = mechAnalyzer.Analyze(logsToAnalyze);
                    if (mechResults != null) allResults.AddRange(mechResults);

                    // עדכון סטטוס למשתמש
                    ReportProgress(50, "Running GetReady Analyzer...");
                    var grAnalyzer = new GetReadyAnalyzer();
                    var grResults = grAnalyzer.Analyze(logsToAnalyze);
                    if (grResults != null) allResults.AddRange(grResults);

                    ReportProgress(90, "Finalizing Report...");

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
                            // פתיחת החלון ושמירת הרפרנס
                            _analysisWindow = new AnalysisReportWindow(allResults);
                            _analysisWindow.Owner = Application.Current.MainWindow;
                            _analysisWindow.Closed += (s, e) => _analysisWindow = null; // איפוס בסגירה
                            _analysisWindow.Show();
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

        // פונקציית עזר לעדכון מהיר של ה-UI מה-Thread המשני

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
            double rangeInSeconds = ContextSeconds;

            StatusMessage = $"Applying Focus Time (+/- {rangeInSeconds}s)...";

            DateTime targetTime = SelectedLog.Date;
            DateTime start = targetTime.AddSeconds(-rangeInSeconds);
            DateTime end = targetTime.AddSeconds(rangeInSeconds);

            Task.Run(() =>
            {
                // סינון לפי זמן + מיון יורד
                var contextLogs = _allLogsCache.Where(l =>
                    l.Date >= start &&
                    l.Date <= end
                ).OrderByDescending(l => l.Date).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // עדכון המאגר המסונן
                    _lastFilteredCache = contextLogs;

                    // חשוב: איפוס פילטר מתקדם כדי שהתוכנה תדע להשתמש ב-_lastFilteredCache
                    _savedFilterRoot = null;

                    // הפעלת הפילטר
                    IsFilterActive = true;

                    // קריאה לפונקציית התצוגה
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
        }

        private void OpenMarkedLogsWindow(object obj)
        {
            if (_markedLogsWindow == null)
            {
                _markedLogsWindow = new MarkedLogsWindow { DataContext = this };
                _markedLogsWindow.Closed += (s, e) => _markedLogsWindow = null;
            }

            if (_markedLogsWindow.IsVisible) _markedLogsWindow.Hide();
            else { _markedLogsWindow.Show(); _markedLogsWindow.Activate(); }
        }

        private async void ProcessFiles(string[] filePaths)
        {
            StopLiveMonitoring();

            // (בדיקת Live Mode נשארת אותו דבר...)
            if (filePaths.Length == 1)
            {
                string fileName = Path.GetFileName(filePaths[0]);
                if (fileName.StartsWith(LIVE_FILE_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    LoadedFiles.Clear();
                    LoadedFiles.Add(fileName);
                    StartLiveMonitoring(filePaths[0]);
                    return;
                }
            }

            IsBusy = true;
            CurrentProgress = 0;
            StatusMessage = "Starting...";
            ClearLogs(null);

            try
            {
                foreach (var f in filePaths) LoadedFiles.Add(Path.GetFileName(f));

                // --- השינוי כאן: Progress מקבל עכשיו (double, string) ---
                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                var session = await _logService.LoadSessionAsync(filePaths, progress);

                // שאר הקוד נשאר זהה...
                _allLogsCache = session.Logs;
                _isFilterActive = false;
                OnPropertyChanged(nameof(IsFilterActive));

                if (!string.IsNullOrEmpty(session.VersionsInfo)) WindowTitle = $"IndiLogs 3.0 - {session.VersionsInfo}";

                StatusMessage = "Applying Colors...";
                await _coloringService.ApplyDefaultColorsAsync(session.Logs);

                var def = _allLogsCache.Where(l => IsDefaultLog(l)).ToList();
                FilteredLogs.ReplaceAll(def);

                CurrentProgress = 100;
                StatusMessage = "Ready";
                Logs = _allLogsCache;

                foreach (var e in session.Events) Events.Add(e);
                foreach (var s in session.Screenshots) Screenshots.Add(s);
                SetupInfo = session.SetupInfo ?? "";
                PressConfig = session.PressConfiguration ?? "";
            }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
            finally { IsBusy = false; }
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
            if (obj is FrameworkElement element)
            {
                Point point = element.PointToScreen(new Point(0, 0));
                win.Left = point.X;
                win.Top = point.Y + element.ActualHeight;
            }
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

            // ... (חלק 1 וחלק 2 של הפונקציה נשארים ללא שינוי - העתק מהקוד הקודם) ...
            // חלק 1: סינון חיובי...
            if (show)
            {
                if (_lastFilteredCache != null && _lastFilteredCache.Count > 0) currentLogs = _lastFilteredCache;
                else currentLogs = _allLogsCache;

                if (_activeThreadFilters.Any()) currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));
            }
            else currentLogs = _allLogsCache;

            // חלק 2: סינון שלילי (Filter Out)...
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

            // --- חלק 3 המתוקן: Quick Search ---
            if (!string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2)
            {
                currentLogs = currentLogs.Where(l =>
                    // מחפש רק ב-Message וב-ThreadName (כי העפנו את Logger)
                    (l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (l.ThreadName != null && l.ThreadName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                );
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
                string threadToHide = win.TextToRemove;
                string filterKey = "THREAD:" + threadToHide; // קידומת מזהה

                if (!_negativeFilters.Contains(filterKey))
                {
                    _negativeFilters.Add(filterKey);
                    IsFilterOutActive = true; // מדליק את הצ'קבוקס
                    ToggleFilterView(IsFilterActive);
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
            if (node == null) return true;
            if (node.Type == NodeType.Condition)
            {
                string val = "";
                if (node.Field == "Level") val = log.Level;
                else if (node.Field == "ThreadName") val = log.ThreadName;
                else if (node.Field == "Logger") val = log.Logger;
                else val = log.Message;

                if (string.IsNullOrEmpty(val)) return false;

                if (node.Operator == "Equals") return val.Equals(node.Value, StringComparison.OrdinalIgnoreCase);
                if (node.Operator == "Begins With") return val.StartsWith(node.Value, StringComparison.OrdinalIgnoreCase);
                if (node.Operator == "Ends With") return val.EndsWith(node.Value, StringComparison.OrdinalIgnoreCase);
                if (node.Operator == "Regex") { try { return Regex.IsMatch(val, node.Value, RegexOptions.IgnoreCase); } catch { return false; } }

                return val.IndexOf(node.Value, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else
            {
                if (node.Children.Count == 0) return true;
                bool isAnd = node.LogicalOperator.Contains("AND");
                bool isNot = node.LogicalOperator.Contains("NOT");
                bool result = isAnd;
                foreach (var child in node.Children)
                {
                    bool childRes = EvaluateFilterNode(log, child);
                    if (isAnd) result &= childRes; else result |= childRes;
                }
                return isNot ? !result : result;
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
                if (c.ColoringRules != null)
                {
                    _savedColoringRules = c.ColoringRules;
                    await _coloringService.ApplyDefaultColorsAsync(_allLogsCache);
                    await _coloringService.ApplyCustomColoringAsync(_allLogsCache, c.ColoringRules);
                }
                if (c.FilterRoot != null)
                {
                    _savedFilterRoot = c.FilterRoot;
                    var res = await Task.Run(() => _allLogsCache.Where(l => EvaluateFilterNode(l, c.FilterRoot)).ToList());
                    _lastFilteredCache = res;
                    _isFilterActive = true;
                    OnPropertyChanged(nameof(IsFilterActive));
                    ToggleFilterView(true);
                }
                IsBusy = false;
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