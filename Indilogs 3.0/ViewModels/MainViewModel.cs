using IndiLogs_3._0.Models;
using IndiLogs_3._0.Models.Analysis;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Analysis;
using IndiLogs_3._0.Views;
using Microsoft.Win32;
using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using OxyPlot.Legends;
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
        public ObservableCollection<LogEntry> MarkedAppLogs { get; set; }
        private FilterNode _mainFilterRoot = null;
        private FilterNode _appFilterRoot = null;
        private List<ColoringCondition> _mainColoringRules = new List<ColoringCondition>();
        private List<ColoringCondition> _appColoringRules = new List<ColoringCondition>();
        private List<LogEntry> _lastFilteredAppCache;
        private bool _isAppTimeFocusActive;
        public GraphsViewModel GraphsVM { get; set; }
        // --- Services ---
        private readonly LogFileService _logService;
        private readonly LogColoringService _coloringService;
        private readonly CsvExportService _csvService;

        // --- Windows Instances ---
        private StatesWindow _statesWindow;
        private AnalysisReportWindow _analysisWindow;
        private MarkedLogsWindow _markedMainLogsWindow;
        private MarkedLogsWindow _markedAppLogsWindow;

        // --- הפרדת משתנים (State Separation) ---
        private bool _isMainFilterActive;
        private bool _isAppFilterActive;

        private bool _isMainFilterOutActive;
        private bool _isAppFilterOutActive;

        // --- Timers ---
        private DispatcherTimer _searchDebounceTimer;

        // --- Caches ---
        private ObservableRangeCollection<LogEntry> _liveLogsCollection;
        private IList<LogEntry> _allLogsCache;              // Cache for Main Logs
        private IList<LogEntry> _allAppLogsCache;           // Cache for AppDev Logs
        private IList<LogEntry> _lastFilteredCache = new List<LogEntry>();

        // --- Filter States ---
        private List<string> _negativeFilters = new List<string>();
        private List<string> _activeThreadFilters = new List<string>();
        private List<ColoringCondition> _savedColoringRules = new List<ColoringCondition>();
        private FilterNode _savedFilterRoot = null;
        private bool _isTimeFocusActive = false;

        // --- Tree Filter State (NEW LOGIC) ---
        private LoggerNode _selectedTreeItem;
        public LoggerNode SelectedTreeItem
        {
            get => _selectedTreeItem;
            set { _selectedTreeItem = value; OnPropertyChanged(); }
        }

        // HashSet ללוגרים ספציפיים שמוסתרים (ללא ילדים)
        private HashSet<string> _treeHiddenLoggers = new HashSet<string>();
        // HashSet לענפים שלמים שמוסתרים (Prefix)
        private HashSet<string> _treeHiddenPrefixes = new HashSet<string>();

        // מצבי "הצג רק את..." (Isolation Mode)
        private string _treeShowOnlyLogger = null; // מצב 3: Show Only This Logger
        private string _treeShowOnlyPrefix = null; // מצב 4: Show This Logger With Children

        // --- Live Monitoring Variables ---
        private CancellationTokenSource _liveCts;
        private string _liveFilePath;
        private int _totalLogsReadFromFile;
        private long _lastKnownFileSize;
        private const string LIVE_FILE_NAME = "no-sn.engineGroupA.file";
        private const int POLLING_INTERVAL_MS = 3000;

        // --- Collections ---
        // Main Logs Tab
        private IEnumerable<LogEntry> _logs;
        public IEnumerable<LogEntry> Logs
        {
            get => _logs;
            set { _logs = value; OnPropertyChanged(); }
        }
        public ObservableRangeCollection<LogEntry> FilteredLogs { get; set; }

        // APP Tab
        private ObservableRangeCollection<LogEntry> _appDevLogsFiltered;
        public ObservableRangeCollection<LogEntry> AppDevLogsFiltered
        {
            get => _appDevLogsFiltered;
            set { _appDevLogsFiltered = value; OnPropertyChanged(); }
        }

        // Tree
        public ObservableCollection<LoggerNode> LoggerTreeRoot { get; set; }

        // Other Tabs
        public ObservableCollection<EventEntry> Events { get; set; }
        public ObservableCollection<BitmapImage> Screenshots { get; set; }

        // General Collections
        public ObservableCollection<string> LoadedFiles { get; set; }
        public ObservableCollection<LogSessionData> LoadedSessions { get; set; }
        public ObservableCollection<SavedConfiguration> SavedConfigs { get; set; }
        public ObservableCollection<LogEntry> MarkedLogs { get; set; }
        public ObservableCollection<string> AvailableFonts { get; set; }
        public ObservableCollection<string> TimeUnits { get; } = new ObservableCollection<string> { "Seconds", "Minutes" };

        public event Action<LogEntry> RequestScrollToLog;

        // --- Properties ---
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
                    SwitchToSession(_selectedSession);
                }
            }
        }
        private bool _isMarkedLogsCombined;
        public bool IsMarkedLogsCombined
        {
            get => _isMarkedLogsCombined;
            set
            {
                if (_isMarkedLogsCombined != value)
                {
                    _isMarkedLogsCombined = value;
                    OnPropertyChanged();
                    // אופציונלי: סגירת חלונות קיימים בעת שינוי המצב כדי למנוע בלבול
                    CloseAllMarkedWindows();
                }
            }
        }
        public class GraphComponentItem : INotifyPropertyChanged
        {
            public string Name { get; set; }
            public string FullKey { get; set; }
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(); SelectionChanged?.Invoke(); }
            }
            public Action SelectionChanged { get; set; }
            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
      

        // משתנה לחלון המאוחד
        private MarkedLogsWindow _combinedMarkedWindow;
        private int _leftTabIndex;
        public int LeftTabIndex
        {
            get => _leftTabIndex;
            set { _leftTabIndex = value; OnPropertyChanged(); }
        }

        private string _windowTitle = "IndiLogs 3.0";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        private string _setupInfo;
        public string SetupInfo
        {
            get => _setupInfo;
            set { _setupInfo = value; OnPropertyChanged(); }
        }

        private string _pressConfig;
        public string PressConfig
        {
            get => _pressConfig;
            set { _pressConfig = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private double _currentProgress;
        public double CurrentProgress
        {
            get => _currentProgress;
            set { _currentProgress = value; OnPropertyChanged(); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private LogEntry _selectedLog;
        public LogEntry SelectedLog
        {
            get => _selectedLog;
            set { _selectedLog = value; OnPropertyChanged(); }
        }

        private SavedConfiguration _selectedConfig;
        public SavedConfiguration SelectedConfig
        {
            get => _selectedConfig;
            set { _selectedConfig = value; OnPropertyChanged(); }
        }

        // --- Search & Filter Properties ---
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
                    // Debounce
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        private bool _isFilterActive;
        public bool IsFilterActive
        {
            get
            {
                if (SelectedTabIndex == 2) return _isAppFilterActive;
                return _isMainFilterActive;
            }
            set
            {
                if (SelectedTabIndex == 2)
                {
                    if (_isAppFilterActive != value)
                    {
                        _isAppFilterActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (_isMainFilterActive != value)
                    {
                        _isMainFilterActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(_isMainFilterActive);
                    }
                }
            }
        }

        private bool _isFilterOutActive;
        public bool IsFilterOutActive
        {
            get
            {
                if (SelectedTabIndex == 2) return _isAppFilterOutActive;
                return _isMainFilterOutActive;
            }
            set
            {
                if (SelectedTabIndex == 2)
                {
                    if (_isAppFilterOutActive != value)
                    {
                        _isAppFilterOutActive = value;
                        OnPropertyChanged();
                        ApplyAppLogsFilter();
                    }
                }
                else
                {
                    if (_isMainFilterOutActive != value)
                    {
                        _isMainFilterOutActive = value;
                        OnPropertyChanged();
                        UpdateMainLogsFilter(_isMainFilterActive);
                    }
                }
            }
        }

        // --- Live Mode Properties ---
        private bool _isLiveMode;
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set { _isLiveMode = value; OnPropertyChanged(); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPaused)); }
        }
        public bool IsPaused => !IsRunning;

        // --- Fonts & UI Zoom ---
        private string _selectedFont = "Segoe UI";
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

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set { _isDarkMode = value; ApplyTheme(value); OnPropertyChanged(); }
        }

        private double _gridFontSize = 12;
        public double GridFontSize
        {
            get => _gridFontSize;
            set { _gridFontSize = value; OnPropertyChanged(); }
        }

        private double _screenshotZoom = 400;
        public double ScreenshotZoom
        {
            get => _screenshotZoom;
            set { _screenshotZoom = value; OnPropertyChanged(); }
        }

        private int _contextSeconds = 10;
        public int ContextSeconds
        {
            get => _contextSeconds;
            set { if (_contextSeconds != value) { _contextSeconds = value; OnPropertyChanged(); } }
        }

        private string _selectedTimeUnit = "Seconds";
        public string SelectedTimeUnit
        {
            get => _selectedTimeUnit;
            set { _selectedTimeUnit = value; OnPropertyChanged(); }
        }

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
        public ICommand ExportParsedDataCommand { get; }
        public ICommand RunAnalysisCommand { get; }
        public ICommand FilterToStateCommand { get; }
        public ICommand OpenStatesWindowCommand { get; }

        // Live Commands
        public ICommand LivePlayCommand { get; }
        public ICommand LivePauseCommand { get; }
        public ICommand LiveClearCommand { get; }

        // Tree Commands (NEW)
        public ICommand TreeShowThisCommand { get; }            // 1
        public ICommand TreeHideThisCommand { get; }            // 2
        public ICommand TreeShowOnlyThisCommand { get; }        // 3
        public ICommand TreeShowWithChildrenCommand { get; }    // 4
        public ICommand TreeHideWithChildrenCommand { get; }    // 5
        public ICommand TreeShowAllCommand { get; }             // Reset

        public MainViewModel()
        {
            _csvService = new CsvExportService();
            _logService = new LogFileService();
            _coloringService = new LogColoringService();
            GraphsVM = new GraphsViewModel();
            // Tree Commands Initialization
            TreeShowThisCommand = new RelayCommand(ExecuteTreeShowThis);
            TreeHideThisCommand = new RelayCommand(ExecuteTreeHideThis);
            TreeShowOnlyThisCommand = new RelayCommand(ExecuteTreeShowOnlyThis);
            TreeShowWithChildrenCommand = new RelayCommand(ExecuteTreeShowWithChildren);
            TreeHideWithChildrenCommand = new RelayCommand(ExecuteTreeHideWithChildren);
            TreeShowAllCommand = new RelayCommand(ExecuteTreeShowAll);

            _allLogsCache = new List<LogEntry>();
            Logs = new List<LogEntry>();
            LoadedSessions = new ObservableCollection<LogSessionData>();
            FilteredLogs = new ObservableRangeCollection<LogEntry>();

            // NEW Collections
            AppDevLogsFiltered = new ObservableRangeCollection<LogEntry>();
            LoggerTreeRoot = new ObservableCollection<LoggerNode>();

            Events = new ObservableCollection<EventEntry>();
            Screenshots = new ObservableCollection<BitmapImage>();
            LoadedFiles = new ObservableCollection<string>();
            SavedConfigs = new ObservableCollection<SavedConfiguration>();
            MarkedLogs = new ObservableCollection<LogEntry>();
            MarkedAppLogs = new ObservableCollection<LogEntry>();
            AvailableFonts = new ObservableCollection<string>();
            if (Fonts.SystemFontFamilies != null)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) AvailableFonts.Add(font.Source);

            // Timer Initialization
            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += OnSearchTimerTick;

            // Command Initialization
            LoadCommand = new RelayCommand(LoadFile);
            ClearCommand = new RelayCommand(ClearLogs);
            MarkRowCommand = new RelayCommand(MarkRow);
            NextMarkedCommand = new RelayCommand(GoToNextMarked);
            PrevMarkedCommand = new RelayCommand(GoToPrevMarked);
            JumpToLogCommand = new RelayCommand(JumpToLog);

            OpenJiraCommand = new RelayCommand(o => OpenUrl("https://hp-jira.external.hp.com/secure/Dashboard.jspa"));
            OpenKibanaCommand = new RelayCommand(OpenKibana);
            OpenOutlookCommand = new RelayCommand(OpenOutlook);
            OpenGraphViewerCommand = new RelayCommand(OpenGraphViewer);
            OpenStatesWindowCommand = new RelayCommand(OpenStatesWindow);

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

            ExportParsedDataCommand = new RelayCommand(ExportParsedData);
            RunAnalysisCommand = new RelayCommand(RunAnalysis);
            FilterToStateCommand = new RelayCommand(FilterToState);

            ZoomInCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == 4)
                    ScreenshotZoom = Math.Min(5000, ScreenshotZoom + 100);
                else
                    GridFontSize = Math.Min(30, GridFontSize + 1);
            });
            ZoomOutCommand = new RelayCommand(o =>
            {
                if (SelectedTabIndex == 4)
                    ScreenshotZoom = Math.Max(100, ScreenshotZoom - 100);
                else
                    GridFontSize = Math.Max(8, GridFontSize - 1);
            });

            LivePlayCommand = new RelayCommand(LivePlay);
            LivePauseCommand = new RelayCommand(LivePause);
            LiveClearCommand = new RelayCommand(LiveClear);

            ApplyTheme(false);
            LoadSavedConfigurations();
        }

        private void OnSearchTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ToggleFilterView(IsFilterActive);
        }

        // --- SESSION SWITCHING ---

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    if (_selectedTabIndex == 2)
                    {
                        LeftTabIndex = 1; // LOGGERS
                    }
                    else if (_selectedTabIndex == 0 || _selectedTabIndex == 1)
                    {
                        LeftTabIndex = 0; // EXPLORER
                    }

                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                }
            }
        }

        private void SwitchToSession(LogSessionData session)
        {
            _isMainFilterActive = false;
            _isAppFilterActive = false;
            _isMainFilterOutActive = false;
            _isAppFilterOutActive = false;
            if (session == null) return;
            IsBusy = true;

            // 1. Update Main Logs
            _allLogsCache = session.Logs;
            Logs = session.Logs;

            var defaultFilteredLogs = session.Logs.Where(l => IsDefaultLog(l)).ToList();
            FilteredLogs.ReplaceAll(defaultFilteredLogs);
            if (FilteredLogs.Count > 0) SelectedLog = FilteredLogs[0];

            // 2. Update Other Data
            Events = new ObservableCollection<EventEntry>(session.Events); OnPropertyChanged(nameof(Events));
            Screenshots = new ObservableCollection<BitmapImage>(session.Screenshots); OnPropertyChanged(nameof(Screenshots));
            MarkedLogs = session.MarkedLogs; OnPropertyChanged(nameof(MarkedLogs));
            SetupInfo = session.SetupInfo;
            PressConfig = session.PressConfiguration;

            if (!string.IsNullOrEmpty(session.VersionsInfo))
                WindowTitle = $"IndiLogs 3.0 - {session.FileName} ({session.VersionsInfo})";
            else
                WindowTitle = $"IndiLogs 3.0 - {session.FileName}";

            // 3. Update AppDev Logs & Tree
            _allAppLogsCache = session.AppDevLogs ?? new List<LogEntry>();
            BuildLoggerTree(_allAppLogsCache);

            SearchText = "";
            IsFilterActive = false;
            IsFilterOutActive = false;
            _isTimeFocusActive = false;
            _isAppTimeFocusActive = false;

            _negativeFilters.Clear();
            _activeThreadFilters.Clear();

            _mainFilterRoot = null;
            _appFilterRoot = null;
            _lastFilteredAppCache = null;
            _lastFilteredCache.Clear();

            // איפוס עץ
            ResetTreeFilters();

            ApplyAppLogsFilter();
            IsBusy = false;
        }

        // --- TREE BUILDING & COMMANDS ---

        private void BuildLoggerTree(IEnumerable<LogEntry> logs)
        {
            LoggerTreeRoot.Clear();
            if (logs == null || !logs.Any()) return;

            int totalCount = logs.Count();
            var rootNode = new LoggerNode { Name = "All Loggers", FullPath = "", IsExpanded = true, Count = totalCount };

            var loggerGroups = logs.GroupBy(l => l.Logger)
                                   .Select(g => new { Name = g.Key, Count = g.Count() })
                                   .ToList();

            foreach (var group in loggerGroups)
            {
                if (string.IsNullOrEmpty(group.Name)) continue;
                var parts = group.Name.Split('.');
                AddNodeRecursive(rootNode, parts, 0, "", group.Count);
            }

            foreach (var child in rootNode.Children)
            {
                LoggerTreeRoot.Add(child);
            }
        }

        private void AddNodeRecursive(LoggerNode parent, string[] parts, int index, string currentPath, int count)
        {
            if (index >= parts.Length) return;

            string part = parts[index];
            string newPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";

            var child = parent.Children.FirstOrDefault(c => c.Name == part);
            if (child == null)
            {
                child = new LoggerNode { Name = part, FullPath = newPath };
                int insertIdx = 0;
                while (insertIdx < parent.Children.Count && string.Compare(parent.Children[insertIdx].Name, part) < 0)
                    insertIdx++;
                parent.Children.Insert(insertIdx, child);
            }

            child.Count += count;
            AddNodeRecursive(child, parts, index + 1, newPath, count);
        }

        // 1. Show This Logger
        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;

                _treeHiddenLoggers.Remove(node.FullPath);
                node.IsHidden = false;

                // עדכון פילטר אוטומטי (App)
                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }

        // 2. Hide This Logger
        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;

                _treeHiddenLoggers.Add(node.FullPath);
                node.IsHidden = true;

                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }

        // 3. Show Only This Logger (Isolate)
        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                _treeShowOnlyLogger = node.FullPath;

                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }

        // 4. Show With Children (Branch Isolate)
        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                ResetTreeFilters();
                _treeShowOnlyPrefix = node.FullPath;

                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }

        // 5. Hide With Children (Branch Hide)
        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                _treeShowOnlyLogger = null;
                _treeShowOnlyPrefix = null;

                _treeHiddenPrefixes.Add(node.FullPath);
                node.IsHidden = true; // Visual indicator on parent

                _isAppFilterActive = true;
                OnPropertyChanged(nameof(IsFilterActive));
                ToggleFilterView(true);
            }
        }

        private void ExecuteTreeShowAll(object obj)
        {
            ResetTreeFilters();
            _isAppFilterActive = false; // Reset filter state
            OnPropertyChanged(nameof(IsFilterActive));
            ToggleFilterView(false);
        }

        private void ResetTreeFilters()
        {
            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;
            foreach (var node in LoggerTreeRoot) ResetVisualHiddenState(node);
        }

        private void ResetVisualHiddenState(LoggerNode node)
        {
            node.IsHidden = false;
            foreach (var child in node.Children) ResetVisualHiddenState(child);
        }

        private void ViewLogDetails(object parameter)
        {
            if (parameter is LogEntry log) new LogDetailsWindow(log).Show();
        }

        public void HandleExternalArguments(string[] args)
        {
            if (args != null && args.Length > 0) OnFilesDropped(args);
        }

        public async void SortAppLogs(string sortBy, bool ascending)
        {
            if (AppDevLogsFiltered == null || AppDevLogsFiltered.Count == 0) return;

            IsBusy = true;
            StatusMessage = "Sorting...";

            await Task.Run(() =>
            {
                List<LogEntry> sorted = null;
                var source = AppDevLogsFiltered.ToList();

                switch (sortBy)
                {
                    case "Time": sorted = ascending ? source.OrderBy(x => x.Date).ToList() : source.OrderByDescending(x => x.Date).ToList(); break;
                    case "Level": sorted = ascending ? source.OrderBy(x => x.Level).ToList() : source.OrderByDescending(x => x.Level).ToList(); break;
                    case "Logger": sorted = ascending ? source.OrderBy(x => x.Logger).ToList() : source.OrderByDescending(x => x.Logger).ToList(); break;
                    case "Thread": sorted = ascending ? source.OrderBy(x => x.ThreadName).ToList() : source.OrderByDescending(x => x.ThreadName).ToList(); break;
                    default: sorted = source; break;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppDevLogsFiltered.ReplaceAll(sorted);
                    IsBusy = false;
                    StatusMessage = "Sorted.";
                });
            });
        }

        // --- UNIFIED FILTERING LOGIC ---

        private void ToggleFilterView(bool show)
        {
            UpdateMainLogsFilter(show);
            ApplyAppLogsFilter();
        }

        private void UpdateMainLogsFilter(bool show)
        {
            bool isActive = _isMainFilterActive;
            IEnumerable<LogEntry> currentLogs;
            bool hasSearchText = !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2;

            if (isActive || hasSearchText)
            {
                if ((_mainFilterRoot != null && _mainFilterRoot.Children != null && _mainFilterRoot.Children.Count > 0) || _isTimeFocusActive)
                    currentLogs = _lastFilteredCache ?? new List<LogEntry>();
                else
                    currentLogs = _allLogsCache;

                if (_activeThreadFilters.Any())
                    currentLogs = currentLogs.Where(l => _activeThreadFilters.Contains(l.ThreadName));

                if (hasSearchText)
                    currentLogs = currentLogs.Where(l => l.Message != null && l.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            else
            {
                currentLogs = _allLogsCache;
            }

            if (_isMainFilterOutActive && _negativeFilters.Any())
            {
                currentLogs = currentLogs.Where(l =>
                {
                    foreach (var f in _negativeFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            if (l.ThreadName != null && l.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0) return false;
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

        private void ApplyAppLogsFilter()
        {
            if (_allAppLogsCache == null) return;

            if (!_isAppFilterActive && string.IsNullOrWhiteSpace(SearchText))
            {
                AppDevLogsFiltered.ReplaceAll(_allAppLogsCache);
                return;
            }

            var source = _allAppLogsCache;
            if (_isAppFilterActive && _isAppTimeFocusActive && _lastFilteredAppCache != null)
            {
                source = _lastFilteredAppCache;
            }

            var query = source.AsParallel().AsOrdered();

            // 1. Advanced Filter
            if (_isAppFilterActive && !_isAppTimeFocusActive && _appFilterRoot != null && _appFilterRoot.Children.Count > 0)
            {
                query = query.Where(l => EvaluateFilterNode(l, _appFilterRoot));
            }

            // 2. Tree Filter (Logic Updated)
            if (_isAppFilterActive)
            {
                if (_treeShowOnlyLogger != null)
                {
                    query = query.Where(l => l.Logger == _treeShowOnlyLogger);
                }
                else if (_treeShowOnlyPrefix != null)
                {
                    query = query.Where(l => l.Logger != null && (l.Logger == _treeShowOnlyPrefix || l.Logger.StartsWith(_treeShowOnlyPrefix + ".")));
                }
                else
                {
                    if (_treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0)
                    {
                        query = query.Where(l =>
                        {
                            if (l.Logger == null) return true;
                            if (_treeHiddenLoggers.Contains(l.Logger)) return false;

                            foreach (var prefix in _treeHiddenPrefixes)
                            {
                                if (l.Logger == prefix || l.Logger.StartsWith(prefix + ".")) return false;
                            }
                            return true;
                        });
                    }
                }
            }

            // 3. Search
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText;
                query = query.Where(l => l.Message != null && l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // 4. Negative Filter
            if (_isAppFilterOutActive && _negativeFilters.Any())
            {
                var negFilters = _negativeFilters.ToList();
                query = query.Where(l =>
                {
                    foreach (var f in negFilters)
                    {
                        if (f.StartsWith("THREAD:"))
                        {
                            if (l.ThreadName != null && l.ThreadName.IndexOf(f.Substring(7), StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                        else
                        {
                            if (l.Message != null && l.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                        }
                    }
                    return true;
                });
            }

            var resultList = query.ToList();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppDevLogsFiltered.ReplaceAll(resultList);
            });
        }

        // --- LIVE MONITORING ---

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
            _liveCts = new CancellationTokenSource();
            Task.Run(() => PollingLoop(_liveCts.Token));
        }

        private void StopLiveMonitoring()
        {
            if (_liveCts != null) { _liveCts.Cancel(); _liveCts = null; }
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
                        if (currentLen > _lastKnownFileSize) await FetchNewData(currentLen);
                        else if (currentLen < _lastKnownFileSize) { _lastKnownFileSize = 0; _totalLogsReadFromFile = 0; }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Polling Error: {ex.Message}"); }
                try { await Task.Delay(POLLING_INTERVAL_MS, token); }
                catch (TaskCanceledException) { break; }
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
                await _coloringService.ApplyDefaultColorsAsync(newItems, false);
                if (_savedColoringRules.Count > 0)
                    await _coloringService.ApplyCustomColoringAsync(newItems, _savedColoringRules);
                newItems.Reverse();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _liveLogsCollection.InsertRange(0, newItems);
                    List<LogEntry> itemsForFilteredTab = newItems.Where(IsDefaultLog).ToList();
                    if (itemsForFilteredTab != null && itemsForFilteredTab.Count > 0)
                    {
                        FilteredLogs.InsertRange(0, itemsForFilteredTab);
                        SelectedLog = FilteredLogs[0];
                    }
                });
            }
        }

        private void LivePlay(object obj) { IsRunning = true; StatusMessage = "Live monitoring active."; }
        private void LivePause(object obj) { IsRunning = false; StatusMessage = "Live monitoring paused."; }
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
                        using (var ms = new MemoryStream()) { await fs.CopyToAsync(ms); ms.Position = 0; var logs = _logService.ParseLogStream(ms); _totalLogsReadFromFile = logs.Count; _lastKnownFileSize = fs.Length; }
                    }
                }
                catch { }
            });
            StatusMessage = "Cleared. Press Play to resume from now.";
        }

        // --- FILE LOADING ---

        private void LoadFile(object obj)
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "All Supported|*.zip;*.log|Log Files (*.log)|*.log|Log Archives (*.zip)|*.zip|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true) ProcessFiles(dialog.FileNames);
        }

        // בתוך הקובץ ViewModels/MainViewModel.cs

        private async void ProcessFiles(string[] filePaths)
        {
            StopLiveMonitoring();
            IsBusy = true;
            StatusMessage = "Processing files...";

            try
            {
                var progress = new Progress<(double Percent, string Message)>(update =>
                {
                    CurrentProgress = update.Percent;
                    StatusMessage = update.Message;
                });

                var newSession = await _logService.LoadSessionAsync(filePaths, progress);

                newSession.FileName = System.IO.Path.GetFileName(filePaths[0]);
                if (filePaths.Length > 1)
                    newSession.FileName += $" (+{filePaths.Length - 1})";
                newSession.FilePath = filePaths[0];

                StatusMessage = "Applying Colors...";

                // צביעה
                await _coloringService.ApplyDefaultColorsAsync(newSession.Logs, false);
                if (newSession.AppDevLogs != null && newSession.AppDevLogs.Any())
                    await _coloringService.ApplyDefaultColorsAsync(newSession.AppDevLogs, true);

                // --- לוגיקת הגרפים ---
                // אנחנו לא שומרים את התוצאה כאן, אלא נותנים ל-GraphsVM לטפל בזה
                if (newSession.Logs != null && newSession.Logs.Any())
                {
                    // קריאה אסינכרונית ללא המתנה (Fire and Forget) כדי לא לתקוע את ה-UI
                    _ = GraphsVM.ProcessLogsAsync(newSession.Logs);
                }

                LoadedSessions.Add(newSession);
                SelectedSession = newSession;

                CurrentProgress = 100;
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ClearLogs(object obj)
        {
            MarkedLogs.Clear(); MarkedAppLogs.Clear();
            _isMainFilterActive = false; _isAppFilterActive = false;
            _isMainFilterOutActive = false; _isAppFilterOutActive = false;
            _isAppTimeFocusActive = false; _lastFilteredAppCache = null;
            _isTimeFocusActive = false;
            if (_allLogsCache != null) _allLogsCache.Clear();
            _lastFilteredCache.Clear(); _negativeFilters.Clear(); _activeThreadFilters.Clear();
            Logs = new List<LogEntry>(); FilteredLogs.Clear(); AppDevLogsFiltered.Clear();
            LoggerTreeRoot.Clear(); Events.Clear(); Screenshots.Clear();
            LoadedFiles.Clear(); CurrentProgress = 0; SetupInfo = ""; PressConfig = ""; ScreenshotZoom = 400;
            IsFilterOutActive = false; LoadedSessions.Clear(); SelectedSession = null;
            _allAppLogsCache = null;
            ResetTreeFilters();
        }

        // --- FILTER & ANALYSIS COMMANDS ---

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

        private void FilterOutThread(object obj)
        {
            if (SelectedLog == null || string.IsNullOrEmpty(SelectedLog.ThreadName)) return;
            var win = new FilterOutWindow(SelectedLog.ThreadName);
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.TextToRemove))
            {
                string filterKey = "THREAD:" + win.TextToRemove;
                if (!_negativeFilters.Contains(filterKey))
                {
                    _negativeFilters.Add(filterKey);
                    IsFilterOutActive = true;
                    ToggleFilterView(IsFilterActive);
                }
            }
        }

        private void OpenThreadFilter(object obj)
        {
            if (_allLogsCache == null || !_allLogsCache.Any()) return;
            var threads = _allLogsCache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            var win = new ThreadFilterWindow(threads);
            if (win.ShowDialog() == true)
            {
                if (win.ShouldClear) { _activeThreadFilters.Clear(); if (_savedFilterRoot == null) IsFilterActive = false; }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any()) { _activeThreadFilters = win.SelectedThreads; IsFilterActive = true; }
                ToggleFilterView(IsFilterActive);
            }
        }

        private async void OpenFilterWindow(object obj)
        {
            var win = new FilterWindow();
            bool isAppTab = SelectedTabIndex == 2;
            var currentRoot = isAppTab ? _appFilterRoot : _mainFilterRoot;

            if (currentRoot != null) { win.ViewModel.RootNodes.Clear(); win.ViewModel.RootNodes.Add(currentRoot.DeepClone()); }

            if (win.ShowDialog() == true)
            {
                var newRoot = win.ViewModel.RootNodes.FirstOrDefault();
                bool hasAdvanced = newRoot != null && newRoot.Children.Count > 0;
                IsBusy = true;
                await Task.Run(() =>
                {
                    if (isAppTab) _appFilterRoot = newRoot;
                    else
                    {
                        _mainFilterRoot = newRoot;
                        if (hasAdvanced) { var res = _allLogsCache.Where(l => EvaluateFilterNode(l, _mainFilterRoot)).ToList(); _lastFilteredCache = res; }
                        else _lastFilteredCache.Clear();
                    }
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isAppTab) { _isAppFilterActive = hasAdvanced; ApplyAppLogsFilter(); }
                    else { _isMainFilterActive = hasAdvanced || _activeThreadFilters.Any(); UpdateMainLogsFilter(_isMainFilterActive); }
                    OnPropertyChanged(nameof(IsFilterActive));
                    IsBusy = false;
                });
            }
        }

        private bool EvaluateFilterNode(LogEntry log, FilterNode node)
        {
            if (node == null) return true;
            if (node.Type == NodeType.Condition)
            {
                string val = "";
                switch (node.Field)
                {
                    case "Level": val = log.Level; break;
                    case "ThreadName": val = log.ThreadName; break;
                    case "Logger": val = log.Logger; break;
                    case "ProcessName": val = log.ProcessName; break;
                    default: val = log.Message; break;
                }
                if (string.IsNullOrEmpty(val)) return false;
                string op = node.Operator;
                string criteria = node.Value;
                if (op == "Equals") return val.Equals(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Begins With") return val.StartsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Ends With") return val.EndsWith(criteria, StringComparison.OrdinalIgnoreCase);
                if (op == "Regex") { try { return System.Text.RegularExpressions.Regex.IsMatch(val, criteria, System.Text.RegularExpressions.RegexOptions.IgnoreCase); } catch { return false; } }
                return val.IndexOf(criteria, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else
            {
                if (node.Children == null || node.Children.Count == 0) return true;
                string op = node.LogicalOperator;
                bool isBaseOr = op.Contains("OR");
                bool baseResult;
                if (isBaseOr)
                {
                    baseResult = false;
                    foreach (var child in node.Children) { if (EvaluateFilterNode(log, child)) { baseResult = true; break; } }
                }
                else
                {
                    baseResult = true;
                    foreach (var child in node.Children) { if (!EvaluateFilterNode(log, child)) { baseResult = false; break; } }
                }
                if (op.StartsWith("NOT")) return !baseResult;
                return baseResult;
            }
        }

        private void FilterContext(object obj)
        {
            if (SelectedLog == null) return;
            IsBusy = true;
            double multiplier = SelectedTimeUnit == "Minutes" ? 60 : 1;
            double rangeInSeconds = ContextSeconds * multiplier;
            DateTime targetTime = SelectedLog.Date;
            DateTime startTime = targetTime.AddSeconds(-rangeInSeconds);
            DateTime endTime = targetTime.AddSeconds(rangeInSeconds);
            bool isAppTab = SelectedTabIndex == 2;

            Task.Run(() =>
            {
                if (isAppTab)
                {
                    if (_allAppLogsCache != null)
                    {
                        var contextLogs = _allAppLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() => { _lastFilteredAppCache = contextLogs; _isAppTimeFocusActive = true; _appFilterRoot = null; IsFilterActive = true; ToggleFilterView(true); StatusMessage = $"APP Focus Time: {contextLogs.Count} logs shown"; IsBusy = false; });
                    }
                }
                else
                {
                    if (_allLogsCache != null)
                    {
                        var contextLogs = _allLogsCache.Where(l => l.Date >= startTime && l.Date <= endTime).OrderByDescending(l => l.Date).ToList();
                        Application.Current.Dispatcher.Invoke(() => { _lastFilteredCache = contextLogs; _savedFilterRoot = null; _isTimeFocusActive = true; IsFilterActive = true; ToggleFilterView(true); StatusMessage = $"Focus Time: +/- {rangeInSeconds}s | {contextLogs.Count} logs shown"; IsBusy = false; });
                    }
                }
            });
        }
        private void UndoFilterOut(object parameter) { }

        // --- EXPORT & ANALYSIS ---
        private async void ExportParsedData(object obj)
        {
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any()) { MessageBox.Show("No logs loaded.", "Info"); return; }
            IsBusy = true; StatusMessage = "Parsing and Exporting CSV...";
            await _csvService.ExportLogsToCsvAsync(SelectedSession.Logs, SelectedSession.FileName);
            IsBusy = false; StatusMessage = "Ready";
        }

        private void RunAnalysis(object obj)
        {
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any()) { MessageBox.Show("No logs loaded.", "Info"); return; }
            if (_analysisWindow != null && _analysisWindow.IsVisible) { _analysisWindow.Activate(); return; }
            if (SelectedSession.CachedAnalysis != null && SelectedSession.CachedAnalysis.Any()) { OpenAnalysisWindow(SelectedSession.CachedAnalysis); return; }

            IsBusy = true; StatusMessage = "Initializing Analysis...";
            var logsToAnalyze = SelectedSession.Logs.ToList();
            Task.Run(() =>
            {
                try
                {
                    var allResults = new List<AnalysisResult>();
                    ReportProgress(10, "Running Mechanit Analyzer...");
                    var mechResults = new MechanitAnalyzer().Analyze(logsToAnalyze);
                    if (mechResults != null) allResults.AddRange(mechResults);

                    ReportProgress(50, "Running GetReady Analyzer...");
                    var grResults = new GetReadyAnalyzer().Analyze(logsToAnalyze);
                    if (grResults != null) allResults.AddRange(grResults);

                    ReportProgress(90, "Finalizing Report...");
                    Application.Current.Dispatcher.Invoke(() => { IsBusy = false; StatusMessage = "Ready"; if (allResults.Count == 0) MessageBox.Show("No processes found."); else { SelectedSession.CachedAnalysis = allResults; OpenAnalysisWindow(allResults); } });
                }
                catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => { IsBusy = false; MessageBox.Show($"TASK ERROR:\n{ex.Message}"); }); }
            });
        }

        private void OpenAnalysisWindow(List<AnalysisResult> results)
        {
            _analysisWindow = new AnalysisReportWindow(results);
            _analysisWindow.Owner = Application.Current.MainWindow;
            _analysisWindow.Closed += (s, e) => _analysisWindow = null;
            _analysisWindow.Show();
        }

        private void ReportProgress(double percent, string msg) { Application.Current.Dispatcher.Invoke(() => { CurrentProgress = percent; StatusMessage = msg; }); }

        private void OpenStatesWindow(object obj)
        {
            if (_statesWindow != null && _statesWindow.IsVisible) { _statesWindow.Activate(); return; }
            if (SelectedSession == null || SelectedSession.Logs == null || !SelectedSession.Logs.Any()) { MessageBox.Show("No logs loaded."); return; }
            if (SelectedSession.CachedStates != null && SelectedSession.CachedStates.Count > 0)
            {
                _statesWindow = new StatesWindow(SelectedSession.CachedStates, this);
                _statesWindow.Owner = Application.Current.MainWindow;
                _statesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _statesWindow.Closed += (s, e) => _statesWindow = null;
                _statesWindow.Show();
                return;
            }

            IsBusy = true; StatusMessage = "Analyzing States...";
            var logsToProcess = SelectedSession.Logs.ToList();
            Task.Run(() =>
            {
                var statesList = new List<StateEntry>();
                var transitionLogs = logsToProcess.Where(l => l.ThreadName != null && l.ThreadName.Equals("Manager", StringComparison.OrdinalIgnoreCase) && l.Message != null && l.Message.IndexOf("PlcMngr:", StringComparison.OrdinalIgnoreCase) >= 0 && l.Message.Contains("->")).OrderBy(l => l.Date).ToList();

                if (transitionLogs.Count == 0) { Application.Current.Dispatcher.Invoke(() => { IsBusy = false; StatusMessage = "Ready"; MessageBox.Show("No state transitions found."); }); return; }

                DateTime logEndLimit = logsToProcess.Last().Date;
                for (int i = 0; i < transitionLogs.Count; i++)
                {
                    var currentLog = transitionLogs[i];
                    var parts = currentLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                    if (parts.Length < 2) continue;
                    string fromStateRaw = parts[0].Replace("PlcMngr:", "").Trim();
                    string toStateRaw = parts[1].Trim();
                    var entry = new StateEntry { StateName = toStateRaw, TransitionTitle = $"{fromStateRaw} -> {toStateRaw}", StartTime = currentLog.Date, LogReference = currentLog, Status = "", StatusColor = Brushes.Gray };
                    if (i < transitionLogs.Count - 1)
                    {
                        var nextLog = transitionLogs[i + 1];
                        entry.EndTime = nextLog.Date;
                        var nextParts = nextLog.Message.Split(new[] { "->" }, StringSplitOptions.None);
                        if (nextParts.Length >= 2)
                        {
                            string nextDestination = nextParts[1].Trim();
                            if (entry.StateName.Equals("GET_READY", StringComparison.OrdinalIgnoreCase)) { if (nextDestination.Equals("DYNAMIC_READY", StringComparison.OrdinalIgnoreCase)) { entry.Status = "SUCCESS"; entry.StatusColor = Brushes.LightGreen; } else { entry.Status = "FAILED"; entry.StatusColor = Brushes.Red; } }
                            else if (entry.StateName.Equals("MECH_INIT", StringComparison.OrdinalIgnoreCase)) { if (nextDestination.Equals("STANDBY", StringComparison.OrdinalIgnoreCase)) { entry.Status = "SUCCESS"; entry.StatusColor = Brushes.LightGreen; } else { entry.Status = "FAILED"; entry.StatusColor = Brushes.Red; } }
                        }
                    }
                    else { entry.EndTime = logEndLimit; entry.Status = "Current"; }
                    statesList.Add(entry);
                }
                var displayList = statesList.OrderByDescending(s => s.StartTime).ToList();
                Application.Current.Dispatcher.Invoke(() => { IsBusy = false; StatusMessage = "Ready"; SelectedSession.CachedStates = displayList; _statesWindow = new StatesWindow(displayList, this); _statesWindow.Owner = Application.Current.MainWindow; _statesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; _statesWindow.Closed += (s, e) => _statesWindow = null; _statesWindow.Show(); });
            });
        }

        private void FilterToState(object obj)
        {
            if (obj is StateEntry state)
            {
                IsBusy = true;
                StatusMessage = $"Focusing state: {state.StateName}...";

                Task.Run(() =>
                {
                    DateTime start = state.StartTime;
                    DateTime end = state.EndTime ?? DateTime.MaxValue;

                    // --- זה החלק שהיה חסר: קריאה ל-GraphsVM לעדכון הגרף ---
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (GraphsVM != null)
                        {
                            // שימוש בפונקציה החדשה שיצרנו
                            GraphsVM.SetTimeRange(start, end);
                        }
                    });
                    // -----------------------------------------------------

                    if (_allLogsCache != null)
                    {
                        var timeSlice = _allLogsCache.Where(l => l.Date >= start && l.Date <= end).OrderByDescending(l => l.Date).ToList();
                        var smartFiltered = timeSlice.Where(l => IsDefaultLog(l)).ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _lastFilteredCache = timeSlice;
                            _savedFilterRoot = null;
                            _isTimeFocusActive = true;
                            _isMainFilterActive = true;
                            SelectedTabIndex = 0;
                            UpdateMainLogsFilter(true);
                            if (FilteredLogs != null)
                            {
                                FilteredLogs.ReplaceAll(smartFiltered);
                                if (FilteredLogs.Count > 0) SelectedLog = FilteredLogs[0];
                            }
                            OnPropertyChanged(nameof(IsFilterActive));
                            StatusMessage = $"State: {state.StateName} | Main: {timeSlice.Count}, Filtered: {smartFiltered.Count}";
                            IsBusy = false;
                        });
                    }
                    else
                    {
                        IsBusy = false;
                    }
                });
            }
        }

        // --- CONFIG & UI HELPERS ---

        private void LoadSavedConfigurations()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "Configs");
            if (Directory.Exists(path))
                foreach (var f in Directory.GetFiles(path, "*.json")) { try { var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(f)); c.FilePath = f; SavedConfigs.Add(c); } catch { } }
        }

        private async void ApplyConfiguration(object parameter)
        {
            if (parameter is SavedConfiguration c)
            {
                IsBusy = true;
                StatusMessage = $"Loading config: {c.Name} (Overriding current state)...";

                // ============================================================
                // 1. ניקוי מלא (Clean Slate) - מאפסים הכל לפני הטעינה
                // ============================================================
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // איפוס חיפוש
                    SearchText = "";
                    IsSearchPanelVisible = false;

                    // איפוס פילטרים ידניים (Thread, Filter Out)
                    _negativeFilters.Clear();
                    _activeThreadFilters.Clear();

                    // איפוס פוקוס זמן
                    _isTimeFocusActive = false;
                    _isAppTimeFocusActive = false;

                    // איפוס עץ (Tree Isolation)
                    ResetTreeFilters();

                    // איפוס מטמונים (Caches) של סינון קודם
                    _lastFilteredCache.Clear();
                    _lastFilteredAppCache = null;

                    // כיבוי דגלים זמני (נדליק מחדש לפי הצורך בסוף)
                    _isMainFilterActive = false;
                    _isAppFilterActive = false;
                    _isMainFilterOutActive = false;
                    _isAppFilterOutActive = false;

                    // עדכון כפתורים ב-UI
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                });

                // ============================================================
                // 2. טעינת חוקי צביעה (Background)
                // ============================================================
                await Task.Run(async () =>
                {
                    // טעינת חוקים ל-MAIN
                    // אם הרשימה בקובץ ריקה, נאתחל לרשימה ריקה (כדי למחוק חוקים קודמים)
                    _mainColoringRules = c.MainColoringRules ?? new List<ColoringCondition>();
                    if (_allLogsCache != null)
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_allLogsCache, false);
                        if (_mainColoringRules.Any())
                            await _coloringService.ApplyCustomColoringAsync(_allLogsCache, _mainColoringRules);
                    }

                    // טעינת חוקים ל-APP
                    _appColoringRules = c.AppColoringRules ?? new List<ColoringCondition>();
                    if (_allAppLogsCache != null)
                    {
                        await _coloringService.ApplyDefaultColorsAsync(_allAppLogsCache, true);
                        if (_appColoringRules.Any())
                            await _coloringService.ApplyCustomColoringAsync(_allAppLogsCache, _appColoringRules);
                    }
                });

                // ============================================================
                // 3. טעינת פילטרים מתקדמים (Logic)
                // ============================================================

                // טעינת עץ פילטר ראשי
                _mainFilterRoot = c.MainFilterRoot;
                if (_mainFilterRoot != null && _allLogsCache != null)
                {
                    // חישוב מחדש של הפילטר הראשי
                    var res = await Task.Run(() => _allLogsCache.Where(l => EvaluateFilterNode(l, _mainFilterRoot)).ToList());
                    _lastFilteredCache = res;
                }

                // טעינת עץ פילטר אפליקציה
                _appFilterRoot = c.AppFilterRoot;

                // ============================================================
                // 4. רענון UI סופי
                // ============================================================
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // קביעת הדגלים ע"פ מה שנטען מהקובץ בלבד
                    if (_appFilterRoot != null && _appFilterRoot.Children.Count > 0)
                        _isAppFilterActive = true;

                    if (_mainFilterRoot != null && _mainFilterRoot.Children.Count > 0)
                        _isMainFilterActive = true;

                    // רענון צבעים בטבלאות (Visual Refresh)
                    if (Logs != null) foreach (var log in Logs) log.OnPropertyChanged("RowBackground");
                    if (AppDevLogsFiltered != null) foreach (var log in AppDevLogsFiltered) log.OnPropertyChanged("RowBackground");

                    // הפעלת הפילטרים החדשים בפועל
                    UpdateMainLogsFilter(_isMainFilterActive);
                    ApplyAppLogsFilter();

                    // עדכון מצב הכפתורים
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterOutActive));
                });

                IsBusy = false;
                StatusMessage = "Configuration loaded successfully.";
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
                var cfg = new SavedConfiguration { Name = dlg.ConfigName, CreatedDate = DateTime.Now, FilePath = Path.Combine(dir, dlg.ConfigName + ".json"), MainColoringRules = _mainColoringRules ?? new List<ColoringCondition>(), MainFilterRoot = _mainFilterRoot, AppColoringRules = _appColoringRules ?? new List<ColoringCondition>(), AppFilterRoot = _appFilterRoot };
                File.WriteAllText(cfg.FilePath, JsonConvert.SerializeObject(cfg)); SavedConfigs.Add(cfg);
            }
        }
        private void LoadConfigurationFromFile(object obj)
        {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true) { try { var c = JsonConvert.DeserializeObject<SavedConfiguration>(File.ReadAllText(dlg.FileName)); c.FilePath = dlg.FileName; SavedConfigs.Add(c); } catch { } }
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

        private void UpdateResource(ResourceDictionary dict, string key, object value) { if (dict.Contains(key)) dict.Remove(key); dict.Add(key, value); }

        private async void OpenColoringWindow(object obj)
        {
            try
            {
                var win = new ColoringWindow();
                bool isAppTab = SelectedTabIndex == 2;
                var currentRulesSource = isAppTab ? _appColoringRules : _mainColoringRules;
                var rulesCopy = currentRulesSource.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    IsBusy = true;
                    StatusMessage = isAppTab ? "Applying APP Colors..." : "Applying Main Colors...";
                    await Task.Run(async () =>
                    {
                        if (isAppTab) { _appColoringRules = newRules; if (_allAppLogsCache != null) { await _coloringService.ApplyDefaultColorsAsync(_allAppLogsCache, true); await _coloringService.ApplyCustomColoringAsync(_allAppLogsCache, _appColoringRules); } }
                        else { _mainColoringRules = newRules; if (_allLogsCache != null) { await _coloringService.ApplyDefaultColorsAsync(_allLogsCache, false); await _coloringService.ApplyCustomColoringAsync(_allLogsCache, _mainColoringRules); } }
                    });
                    Application.Current.Dispatcher.Invoke(() => { if (isAppTab) { if (AppDevLogsFiltered != null) foreach (var log in AppDevLogsFiltered) log.OnPropertyChanged("RowBackground"); } else { if (Logs != null) foreach (var log in Logs) log.OnPropertyChanged("RowBackground"); } });
                    IsBusy = false; StatusMessage = "Colors Updated.";
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); IsBusy = false; }
        }
        private void MarkRow(object obj)
        {
            if (SelectedLog != null)
            {
                SelectedLog.IsMarked = !SelectedLog.IsMarked;
                bool isAppTab = SelectedTabIndex == 2;
                var targetList = isAppTab ? MarkedAppLogs : MarkedLogs;
                if (SelectedLog.IsMarked) { targetList.Add(SelectedLog); var sorted = targetList.OrderByDescending(x => x.Date).ToList(); targetList.Clear(); foreach (var l in sorted) targetList.Add(l); }
                else { targetList.Remove(SelectedLog); }
            }
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

        private void JumpToLog(object obj) { if (obj is LogEntry log) { SelectedLog = log; RequestScrollToLog?.Invoke(log); } }
        private void OpenSettingsWindow(object obj)
        {
            var win = new SettingsWindow { DataContext = this };

            // הגדרת הבעלים כדי שהחלון לא יברח אחורה
            if (Application.Current.MainWindow != null && Application.Current.MainWindow != win)
                win.Owner = Application.Current.MainWindow;

            // חישוב המיקום לפי הכפתור שנלחץ
            if (obj is FrameworkElement button)
            {
                // קבלת המיקום של הכפתור ביחס למסך
                Point buttonPosition = button.PointToScreen(new Point(0, 0));

                // הצבת החלון: ה-Left זהה לכפתור, ה-Top הוא מתחת לכפתור
                win.Left = buttonPosition.X;
                win.Top = buttonPosition.Y + button.ActualHeight;
            }
            else
            {
                // גיבוי למקרה שמשהו השתבש - למרכז
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            win.Show();
        }
        private void OpenFontsWindow(object obj) { new FontsWindow { DataContext = this }.ShowDialog(); }
        private void UpdateContentFont(string fontName) { if (!string.IsNullOrEmpty(fontName) && Application.Current != null) UpdateResource(Application.Current.Resources, "ContentFontFamily", new FontFamily(fontName)); }
        private void UpdateContentFontWeight(bool isBold)
        {
            if (Application.Current != null)
            {
                // הוספנו System.Windows לפני FontWeights
                UpdateResource(Application.Current.Resources, "ContentFontWeight",
                    isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);
            }
        }
        private void OpenMarkedLogsWindow(object obj)
        {
            // מצב 1: איחוד חלונות (Combined Mode)
            if (IsMarkedLogsCombined)
            {
                if (_combinedMarkedWindow != null && _combinedMarkedWindow.IsVisible)
                {
                    _combinedMarkedWindow.Activate();
                    return;
                }

                // יצירת רשימה מאוחדת: Main + App, ממויינת לפי תאריך יורד (החדש למעלה)
                var combinedList = new List<LogEntry>();
                if (MarkedLogs != null) combinedList.AddRange(MarkedLogs);
                if (MarkedAppLogs != null) combinedList.AddRange(MarkedAppLogs);

                // מיון: החדש ביותר (Date גדול יותר) למעלה
                var sortedList = combinedList.OrderByDescending(x => x.Date).ToList();

                // המרה ל-ObservableCollection כדי שהחלון יקבל את זה
                var collectionToShow = new ObservableCollection<LogEntry>(sortedList);

                _combinedMarkedWindow = new MarkedLogsWindow(collectionToShow, "Marked Lines (Combined - Main & App)");
                _combinedMarkedWindow.DataContext = this;
                _combinedMarkedWindow.Owner = Application.Current.MainWindow;
                _combinedMarkedWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _combinedMarkedWindow.Closed += (s, e) => _combinedMarkedWindow = null;
                _combinedMarkedWindow.Show();
            }
            // מצב 2: חלונות נפרדים (Split Mode - הלוגיקה הישנה)
            else
            {
                bool isAppTab = SelectedTabIndex == 2;

                if (isAppTab)
                {
                    if (_markedAppLogsWindow != null && _markedAppLogsWindow.IsVisible)
                    {
                        _markedAppLogsWindow.Activate();
                        return;
                    }
                    _markedAppLogsWindow = new MarkedLogsWindow(MarkedAppLogs, "Marked Lines (APP)");
                    _markedAppLogsWindow.DataContext = this;
                    _markedAppLogsWindow.Owner = Application.Current.MainWindow;
                    _markedAppLogsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    _markedAppLogsWindow.Closed += (s, e) => _markedAppLogsWindow = null;
                    _markedAppLogsWindow.Show();
                }
                else
                {
                    if (_markedMainLogsWindow != null && _markedMainLogsWindow.IsVisible)
                    {
                        _markedMainLogsWindow.Activate();
                        return;
                    }
                    _markedMainLogsWindow = new MarkedLogsWindow(MarkedLogs, "Marked Lines (LOGS)");
                    _markedMainLogsWindow.DataContext = this;
                    _markedMainLogsWindow.Owner = Application.Current.MainWindow;
                    _markedMainLogsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    _markedMainLogsWindow.Closed += (s, e) => _markedMainLogsWindow = null;
                    _markedMainLogsWindow.Show();
                }
            }
        }

        // פונקציית עזר לסגירת החלונות (מופעלת כשמשנים את הצ'קבוקס)
        private void CloseAllMarkedWindows()
        {
            if (_combinedMarkedWindow != null) { _combinedMarkedWindow.Close(); _combinedMarkedWindow = null; }
            if (_markedMainLogsWindow != null) { _markedMainLogsWindow.Close(); _markedMainLogsWindow = null; }
            if (_markedAppLogsWindow != null) { _markedAppLogsWindow.Close(); _markedAppLogsWindow = null; }
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

        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        private void OpenOutlook(object obj) { try { Process.Start("outlook.exe", "/c ipm.note"); } catch { OpenUrl("mailto:"); } }
        private void OpenKibana(object obj) { }
        private void OpenGraphViewer(object obj)
        {
            try { string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IoRecorderViewer.exe"); if (File.Exists(exePath)) Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }); else MessageBox.Show($"File not found:\n{exePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        public void OnFilesDropped(string[] files) { if (files != null && files.Length > 0) ProcessFiles(files); }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}