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
    public class SingleChartViewModel : INotifyPropertyChanged
    {
        private PlotModel _model;
        public PlotModel Model { get => _model; set { _model = value; OnPropertyChanged(); } }
        public PlotController Controller { get; private set; }
        public string Title { get; set; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                BorderColor = value ? OxyColors.CornflowerBlue : OxyColors.Gray;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BorderThickness));
                OnPropertyChanged(nameof(BorderColor));
            }
        }

        public OxyColor BorderColor { get; set; } = OxyColors.Gray;
        public Thickness BorderThickness => IsActive ? new Thickness(2) : new Thickness(1);
        public ICommand FloatCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public SingleChartViewModel(string title)
        {
            Title = title;
            CreateNewModel();
            Controller = new PlotController();
            Controller.UnbindAll();
            // הגדרות שליטה: זום בגלגלת, גרירה במקש שמאלי
            Controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
            Controller.BindMouseWheel(PlotCommands.ZoomWheel);
            Controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.None, 2, PlotCommands.ResetAt); // דאבל קליק לאיפוס
        }

        public void CreateNewModel()
        {
            Model = new PlotModel { TextColor = OxyColors.White, PlotAreaBorderColor = OxyColors.Gray, Background = OxyColors.Transparent };
            Model.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight, LegendTextColor = OxyColors.White, LegendBackground = OxyColor.FromAColor(200, OxyColors.Black), LegendBorder = OxyColors.Gray });

            // ציר Y
            Model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, TextColor = OxyColors.White, MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, TicklineColor = OxyColors.White, AxislineColor = OxyColors.White, Key = "Y" });
            // ציר X (זמן)
            Model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", TextColor = OxyColors.White, MajorGridlineStyle = LineStyle.Solid, TicklineColor = OxyColors.White, AxislineColor = OxyColors.White, Key = "X" });
        }
    }

    public class GraphsViewModel : INotifyPropertyChanged
    {
        private readonly GraphService _graphService;
        private Dictionary<string, List<DataPoint>> _allData;
        private List<MachineStateSegment> _allStates;

        public ObservableCollection<SingleChartViewModel> Charts { get; set; } = new ObservableCollection<SingleChartViewModel>();
        private List<SingleChartViewModel> _allActiveCharts = new List<SingleChartViewModel>();

        public ObservableCollection<GraphNode> ComponentTree { get; set; } = new ObservableCollection<GraphNode>();
        public ObservableCollection<MachineStateSegment> StateList { get; set; } = new ObservableCollection<MachineStateSegment>();

        private SingleChartViewModel _selectedChart;
        public SingleChartViewModel SelectedChart { get => _selectedChart; set { if (_selectedChart != null) _selectedChart.IsActive = false; _selectedChart = value; if (_selectedChart != null) _selectedChart.IsActive = true; OnPropertyChanged(); } }

        // --- Toolbar ---
        private bool _isToolbarPinned = true;
        public bool IsToolbarPinned { get => _isToolbarPinned; set { _isToolbarPinned = value; OnPropertyChanged(); IsToolbarVisible = value; } }
        private bool _isToolbarVisible = true;
        public bool IsToolbarVisible { get => _isToolbarVisible; set { _isToolbarVisible = value; OnPropertyChanged(); } }

        private string _status;
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _treeSearchText;
        public string TreeSearchText { get => _treeSearchText; set { _treeSearchText = value; OnPropertyChanged(); FilterTree(_treeSearchText); } }

        // --- Time Control ---
        private DateTime _logStartTime;
        private DateTime _logEndTime;

        // טווח התצוגה הנוכחי
        private DateTime _filterStartTime;
        public DateTime FilterStartTime { get => _filterStartTime; set { _filterStartTime = value; OnPropertyChanged(); } }

        private DateTime _filterEndTime;
        public DateTime FilterEndTime { get => _filterEndTime; set { _filterEndTime = value; OnPropertyChanged(); } }

        // גבולות נעילה (אם אנחנו במצב סטייט, אלו יהיו גבולות הסטייט)
        private DateTime _absoluteMinTime;
        private DateTime _absoluteMaxTime;
        private bool _isLockedToState = false;

        // --- Playback ---
        private DispatcherTimer _playbackTimer;
        private bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }

        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed { get => _playbackSpeed; set { _playbackSpeed = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlaybackSpeedText)); } }
        public string PlaybackSpeedText => $"{_playbackSpeed:0.0}x";

        // --- Commands ---
        public ICommand SpeedUpCommand { get; }
        public ICommand SpeedDownCommand { get; }
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

        private bool _isMeasureMode;
        public bool IsMeasureMode { get => _isMeasureMode; set { _isMeasureMode = value; OnPropertyChanged(); Status = value ? "Click 1st point to start measure." : "Measurement mode off."; if (!value) ClearMeasurement(SelectedChart); } }
        private DataPoint? _measureStart;
        private bool _hasActiveMeasurement = false;

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

            // Apply
            ApplyTimeFilterCommand = new RelayCommand(o => UpdateGraphsView(FilterStartTime, FilterEndTime));

            // Reset
            ResetZoomCommand = new RelayCommand(o => UnlockAndReset());

            ZoomToStateCommand = new RelayCommand(ZoomToState);

            StepTimeCommand = new RelayCommand(param =>
            {
                if (param is object[] args && args.Length == 2 && int.TryParse(args[1].ToString(), out int seconds))
                {
                    DateTime newStart = FilterStartTime;
                    DateTime newEnd = FilterEndTime;
                    if (args[0].ToString() == "Start") newStart = newStart.AddSeconds(seconds);
                    else newEnd = newEnd.AddSeconds(seconds);

                    if (_isLockedToState)
                    {
                        if (newStart < _absoluteMinTime) newStart = _absoluteMinTime;
                        if (newEnd > _absoluteMaxTime) newEnd = _absoluteMaxTime;
                    }
                    UpdateGraphsView(newStart, newEnd);
                }
            });

            PlayCommand = new RelayCommand(o => StartPlayback());
            PauseCommand = new RelayCommand(o => StopPlayback());
            SpeedUpCommand = new RelayCommand(o => ChangeSpeed(true));
            SpeedDownCommand = new RelayCommand(o => ChangeSpeed(false));

            AddChart();
        }

        // --- Playback Logic ---
        private void StartPlayback()
        {
            if (_allActiveCharts.Count == 0) return;
            IsPlaying = true;
            _playbackTimer.Start();
        }

        private void StopPlayback()
        {
            IsPlaying = false;
            _playbackTimer.Stop();
        }

        private void OnPlaybackTick(object sender, EventArgs e)
        {
            double stepSeconds = 0.5 * PlaybackSpeed;
            TimeSpan windowSize = FilterEndTime - FilterStartTime;

            DateTime newStart = FilterStartTime.AddSeconds(stepSeconds);
            DateTime newEnd = FilterEndTime.AddSeconds(stepSeconds);

            DateTime limitEnd = _isLockedToState ? _absoluteMaxTime : _logEndTime;
            DateTime limitStart = _isLockedToState ? _absoluteMinTime : _logStartTime;

            if (newEnd > limitEnd)
            {
                newStart = limitStart;
                newEnd = limitStart + windowSize;
                if (newEnd > limitEnd) newEnd = limitEnd;
            }

            UpdateGraphsView(newStart, newEnd);
        }

        // --- View Logic ---
        public void UpdateGraphsView(DateTime start, DateTime end)
        {
            if (start >= end) return;
            FilterStartTime = start;
            FilterEndTime = end;

            double min = DateTimeAxis.ToDouble(start);
            double max = DateTimeAxis.ToDouble(end);

            foreach (var chart in _allActiveCharts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null)
                {
                    ax.Zoom(min, max);
                    chart.Model.InvalidatePlot(false);
                }
            }
        }

        public void SetTimeRange(DateTime start, DateTime end)
        {
            _isLockedToState = true;
            _absoluteMinTime = start;
            _absoluteMaxTime = end;

            double absMin = DateTimeAxis.ToDouble(start);
            double absMax = DateTimeAxis.ToDouble(end);

            foreach (var chart in _allActiveCharts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null)
                {
                    ax.AbsoluteMinimum = absMin;
                    ax.AbsoluteMaximum = absMax;
                }
            }
            UpdateGraphsView(start, end);
        }

        private void UnlockAndReset()
        {
            _isLockedToState = false;
            foreach (var chart in _allActiveCharts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null)
                {
                    ax.AbsoluteMinimum = double.NaN;
                    ax.AbsoluteMaximum = double.NaN;
                }
            }
            UpdateGraphsView(_logStartTime, _logEndTime);
        }

        private void ZoomToState(object param)
        {
            if (param is MachineStateSegment state)
            {
                SetTimeRange(DateTimeAxis.ToDateTime(state.Start), DateTimeAxis.ToDateTime(state.End));
            }
        }

        private void SyncAxes(Axis sourceAxis)
        {
            if (_isSyncing || IsPlaying) return;
            _isSyncing = true;

            double newMin = sourceAxis.ActualMinimum;
            double newMax = sourceAxis.ActualMaximum;

            if (newMax > newMin)
            {
                try
                {
                    FilterStartTime = DateTimeAxis.ToDateTime(newMin);
                    FilterEndTime = DateTimeAxis.ToDateTime(newMax);
                }
                catch { }

                foreach (var chart in _allActiveCharts)
                {
                    var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                    if (ax != null && ax != sourceAxis)
                    {
                        ax.Zoom(newMin, newMax);
                        chart.Model.InvalidatePlot(false);
                    }
                }
            }
            _isSyncing = false;
        }

        // --- Crosshair ---
        private void OnPlotMouseMove(SingleChartViewModel vm, OxyMouseEventArgs e)
        {
            if (IsMeasureMode) return;
            var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X");
            var yAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (xAxis == null || yAxis == null) return;

            var point = Axis.InverseTransform(e.Position, xAxis, yAxis);
            Status = $"T: {DateTimeAxis.ToDateTime(point.X):HH:mm:ss.fff} | Y: {point.Y:F3}";

            var oldV = vm.Model.Annotations.FirstOrDefault(a => a.Tag == "CursorV");
            var oldH = vm.Model.Annotations.FirstOrDefault(a => a.Tag == "CursorH");
            if (oldV != null) vm.Model.Annotations.Remove(oldV);
            if (oldH != null) vm.Model.Annotations.Remove(oldH);

            var vLine = new LineAnnotation { Type = LineAnnotationType.Vertical, X = point.X, Color = OxyColors.White, StrokeThickness = 1, LineStyle = LineStyle.Dash, Tag = "CursorV", Layer = AnnotationLayer.AboveSeries };
            var hLine = new LineAnnotation { Type = LineAnnotationType.Horizontal, Y = point.Y, Color = OxyColors.White, StrokeThickness = 1, LineStyle = LineStyle.Dash, Tag = "CursorH", Layer = AnnotationLayer.AboveSeries };

            vm.Model.Annotations.Add(vLine);
            vm.Model.Annotations.Add(hLine);
            vm.Model.InvalidatePlot(false);
        }

        // --- Main Functions (Expanded) ---

        public async Task ProcessLogsAsync(IEnumerable<LogEntry> logs)
        {
            Status = "Processing Graph Data...";
            var result = await _graphService.ParseLogsToGraphDataAsync(logs);
            _allData = result.Item1;
            ComponentTree = result.Item2;
            _allStates = result.Item3;

            if (logs.Any())
            {
                _logStartTime = logs.First().Date;
                _logEndTime = logs.Last().Date;
                UnlockAndReset();
            }

            StateList.Clear();
            if (_allStates != null) foreach (var s in _allStates) StateList.Add(s);

            foreach (var c in Charts)
            {
                c.Model.Annotations.Clear();
                AddStateAnnotations(c.Model);
                c.Model.ResetAllAxes();
            }

            OnPropertyChanged(nameof(ComponentTree));
            Status = $"Ready. Loaded {_allData.Keys.Count} signals.";
        }

        public void AddSignalToChart(string fullPath)
        {
            if (SelectedChart == null)
            {
                MessageBox.Show("No chart selected.");
                return;
            }
            if (_allData == null || !_allData.ContainsKey(fullPath))
            {
                return;
            }

            var points = _allData[fullPath];
            if (points == null || points.Count == 0) return;

            var parts = fullPath.Split('.');
            string title = parts.Length >= 2 ? $"{parts[parts.Length - 2]}.{parts.Last()}" : parts.Last();

            if (SelectedChart.Model.Series.Any(s => s.Title == title))
            {
                MessageBox.Show("Duplicate signal");
                return;
            }

            var series = new LineSeries
            {
                Title = title,
                Color = GetNextColor(SelectedChart.Model.Series.Count),
                StrokeThickness = 2,
                MarkerType = MarkerType.None
            };

            if (points.Count > 5000)
                series.Decimator = (Action<List<ScreenPoint>, List<ScreenPoint>>)Decimator.Decimate;
            else
                series.Decimator = null;

            series.Points.AddRange(points);
            SelectedChart.Model.Series.Add(series);

            // תמיד נאפס צירים בהוספת סיגנל כדי לוודא שהוא נראה
            SelectedChart.Model.ResetAllAxes();
            SelectedChart.Model.InvalidatePlot(true);

            UpdateChartTitle(SelectedChart);
        }

        public void RemoveSignalFromChart(string fullPath)
        {
            if (SelectedChart == null) return;
            var parts = fullPath.Split('.');
            string title = parts.Length >= 2 ? $"{parts[parts.Length - 2]}.{parts.Last()}" : parts.Last();

            var series = SelectedChart.Model.Series.FirstOrDefault(s => s.Title == title);
            if (series != null)
            {
                SelectedChart.Model.Series.Remove(series);
                SelectedChart.Model.InvalidatePlot(true);
                UpdateChartTitle(SelectedChart);
            }
        }

        private void AddChart()
        {
            var vm = new SingleChartViewModel($"Chart {_allActiveCharts.Count + 1}");
            vm.Model.MouseDown += (s, e) => OnPlotMouseDown(vm, e);
            vm.Model.MouseMove += (s, e) => OnPlotMouseMove(vm, e);
            vm.FloatCommand = new RelayCommand(o => FloatChart(vm));

            var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X");
            if (xAxis != null) xAxis.AxisChanged += (s, e) => { SyncAxes(xAxis); UpdateStateLabelPositions(vm); };

            var yAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (yAxis != null) yAxis.AxisChanged += (s, e) => UpdateStateLabelPositions(vm);

            if (_allStates != null) AddStateAnnotations(vm.Model);
            Charts.Add(vm);
            _allActiveCharts.Add(vm);
            SelectedChart = vm;
        }

        private void RemoveChartSafe(SingleChartViewModel vm)
        {
            if (Charts.Contains(vm)) Charts.Remove(vm);
            if (_allActiveCharts.Contains(vm)) _allActiveCharts.Remove(vm);
        }

        private void FloatChart(SingleChartViewModel oldVm)
        {
            var newVm = new SingleChartViewModel(oldVm.Title);
            foreach (var oldSeries in oldVm.Model.Series.OfType<LineSeries>())
            {
                var newSeries = new LineSeries { Title = oldSeries.Title, Color = oldSeries.Color, StrokeThickness = oldSeries.StrokeThickness, MarkerType = oldSeries.MarkerType, Decimator = oldSeries.Decimator };
                newSeries.Points.AddRange(oldSeries.Points);
                newVm.Model.Series.Add(newSeries);
            }
            if (_allStates != null) AddStateAnnotations(newVm.Model);

            var xAxis = newVm.Model.Axes.FirstOrDefault(a => a.Key == "X");
            if (xAxis != null) xAxis.AxisChanged += (s, e) => { SyncAxes(xAxis); UpdateStateLabelPositions(newVm); };
            var yAxis = newVm.Model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (yAxis != null) yAxis.AxisChanged += (s, e) => UpdateStateLabelPositions(newVm);

            RemoveChartSafe(oldVm);
            if (Charts.Count == 0) AddChart();
            _allActiveCharts.Add(newVm);

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var win = new FloatingChartWindow(newVm, (vmToRemove) => { if (_allActiveCharts.Contains(vmToRemove)) _allActiveCharts.Remove(vmToRemove); });
                win.Owner = Application.Current.MainWindow;
                win.Show();
            }, DispatcherPriority.ApplicationIdle);
        }

        private void AddStateAnnotations(PlotModel model)
        {
            if (_allStates == null) return;
            foreach (var seg in _allStates)
            {
                model.Annotations.Add(new RectangleAnnotation
                {
                    MinimumX = seg.Start,
                    MaximumX = seg.End,
                    Fill = OxyColor.FromAColor(80, seg.Color),
                    Layer = AnnotationLayer.BelowSeries,
                    ToolTip = seg.Name
                });

                model.Annotations.Add(new TextAnnotation
                {
                    Text = seg.Name,
                    TextPosition = new DataPoint((seg.Start + seg.End) / 2, 0),
                    TextColor = OxyColors.White,
                    FontWeight = OxyPlot.FontWeights.Bold,
                    Background = OxyColor.FromAColor(150, OxyColors.Black),
                    Stroke = OxyColors.White,
                    StrokeThickness = 1,
                    Padding = new OxyThickness(4),
                    Layer = AnnotationLayer.AboveSeries,
                    Tag = "StateLabel",
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
                });
            }
        }

        private void UpdateStateLabelPositions(SingleChartViewModel vm)
        {
            var yAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (yAxis == null) return;
            double centerY = (yAxis.ActualMaximum + yAxis.ActualMinimum) / 2.0;
            bool changed = false;
            foreach (var ann in vm.Model.Annotations.OfType<TextAnnotation>())
            {
                if (ann.Tag == "StateLabel") { ann.TextPosition = new DataPoint(ann.TextPosition.X, centerY); changed = true; }
            }
            if (changed) vm.Model.InvalidatePlot(false);
        }

        private void UpdateChartTitle(SingleChartViewModel vm) { var titles = vm.Model.Series.OfType<Series>().Select(s => s.Title).ToList(); vm.Title = titles.Any() ? string.Join(", ", titles) : $"Chart {Charts.IndexOf(vm) + 1}"; }
        private OxyColor GetNextColor(int index) { var colors = new[] { OxyColors.Cyan, OxyColors.Orange, OxyColors.Lime, OxyColors.Magenta, OxyColors.Yellow, OxyColors.CornflowerBlue }; return colors[index % colors.Length]; }

        private void ChangeSpeed(bool increase)
        {
            var speeds = new[] { 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0 };
            int currentIndex = Array.FindIndex(speeds, s => s >= _playbackSpeed);
            if (currentIndex == -1) currentIndex = 3;
            if (increase) { if (currentIndex < speeds.Length - 1) PlaybackSpeed = speeds[currentIndex + 1]; }
            else { if (currentIndex > 0) PlaybackSpeed = speeds[currentIndex - 1]; }
        }

        private void OnPlotMouseDown(SingleChartViewModel vm, OxyMouseDownEventArgs e) { bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control; if (!isCtrl) { SelectedChart = vm; return; } if (e.ChangedButton == OxyMouseButton.Left) { if (_hasActiveMeasurement) { ClearMeasurement(vm); _hasActiveMeasurement = false; _measureStart = null; Status = "Measurement cleared."; vm.Model.InvalidatePlot(true); return; } var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X"); var yAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "Y"); if (xAxis == null || yAxis == null) return; var point = Axis.InverseTransform(e.Position, xAxis, yAxis); if (_measureStart == null) { _measureStart = point; Status = "Point 1 Set."; vm.Model.Annotations.Add(new PointAnnotation { X = point.X, Y = point.Y, Fill = OxyColors.Red, Shape = MarkerType.Circle, Size = 5 }); } else { var p1 = _measureStart.Value; var p2 = point; vm.Model.Annotations.Add(new PointAnnotation { X = p2.X, Y = p2.Y, Fill = OxyColors.Red, Shape = MarkerType.Circle, Size = 5 }); vm.Model.Annotations.Add(new ArrowAnnotation { StartPoint = p1, EndPoint = p2, Color = OxyColors.Blue, LineStyle = LineStyle.Dash, HeadLength = 0 }); double dt = (p2.X - p1.X) * 24 * 3600; double dy = p2.Y - p1.Y; vm.Model.Annotations.Add(new TextAnnotation { TextPosition = new DataPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2), Text = $"ΔT: {Math.Abs(dt):F3}s\nΔY: {dy:F4}", Background = OxyColor.FromAColor(220, OxyColors.LightYellow), Stroke = OxyColors.Black, StrokeThickness = 1, TextColor = OxyColors.Black, Padding = new OxyThickness(5) }); Status = $"Measured: ΔT={Math.Abs(dt):F3}s, ΔY={dy:F3}"; _measureStart = null; _hasActiveMeasurement = true; } vm.Model.InvalidatePlot(true); e.Handled = true; } }
        private void ClearMeasurement(SingleChartViewModel vm) { if (vm == null) return; var toRemove = vm.Model.Annotations.Where(a => a is PointAnnotation || a is ArrowAnnotation || (a is TextAnnotation ta && ta.Tag != "StateLabel")).ToList(); foreach (var a in toRemove) vm.Model.Annotations.Remove(a); }
        private void FilterTree(string text) { if (ComponentTree == null) return; foreach (var node in ComponentTree) FilterNodeRecursive(node, text); }
        private bool FilterNodeRecursive(GraphNode node, string text) { bool match = string.IsNullOrEmpty(text) || node.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0; bool childMatch = false; foreach (var child in node.Children) { if (FilterNodeRecursive(child, text)) childMatch = true; } if (childMatch) { node.IsVisible = true; node.IsExpanded = true; return true; } node.IsVisible = match; if (!match) node.IsExpanded = false; return match; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}