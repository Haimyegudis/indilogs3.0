using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Views;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Legends;
using OxyPlot.Annotations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;

namespace IndiLogs_3._0.ViewModels
{
    // ... (SingleChartViewModel נשאר ללא שינוי - העתק אותו מהקודם)
    public class SingleChartViewModel : INotifyPropertyChanged
    {
        // ... (אותו קוד בדיוק כמו מקודם) ...
        private PlotModel _model;
        public PlotModel Model { get => _model; set { _model = value; OnPropertyChanged(); } }
        public PlotController Controller { get; private set; }
        public string Title { get; set; }
        private bool _isActive;
        public bool IsActive { get => _isActive; set { _isActive = value; BorderColor = value ? OxyColors.CornflowerBlue : OxyColors.Gray; OnPropertyChanged(); OnPropertyChanged(nameof(BorderThickness)); OnPropertyChanged(nameof(BorderColor)); } }
        public OxyColor BorderColor { get; set; } = OxyColors.Gray;
        public Thickness BorderThickness => IsActive ? new Thickness(2) : new Thickness(1);
        public ICommand FloatCommand { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public SingleChartViewModel(string title) { Title = title; CreateNewModel(); Controller = new PlotController(); Controller.UnbindAll(); Controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt); Controller.BindMouseWheel(PlotCommands.ZoomWheel); }
        public void CreateNewModel() { Model = new PlotModel { TextColor = OxyColors.Black, PlotAreaBorderColor = OxyColors.Black, Background = OxyColors.Transparent }; Model.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight, LegendTextColor = OxyColors.Black, LegendBackground = OxyColor.FromAColor(200, OxyColors.White), LegendBorder = OxyColors.Black }); Model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, TextColor = OxyColors.Black, MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, TicklineColor = OxyColors.Black, AxislineColor = OxyColors.Black, Key = "Y" }); Model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", TextColor = OxyColors.Black, MajorGridlineStyle = LineStyle.Solid, TicklineColor = OxyColors.Black, AxislineColor = OxyColors.Black, Key = "X" }); }
    }

    public class GraphsViewModel : INotifyPropertyChanged
    {
        // ... (שאר המשתנים נשארים אותו דבר)
        private readonly GraphService _graphService;
        private Dictionary<string, List<DataPoint>> _allData;
        private List<MachineStateSegment> _allStates;

        public ObservableCollection<SingleChartViewModel> Charts { get; set; } = new ObservableCollection<SingleChartViewModel>();
        private List<SingleChartViewModel> _allActiveCharts = new List<SingleChartViewModel>(); // רשימה גלובלית
        public ObservableCollection<GraphNode> ComponentTree { get; set; } = new ObservableCollection<GraphNode>();
        public ObservableCollection<MachineStateSegment> StateList { get; set; } = new ObservableCollection<MachineStateSegment>();

        private SingleChartViewModel _selectedChart;
        public SingleChartViewModel SelectedChart { get => _selectedChart; set { if (_selectedChart != null) _selectedChart.IsActive = false; _selectedChart = value; if (_selectedChart != null) _selectedChart.IsActive = true; OnPropertyChanged(); } }

        private string _status; public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        private string _treeSearchText; public string TreeSearchText { get => _treeSearchText; set { _treeSearchText = value; OnPropertyChanged(); FilterTree(_treeSearchText); } }

        private DateTime _logStartTime; private DateTime _logEndTime;
        public DateTime FilterStartTime
        {
            get => _filterStartTime;
            set
            {
                // ולידציה: לא לרדת מתחת להתחלת הלוג
                if (value < _logStartTime) value = _logStartTime;
                // ולידציה: לא לעבור את זמן הסיום שנבחר
                if (value > _filterEndTime && _filterEndTime != DateTime.MinValue) value = _filterEndTime;

                _filterStartTime = value;
                OnPropertyChanged();
            }
        }
        private DateTime _filterStartTime;
        public DateTime FilterEndTime
        {
            get => _filterEndTime;
            set
            {
                // ולידציה: לא לעבור את סוף הלוג
                if (value > _logEndTime) value = _logEndTime;
                // ולידציה: לא להיות קטן מזמן ההתחלה שנבחר
                if (value < _filterStartTime) value = _filterStartTime;

                _filterEndTime = value;
                OnPropertyChanged();
            }
        }
        private DateTime _filterEndTime;

        // --- Playback & Speed ---
        private DispatcherTimer _playbackTimer;
        private DateTime _currentPlaybackTime;
        private bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }

        // משתנה מהירות (ברירת מחדל 1.0)
        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlaybackSpeedText));
            }
        }
        public string PlaybackSpeedText => $"{_playbackSpeed:0.0}x";

        // פקודות מהירות
        public ICommand SpeedUpCommand { get; }
        public ICommand SpeedDownCommand { get; }

        // --- מדידה ---
        private bool _isMeasureMode;
        public bool IsMeasureMode { get => _isMeasureMode; set { _isMeasureMode = value; OnPropertyChanged(); Status = value ? "Click 1st point to start measure." : "Measurement mode off."; if (!value) ClearMeasurement(SelectedChart); } }
        private DataPoint? _measureStart;
        private bool _hasActiveMeasurement = false;

        public ICommand AddChartCommand { get; }
        public ICommand RemoveChartCommand { get; }
        public ICommand ClearChartCommand { get; }
        public ICommand ToggleMeasureCommand { get; }
        public ICommand ApplyTimeFilterCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ZoomToStateCommand { get; }
        public ICommand StepTimeCommand { get; }
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }

        private double _sharedMinX; private double _sharedMaxX; private bool _isSyncing = false;

        public GraphsViewModel()
        {
            _graphService = new GraphService();

            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _playbackTimer.Tick += OnPlaybackTick;

            AddChartCommand = new RelayCommand(o => AddChart());
            RemoveChartCommand = new RelayCommand(o => { if (SelectedChart != null && Charts.Count > 1) { RemoveChartSafe(SelectedChart); SelectedChart = Charts.FirstOrDefault(); } });
            ClearChartCommand = new RelayCommand(o => { if (SelectedChart != null) { SelectedChart.Model.Series.Clear(); ClearMeasurement(SelectedChart); SelectedChart.Model.InvalidatePlot(true); SelectedChart.Title = "Empty Chart"; } });

            ToggleMeasureCommand = new RelayCommand(o => IsMeasureMode = !IsMeasureMode);
            ApplyTimeFilterCommand = new RelayCommand(o => LockViewToRange(FilterStartTime, FilterEndTime));
            ResetZoomCommand = new RelayCommand(o => { FilterStartTime = _logStartTime; FilterEndTime = _logEndTime; UnlockAndResetView(); });
            ZoomToStateCommand = new RelayCommand(ZoomToState);
            StepTimeCommand = new RelayCommand(param =>
            {
                if (param is object[] args && args.Length == 2 && int.TryParse(args[1].ToString(), out int seconds))
                {
                    if (args[0].ToString() == "Start") FilterStartTime = FilterStartTime.AddSeconds(seconds);
                    else FilterEndTime = FilterEndTime.AddSeconds(seconds);
                    if (FilterStartTime < _logStartTime) FilterStartTime = _logStartTime;
                    if (FilterEndTime > _logEndTime) FilterEndTime = _logEndTime;
                }
            });

            PlayCommand = new RelayCommand(o => StartPlayback());
            PauseCommand = new RelayCommand(o => StopPlayback());

            // לוגיקת שינוי מהירות
            SpeedUpCommand = new RelayCommand(o => ChangeSpeed(true));
            SpeedDownCommand = new RelayCommand(o => ChangeSpeed(false));

            AddChart();
        }

        private void ChangeSpeed(bool increase)
        {
            // רשימת מהירויות מוגדרת מראש
            var speeds = new[] { 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0 };

            // מציאת האינדקס הנוכחי (או הקרוב ביותר)
            int currentIndex = Array.FindIndex(speeds, s => s >= _playbackSpeed);
            if (currentIndex == -1) currentIndex = 3; // Default 1.0

            if (increase)
            {
                if (currentIndex < speeds.Length - 1) PlaybackSpeed = speeds[currentIndex + 1];
            }
            else
            {
                if (currentIndex > 0) PlaybackSpeed = speeds[currentIndex - 1];
            }
        }

        private void OnPlaybackTick(object sender, EventArgs e)
        {
            // התקדמות = בסיס (0.5 שניות לטיק) * המהירות
            double stepSeconds = 0.5 * PlaybackSpeed;
            _currentPlaybackTime = _currentPlaybackTime.AddSeconds(stepSeconds);

            if (_currentPlaybackTime > FilterEndTime)
            {
                _currentPlaybackTime = FilterEndTime;
                StopPlayback();
            }

            double start = DateTimeAxis.ToDouble(FilterStartTime);
            double current = DateTimeAxis.ToDouble(_currentPlaybackTime);

            foreach (var chart in _allActiveCharts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null)
                {
                    ax.Zoom(start, current);
                    chart.Model.InvalidatePlot(false);
                }
            }
        }

        // ... (כל שאר הפונקציות נשארות זהות לקובץ הקודם: AddChart, FloatChart, RemoveChartSafe, SyncAxes, etc.)
        // אנא העתק אותן מהתשובה הקודמת כדי לא ליצור חורים בקוד.
        // אני מצרף את החשובות למטה ליתר ביטחון:

        private void StartPlayback()
        {
            if (_allActiveCharts.Count == 0) return;

            // לוגיקה חדשה: אם אנחנו לא במצב "פילטר" פעיל, או שהנגן סיים, 
            // נעדכן את זמני הנגינה לפי מה שמוצג כרגע על המסך
            if (_currentPlaybackTime >= FilterEndTime || _currentPlaybackTime < FilterStartTime)
            {
                // מנסה לקחת את הגבולות מהצ'ארט הנבחר
                var xAxis = SelectedChart?.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (xAxis != null)
                {
                    // עדכון הפילטרים למה שרואים בעיניים כרגע
                    FilterStartTime = DateTimeAxis.ToDateTime(xAxis.ActualMinimum);
                    FilterEndTime = DateTimeAxis.ToDateTime(xAxis.ActualMaximum);

                    // ולידציה שהם בתוך גבולות הלוג
                    if (FilterStartTime < _logStartTime) FilterStartTime = _logStartTime;
                    if (FilterEndTime > _logEndTime) FilterEndTime = _logEndTime;
                }

                // מתחילים מההתחלה של החלון הנוכחי
                _currentPlaybackTime = FilterStartTime;
            }

            IsPlaying = true;
            _playbackTimer.Start();
        }

        private void StopPlayback()
        {
            IsPlaying = false;
            _playbackTimer.Stop();
        }

        private void AddChart()
        {
            var vm = new SingleChartViewModel($"Chart {_allActiveCharts.Count + 1}");
            vm.Model.MouseDown += (s, e) => OnPlotMouseDown(vm, e);
            vm.FloatCommand = new RelayCommand(o => FloatChart(vm));
            var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X");
            if (xAxis != null) xAxis.AxisChanged += (s, e) => SyncAxes(xAxis);
            if (_allStates != null) AddStateAnnotations(vm.Model);
            Charts.Add(vm);
            _allActiveCharts.Add(vm);
            SelectedChart = vm;
        }

        private void RemoveChartSafe(SingleChartViewModel vm) { if (Charts.Contains(vm)) Charts.Remove(vm); if (_allActiveCharts.Contains(vm)) _allActiveCharts.Remove(vm); }
        private void FloatChart(SingleChartViewModel oldVm)
        {
            var newVm = new SingleChartViewModel(oldVm.Title);
            foreach (var oldSeries in oldVm.Model.Series.OfType<LineSeries>()) { var newSeries = new LineSeries { Title = oldSeries.Title, Color = oldSeries.Color, StrokeThickness = oldSeries.StrokeThickness, MarkerType = oldSeries.MarkerType, Decimator = oldSeries.Decimator }; newSeries.Points.AddRange(oldSeries.Points); newVm.Model.Series.Add(newSeries); }
            if (_allStates != null) AddStateAnnotations(newVm.Model);
            var xAxis = newVm.Model.Axes.FirstOrDefault(a => a.Key == "X"); if (xAxis != null) xAxis.AxisChanged += (s, e) => SyncAxes(xAxis);
            RemoveChartSafe(oldVm); if (Charts.Count == 0) AddChart();
            _allActiveCharts.Add(newVm);
            Application.Current.Dispatcher.InvokeAsync(() => { var win = new FloatingChartWindow(newVm, (vmToRemove) => { if (_allActiveCharts.Contains(vmToRemove)) _allActiveCharts.Remove(vmToRemove); }); win.Owner = Application.Current.MainWindow; win.Show(); }, DispatcherPriority.ApplicationIdle);
        }
        private void SyncAxes(Axis sourceAxis) { if (_isSyncing || IsPlaying) return; _isSyncing = true; if (Math.Abs(sourceAxis.ActualMinimum - _sharedMinX) > 0.0001 || Math.Abs(sourceAxis.ActualMaximum - _sharedMaxX) > 0.0001) { _sharedMinX = sourceAxis.ActualMinimum; _sharedMaxX = sourceAxis.ActualMaximum; foreach (var chart in _allActiveCharts) { var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X"); if (ax != null && ax != sourceAxis) { ax.Zoom(_sharedMinX, _sharedMaxX); chart.Model.InvalidatePlot(false); } } } _isSyncing = false; }
        private void LockViewToRange(DateTime start, DateTime end) { if (start >= end) { MessageBox.Show("Invalid time range."); return; } double min = DateTimeAxis.ToDouble(start); double max = DateTimeAxis.ToDouble(end); foreach (var chart in _allActiveCharts) { var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X"); if (ax != null) { ax.AbsoluteMinimum = min; ax.AbsoluteMaximum = max; ax.Zoom(min, max); chart.Model.InvalidatePlot(false); } } }
        private void UnlockAndResetView() { foreach (var chart in _allActiveCharts) { var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X"); if (ax != null) { ax.AbsoluteMinimum = double.NaN; ax.AbsoluteMaximum = double.NaN; chart.Model.ResetAllAxes(); chart.Model.InvalidatePlot(false); } } }
        private void ZoomToState(object param) { if (param is MachineStateSegment state) { FilterStartTime = DateTimeAxis.ToDateTime(state.Start); FilterEndTime = DateTimeAxis.ToDateTime(state.End); LockViewToRange(FilterStartTime, FilterEndTime); } }
        private void OnPlotMouseDown(SingleChartViewModel vm, OxyMouseDownEventArgs e) { bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control; if (!isCtrl) { SelectedChart = vm; return; } if (e.ChangedButton == OxyMouseButton.Left) { if (_hasActiveMeasurement) { ClearMeasurement(vm); _hasActiveMeasurement = false; _measureStart = null; Status = "Measurement cleared."; vm.Model.InvalidatePlot(true); return; } var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X"); var yAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "Y"); if (xAxis == null || yAxis == null) return; var point = Axis.InverseTransform(e.Position, xAxis, yAxis); if (_measureStart == null) { _measureStart = point; Status = "Point 1 Set."; vm.Model.Annotations.Add(new PointAnnotation { X = point.X, Y = point.Y, Fill = OxyColors.Red, Shape = MarkerType.Circle, Size = 5 }); } else { var p1 = _measureStart.Value; var p2 = point; vm.Model.Annotations.Add(new PointAnnotation { X = p2.X, Y = p2.Y, Fill = OxyColors.Red, Shape = MarkerType.Circle, Size = 5 }); vm.Model.Annotations.Add(new ArrowAnnotation { StartPoint = p1, EndPoint = p2, Color = OxyColors.Blue, LineStyle = LineStyle.Dash, HeadLength = 0 }); double dt = (p2.X - p1.X) * 24 * 3600; double dy = p2.Y - p1.Y; var midX = (p1.X + p2.X) / 2; var midY = (p1.Y + p2.Y) / 2; vm.Model.Annotations.Add(new TextAnnotation { TextPosition = new DataPoint(midX, midY), Text = $"ΔT: {Math.Abs(dt):F3}s\nΔY: {dy:F4}", Background = OxyColor.FromAColor(220, OxyColors.LightYellow), Stroke = OxyColors.Black, StrokeThickness = 1, TextColor = OxyColors.Black, Padding = new OxyThickness(5) }); Status = $"Measured: ΔT={Math.Abs(dt):F3}s, ΔY={dy:F3}"; _measureStart = null; _hasActiveMeasurement = true; } vm.Model.InvalidatePlot(true); e.Handled = true; } }
        private void ClearMeasurement(SingleChartViewModel vm) { if (vm == null) return; var toRemove = vm.Model.Annotations.Where(a => a is PointAnnotation || a is ArrowAnnotation || (a is TextAnnotation ta && ta.Layer != AnnotationLayer.BelowSeries)).ToList(); foreach (var a in toRemove) vm.Model.Annotations.Remove(a); }
        private void AddStateAnnotations(PlotModel model) { if (_allStates == null) return; int staggerIndex = 0; foreach (var seg in _allStates) { model.Annotations.Add(new RectangleAnnotation { MinimumX = seg.Start, MaximumX = seg.End, Fill = seg.Color, Layer = AnnotationLayer.BelowSeries, ToolTip = seg.Name }); var text = new TextAnnotation { Text = seg.Name, TextPosition = new DataPoint((seg.Start + seg.End) / 2, 0), TextColor = OxyColors.Black, Background = OxyColor.FromAColor(180, OxyColors.White), Stroke = OxyColors.Black, StrokeThickness = 1, Padding = new OxyThickness(2), Layer = AnnotationLayer.BelowSeries, TextVerticalAlignment = staggerIndex % 2 == 0 ? OxyPlot.VerticalAlignment.Top : OxyPlot.VerticalAlignment.Bottom }; if (staggerIndex % 2 != 0) text.Offset = new ScreenVector(0, -20); else text.Offset = new ScreenVector(0, 20); model.Annotations.Add(text); staggerIndex++; } }
        private void ReapplyStates(SingleChartViewModel vm) { if (_allStates == null) return; var toRemove = vm.Model.Annotations.Where(a => a is RectangleAnnotation || (a is TextAnnotation ta && ta.Layer == AnnotationLayer.BelowSeries)).ToList(); foreach (var a in toRemove) vm.Model.Annotations.Remove(a); AddStateAnnotations(vm.Model); }
        private void FilterTree(string text) { if (ComponentTree == null) return; foreach (var node in ComponentTree) FilterNodeRecursive(node, text); }
        private bool FilterNodeRecursive(GraphNode node, string text) { bool match = string.IsNullOrEmpty(text) || node.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0; bool childMatch = false; foreach (var child in node.Children) { if (FilterNodeRecursive(child, text)) childMatch = true; } if (childMatch) { node.IsVisible = true; node.IsExpanded = true; return true; } node.IsVisible = match; if (!match) node.IsExpanded = false; return match; }
        private void UpdateChartTitle(SingleChartViewModel vm) { var titles = vm.Model.Series.OfType<Series>().Select(s => s.Title).ToList(); vm.Title = titles.Any() ? string.Join(", ", titles) : $"Chart {Charts.IndexOf(vm) + 1}"; }
        private OxyColor GetNextColor(int index) { var colors = new[] { OxyColors.Blue, OxyColors.Red, OxyColors.Green, OxyColors.Orange, OxyColors.Purple, OxyColors.Brown, OxyColors.Magenta, OxyColors.Teal, OxyColors.Coral }; return colors[index % colors.Length]; }
        public async Task ProcessLogsAsync(IEnumerable<LogEntry> logs) { Status = "Processing Graph Data..."; var result = await _graphService.ParseLogsToGraphDataAsync(logs); _allData = result.Item1; ComponentTree = result.Item2; _allStates = result.Item3; if (logs.Any()) { _logStartTime = logs.First().Date; _logEndTime = logs.Last().Date; FilterStartTime = _logStartTime; FilterEndTime = _logEndTime; } StateList.Clear(); if (_allStates != null) foreach (var s in _allStates) StateList.Add(s); foreach (var c in Charts) ReapplyStates(c); OnPropertyChanged(nameof(ComponentTree)); Status = $"Ready. Loaded {_allData.Keys.Count} signals."; }
        public void AddSignalToChart(string fullPath) { if (SelectedChart == null || _allData == null || !_allData.ContainsKey(fullPath)) return; var points = _allData[fullPath]; if (points == null || points.Count == 0) return; var parts = fullPath.Split('.'); string title = parts.Length >= 2 ? $"{parts[parts.Length - 2]}.{parts.Last()}" : parts.Last(); if (SelectedChart.Model.Series.Any(s => s.Title == title)) { MessageBox.Show("Duplicate"); return; } var series = new LineSeries { Title = title, Color = GetNextColor(SelectedChart.Model.Series.Count), StrokeThickness = 2, MarkerType = MarkerType.None }; if (points.Count > 5000) series.Decimator = (Action<List<ScreenPoint>, List<ScreenPoint>>)Decimator.Decimate; else series.Decimator = null; series.Points.AddRange(points); SelectedChart.Model.Series.Add(series); if (points.Any()) { var minP = points.OrderBy(p => p.Y).First(); var maxP = points.OrderByDescending(p => p.Y).First(); AddMinMaxAnnotations(SelectedChart.Model, minP, maxP, series.Color); } if (double.IsNaN(SelectedChart.Model.Axes.FirstOrDefault(a => a.Key == "X")?.AbsoluteMinimum ?? double.NaN)) { SelectedChart.Model.ResetAllAxes(); } else { SelectedChart.Model.InvalidatePlot(true); } UpdateChartTitle(SelectedChart); }
        public void RemoveSignalFromChart(string fullPath) { if (SelectedChart == null) return; var parts = fullPath.Split('.'); string titleToRemove = parts.Length >= 2 ? $"{parts[parts.Length - 2]}.{parts.Last()}" : parts.Last(); var seriesToRemove = SelectedChart.Model.Series.OfType<LineSeries>().FirstOrDefault(s => s.Title == titleToRemove); if (seriesToRemove != null) { SelectedChart.Model.Series.Remove(seriesToRemove); var annsToRemove = SelectedChart.Model.Annotations.Where(a => a is PointAnnotation pa && pa.Fill == seriesToRemove.Color).ToList(); foreach (var a in annsToRemove) SelectedChart.Model.Annotations.Remove(a); SelectedChart.Model.InvalidatePlot(true); UpdateChartTitle(SelectedChart); } }
        private void AddMinMaxAnnotations(PlotModel model, DataPoint min, DataPoint max, OxyColor color) { model.Annotations.Add(new PointAnnotation { X = max.X, Y = max.Y, Text = $"{max.Y:0.##}", TextColor = OxyColors.Black, Fill = color, Shape = MarkerType.Triangle, TextVerticalAlignment = OxyPlot.VerticalAlignment.Top, FontWeight = OxyPlot.FontWeights.Bold }); model.Annotations.Add(new PointAnnotation { X = min.X, Y = min.Y, Text = $"{min.Y:0.##}", TextColor = OxyColors.Black, Fill = color, Shape = MarkerType.Diamond, TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom, FontWeight = OxyPlot.FontWeights.Bold }); }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}