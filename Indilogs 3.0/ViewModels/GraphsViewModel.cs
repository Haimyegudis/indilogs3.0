using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using OxyPlot.Legends;
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
        public string Id { get; } = Guid.NewGuid().ToString();
        private PlotModel _model;
        public PlotModel Model { get => _model; set { _model = value; OnPropertyChanged(); } }
        public PlotController Controller { get; private set; }
        private string _title;
        public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
        private string _originalTitle;

        public ObservableCollection<string> PlottedKeys { get; set; } = new ObservableCollection<string>();

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                BorderColor = value ? "#3B82F6" : "#CCCCCC";
                BorderThickness = value ? new Thickness(2) : new Thickness(1);
                OnPropertyChanged(); OnPropertyChanged(nameof(BorderColor)); OnPropertyChanged(nameof(BorderThickness));
            }
        }

        public string BorderColor { get; set; } = "#CCCCCC";
        public Thickness BorderThickness { get; set; } = new Thickness(1);
        public ICommand FloatCommand { get; set; }

        private LineAnnotation _cursorX;
        private LineAnnotation _cursorY;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public SingleChartViewModel(string title)
        {
            Title = title;
            _originalTitle = title;
            CreateNewModel();

            Controller = new PlotController();
            Controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
            Controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.Control, PlotCommands.ZoomRectangle);
            Controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.None, 2, PlotCommands.ResetAt);

            Controller.BindMouseWheel(new DelegatePlotCommand<OxyMouseWheelEventArgs>((view, controller, args) =>
            {
                double factor = args.Delta > 0 ? 1.5 : 1 / 1.5;
                foreach (var axis in view.ActualModel.Axes)
                {
                    if (axis.IsHorizontal()) axis.ZoomAt(factor, args.Position.X);
                    else axis.ZoomAt(factor, args.Position.Y);
                }
                view.InvalidatePlot(false);
            }));
        }

        public void CreateNewModel()
        {
            Model = new PlotModel { TextColor = OxyColors.Black, PlotAreaBorderColor = OxyColors.Gray, Background = OxyColors.White };

            Model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendTextColor = OxyColors.Black,
                LegendBackground = OxyColor.FromAColor(200, OxyColors.White),
                LegendBorder = OxyColors.Gray,
                FontSize = 10
            });

            var yAxis = new LinearAxis { Position = AxisPosition.Left, TextColor = OxyColors.Black, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.Black), Key = "Y" };
            var xAxis = new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", TextColor = OxyColors.Black, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.Black), Key = "X" };

            Model.Axes.Add(yAxis);
            Model.Axes.Add(xAxis);

            // קו האפס
            Model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal,
                Y = 0,
                Color = OxyColors.Black,
                StrokeThickness = 1.5,
                LineStyle = LineStyle.Solid,
                Layer = AnnotationLayer.BelowSeries,
                Text = "0"
            });

            // --- תיקון שגיאות Visibility ---
            // במקום Visibility, נשתמש ב-StrokeThickness=0 להסתרה
            _cursorX = new LineAnnotation { Type = LineAnnotationType.Vertical, Color = OxyColors.Black, LineStyle = LineStyle.Dash, StrokeThickness = 0, Layer = AnnotationLayer.AboveSeries };
            _cursorY = new LineAnnotation { Type = LineAnnotationType.Horizontal, Color = OxyColors.Black, LineStyle = LineStyle.Dash, StrokeThickness = 0, Layer = AnnotationLayer.AboveSeries };

            Model.Annotations.Add(_cursorX);
            Model.Annotations.Add(_cursorY);

            Model.MouseMove += (s, e) =>
            {
                var xVal = xAxis.InverseTransform(e.Position.X);
                var yVal = yAxis.InverseTransform(e.Position.Y);

                // הצגת הקווים
                _cursorX.X = xVal;
                _cursorY.Y = yVal;
                _cursorX.StrokeThickness = 1; // הופך לנראה
                _cursorY.StrokeThickness = 1; // הופך לנראה

                DateTime time = DateTimeAxis.ToDateTime(xVal);
                Title = $"{_originalTitle}  |  Time: {time:HH:mm:ss.fff}  |  Value: {yVal:F3}";

                Model.InvalidatePlot(false);
            };
        }

        public void UpdateTitle(string newParams)
        {
            _originalTitle = newParams;
            Title = newParams;
        }
    }

    public class GraphsViewModel : INotifyPropertyChanged
    {
        private readonly GraphService _graphService;
        private Dictionary<string, List<DataPoint>> _allData;
        private List<MachineStateSegment> _allStates;

        public ObservableCollection<SingleChartViewModel> Charts { get; set; } = new ObservableCollection<SingleChartViewModel>();
        public ObservableCollection<GraphNode> ComponentTree { get; set; } = new ObservableCollection<GraphNode>();
        public ObservableCollection<string> ActiveChartSignals { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MachineStateSegment> StateTimeline { get; set; } = new ObservableCollection<MachineStateSegment>();
        public ObservableCollection<AnalysisEvent> AnalysisResults { get; set; } = new ObservableCollection<AnalysisEvent>();

        private string _selectedSignalToRemove;
        public string SelectedSignalToRemove { get => _selectedSignalToRemove; set { _selectedSignalToRemove = value; OnPropertyChanged(); } }

        private SingleChartViewModel _selectedChart;
        public SingleChartViewModel SelectedChart
        {
            get => _selectedChart;
            set
            {
                if (_selectedChart != null) _selectedChart.IsActive = false;
                _selectedChart = value;
                if (_selectedChart != null) { _selectedChart.IsActive = true; UpdateActiveSignalsList(); }
                OnPropertyChanged();
            }
        }

        private string _status = "Ready";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _searchText;
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); FilterTree(_searchText); } }

        private DateTime _logStartTime, _logEndTime;

        private DateTime _filterStartTime;
        public DateTime FilterStartTime
        {
            get => _filterStartTime;
            set { _filterStartTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartTimeText)); }
        }

        private DateTime _filterEndTime;
        public DateTime FilterEndTime
        {
            get => _filterEndTime;
            set { _filterEndTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTimeText)); }
        }

        public string StartTimeText
        {
            get => _filterStartTime.ToString("HH:mm:ss");
            set { if (DateTime.TryParse(value, out DateTime dt)) FilterStartTime = _filterStartTime.Date + dt.TimeOfDay; }
        }

        public string EndTimeText
        {
            get => _filterEndTime.ToString("HH:mm:ss");
            set { if (DateTime.TryParse(value, out DateTime dt)) FilterEndTime = _filterEndTime.Date + dt.TimeOfDay; }
        }

        private DispatcherTimer _playbackTimer;
        private bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }

        private double _playbackSpeed = 1.0;
        public string PlaybackSpeedText => $"{_playbackSpeed:0.0}x";

        private bool _isMeasureMode;
        public bool IsMeasureMode { get => _isMeasureMode; set { _isMeasureMode = value; OnPropertyChanged(); } }
        private bool _isSyncing = false;

        public ICommand AddChartCommand { get; }
        public ICommand RemoveChartCommand { get; }
        public ICommand ClearChartCommand { get; }
        public ICommand AddSignalToActiveCommand { get; }
        public ICommand RemoveSignalFromActiveCommand { get; }
        public ICommand ApplyTimeFilterCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand SpeedUpCommand { get; }
        public ICommand SpeedDownCommand { get; }
        public ICommand ZoomToStateCommand { get; }
        public ICommand ClearAnalysisCommand { get; }
        public ICommand ZoomToAnalysisEventCommand { get; }

        public GraphsViewModel()
        {
            _graphService = new GraphService();
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _playbackTimer.Tick += OnPlaybackTick;

            AddChartCommand = new RelayCommand(o => AddNewChart());
            RemoveChartCommand = new RelayCommand(o => RemoveSelectedChart());
            ClearChartCommand = new RelayCommand(o => ClearSelectedChart());
            AddSignalToActiveCommand = new RelayCommand(param => { if (param is string s) AddSignalToChart(s); });

            RemoveSignalFromActiveCommand = new RelayCommand(param =>
            {
                string toRemove = param as string ?? SelectedSignalToRemove;
                if (!string.IsNullOrEmpty(toRemove)) RemoveSignalFromChart(toRemove);
            });

            ApplyTimeFilterCommand = new RelayCommand(o => ApplyTimeFilter());
            ResetZoomCommand = new RelayCommand(o => ResetZoom());
            PlayCommand = new RelayCommand(o => StartPlayback());
            PauseCommand = new RelayCommand(o => StopPlayback());
            SpeedUpCommand = new RelayCommand(o => ChangeSpeed(true));
            SpeedDownCommand = new RelayCommand(o => ChangeSpeed(false));
            ZoomToStateCommand = new RelayCommand(ZoomToState);
            ZoomToAnalysisEventCommand = new RelayCommand(ZoomToAnalysisEvent);
            ClearAnalysisCommand = new RelayCommand(o => { AnalysisResults.Clear(); foreach (var c in Charts) { ClearThresholdMarkers(c); c.Model.InvalidatePlot(true); } });

            AddNewChart();
        }

        public async Task ProcessLogsAsync(IEnumerable<LogEntry> logs)
        {
            Status = "Processing Data...";
            var result = await _graphService.ParseLogsToGraphDataAsync(logs);
            _allData = result.Item1;
            ComponentTree = result.Item2;
            _allStates = result.Item3;

            OnPropertyChanged(nameof(ComponentTree));

            StateTimeline.Clear();
            if (_allStates != null) foreach (var s in _allStates) StateTimeline.Add(s);

            if (logs.Any())
            {
                var t1 = logs.First().Date;
                var t2 = logs.Last().Date;
                _logStartTime = t1 < t2 ? t1 : t2;
                _logEndTime = t1 > t2 ? t1 : t2;

                if ((_logEndTime - _logStartTime).TotalSeconds < 1) _logEndTime = _logStartTime.AddSeconds(1);

                SetAbsoluteBounds(_logStartTime, _logEndTime);
                ResetZoom();
            }

            foreach (var c in Charts) { c.Model.Annotations.Clear(); AddStateAnnotations(c.Model); c.Model.InvalidatePlot(true); }
            Status = $"Loaded {_allData.Count} signals.";
        }

        private void SetAbsoluteBounds(DateTime start, DateTime end)
        {
            double min = DateTimeAxis.ToDouble(start);
            double max = DateTimeAxis.ToDouble(end);
            if (max <= min) max = min + (1.0 / 86400.0);

            foreach (var c in Charts)
            {
                var xAxis = c.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (xAxis != null) { xAxis.AbsoluteMinimum = min; xAxis.AbsoluteMaximum = max; }
            }
        }

        public void SetTimeRange(DateTime start, DateTime end)
        {
            if (end <= start) end = start.AddMilliseconds(500);
            SetAbsoluteBounds(start, end);
            FilterStartTime = start;
            FilterEndTime = end;
            UpdateGraphsView(start, end);
        }

        private void FilterTree(string text)
        {
            if (ComponentTree == null) return;
            foreach (var node in ComponentTree) FilterNodeRecursive(node, text);
        }

        private bool FilterNodeRecursive(GraphNode node, string text)
        {
            bool match = string.IsNullOrEmpty(text) || node.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            bool childMatch = false;
            foreach (var child in node.Children)
            {
                if (FilterNodeRecursive(child, text)) childMatch = true;
            }
            if (childMatch) { node.IsVisible = true; node.IsExpanded = true; return true; }
            node.IsVisible = match;
            if (!match) node.IsExpanded = false;
            return match;
        }

        private void AddNewChart()
        {
            if (Charts.Count >= 5) return;
            var vm = new SingleChartViewModel($"Chart {Charts.Count + 1}");

            if (_logStartTime != DateTime.MinValue)
            {
                var ax = vm.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null)
                {
                    ax.AbsoluteMinimum = DateTimeAxis.ToDouble(_logStartTime);
                    ax.AbsoluteMaximum = DateTimeAxis.ToDouble(_logEndTime);
                    ax.Zoom(DateTimeAxis.ToDouble(FilterStartTime), DateTimeAxis.ToDouble(FilterEndTime));
                }
            }

            var xAxis = vm.Model.Axes.FirstOrDefault(a => a.Key == "X");
            if (xAxis != null)
            {
                xAxis.AxisChanged += (s, e) =>
                {
                    SyncAxes(xAxis);
                    UpdateStateLabelsVisibility(vm.Model);
                };
            }

            if (_allStates != null) AddStateAnnotations(vm.Model);
            Charts.Add(vm);
            SelectedChart = vm;
        }

        private void RemoveSelectedChart() { if (SelectedChart != null && Charts.Count > 1) { Charts.Remove(SelectedChart); SelectedChart = Charts.FirstOrDefault(); } }
        private void ClearSelectedChart() { if (SelectedChart != null) { SelectedChart.Model.Series.Clear(); SelectedChart.PlottedKeys.Clear(); ClearMeasurement(); SelectedChart.Model.InvalidatePlot(true); UpdateActiveSignalsList(); } }

        public void AddSignalToChart(string key)
        {
            if (SelectedChart == null) { MessageBox.Show("Please select a chart first."); return; }
            if (_allData == null || !_allData.ContainsKey(key)) { MessageBox.Show($"No data found for signal: {key}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (SelectedChart.PlottedKeys.Contains(key)) return;

            // --- תיקון CS0165: בדיקה מפורשת ללא pattern matching בתוך Lambda מורכב ---
            bool hasStates = false;
            foreach (var ann in SelectedChart.Model.Annotations)
            {
                if (ann is TextAnnotation ta)
                {
                    if (ta.Tag as string == "StateLabel" || ta.Tag is MachineStateSegment)
                    {
                        hasStates = true;
                        break;
                    }
                }
            }

            if (!hasStates && _allStates != null)
            {
                AddStateAnnotations(SelectedChart.Model);
            }

            var points = _allData[key];
            List<DataPoint> displayPoints = new List<DataPoint>();
            if (points.Count > 5000)
            {
                int step = points.Count / 5000;
                for (int i = 0; i < points.Count; i += step) displayPoints.Add(points[i]);
            }
            else { displayPoints = points; }

            var darkColors = new[] { OxyColors.Blue, OxyColors.Red, OxyColors.Green, OxyColors.OrangeRed, OxyColors.Purple, OxyColors.Teal };
            var lineColor = darkColors[SelectedChart.Model.Series.Count % darkColors.Length];
            bool isSinglePoint = displayPoints.Count == 1;

            var series = new LineSeries
            {
                Title = key.Split('.').Last(),
                Tag = key,
                Color = lineColor,
                StrokeThickness = 2,
                MarkerType = isSinglePoint ? MarkerType.Circle : MarkerType.None,
                MarkerSize = isSinglePoint ? 4 : 0,
                MarkerFill = isSinglePoint ? lineColor : OxyColors.Transparent,
                MarkerStroke = isSinglePoint ? OxyColors.White : OxyColors.Transparent
            };

            series.Points.AddRange(displayPoints);
            SelectedChart.Model.Series.Add(series);
            SelectedChart.PlottedKeys.Add(key);

            string newTitle = string.Join(", ", SelectedChart.Model.Series.OfType<LineSeries>().Select(s => s.Title));
            SelectedChart.UpdateTitle(newTitle);

            SelectedChart.Model.InvalidatePlot(true);
            UpdateActiveSignalsList();
            UpdateGraphsView(FilterStartTime, FilterEndTime);
        }

        public void RemoveSignalFromChart(string key)
        {
            if (SelectedChart == null) return;
            var series = SelectedChart.Model.Series.FirstOrDefault(s => (string)s.Tag == key);
            if (series != null)
            {
                SelectedChart.Model.Series.Remove(series);
                SelectedChart.PlottedKeys.Remove(key);

                string newTitle = string.Join(", ", SelectedChart.Model.Series.OfType<LineSeries>().Select(s => s.Title));
                SelectedChart.UpdateTitle(newTitle);

                SelectedChart.Model.InvalidatePlot(true);
                UpdateActiveSignalsList();
                UpdateGraphsView(FilterStartTime, FilterEndTime);
            }
        }

        private void UpdateActiveSignalsList()
        {
            ActiveChartSignals.Clear();
            if (SelectedChart != null) foreach (var k in SelectedChart.PlottedKeys) ActiveChartSignals.Add(k);
        }

        private void SyncAxes(Axis sourceAxis)
        {
            if (_isSyncing || IsPlaying) return;
            _isSyncing = true;
            try
            {
                FilterStartTime = DateTimeAxis.ToDateTime(sourceAxis.ActualMinimum);
                FilterEndTime = DateTimeAxis.ToDateTime(sourceAxis.ActualMaximum);
            }
            catch { }
            foreach (var chart in Charts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null && ax != sourceAxis) { ax.Zoom(sourceAxis.ActualMinimum, sourceAxis.ActualMaximum); chart.Model.InvalidatePlot(false); }
                AutoZoomYAxis(chart, sourceAxis.ActualMinimum, sourceAxis.ActualMaximum);
            }
            _isSyncing = false;
        }

        public void UpdateGraphsView(DateTime start, DateTime end)
        {
            if (start >= end) return;
            FilterStartTime = start; FilterEndTime = end;
            double min = DateTimeAxis.ToDouble(start); double max = DateTimeAxis.ToDouble(end);
            foreach (var chart in Charts)
            {
                var ax = chart.Model.Axes.FirstOrDefault(a => a.Key == "X");
                if (ax != null) ax.Zoom(min, max);
                AutoZoomYAxis(chart, min, max);
                UpdateStateLabelsVisibility(chart.Model);
                chart.Model.InvalidatePlot(true);
            }
        }

        private void AutoZoomYAxis(SingleChartViewModel chart, double minX, double maxX)
        {
            var yAxis = chart.Model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (yAxis == null) return;
            double yMin = double.MaxValue; double yMax = double.MinValue; bool found = false;
            foreach (var s in chart.Model.Series.OfType<LineSeries>())
            {
                foreach (var p in s.Points)
                {
                    if (p.X >= minX && p.X <= maxX) { found = true; if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y; }
                }
            }
            if (found && yMin != double.MaxValue)
            {
                double pad = (yMax - yMin) * 0.1;
                if (pad == 0) pad = 1;
                yAxis.Zoom(yMin - pad, yMax + pad);
            }
        }

        private void ResetZoom()
        {
            if (_logStartTime == DateTime.MinValue) return;
            SetAbsoluteBounds(_logStartTime, _logEndTime);
            FilterStartTime = _logStartTime;
            FilterEndTime = _logEndTime;
            UpdateGraphsView(_logStartTime, _logEndTime);
        }

        private void ApplyTimeFilter() => SetTimeRange(FilterStartTime, FilterEndTime);

        private void ClearMeasurement() { /* כפתור מדידה הוסר */ }

        private void AddStateAnnotations(PlotModel model)
        {
            if (_allStates == null) return;

            if (!model.Annotations.Any(a => a is LineAnnotation la && la.Y == 0))
            {
                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = 0,
                    Color = OxyColors.Black,
                    StrokeThickness = 1.5,
                    LineStyle = LineStyle.Solid,
                    Layer = AnnotationLayer.BelowSeries,
                    Text = "0"
                });
            }

            foreach (var seg in _allStates)
            {
                model.Annotations.Add(new RectangleAnnotation
                {
                    MinimumX = seg.Start,
                    MaximumX = seg.End,
                    Fill = OxyColor.FromAColor(40, seg.Color),
                    Layer = AnnotationLayer.BelowSeries
                });

                var textAnn = new TextAnnotation
                {
                    Text = seg.Name,
                    TextPosition = new DataPoint((seg.Start + seg.End) / 2, 0),
                    TextColor = OxyColors.Black,
                    Background = OxyColor.FromAColor(150, OxyColors.White),
                    Padding = new OxyThickness(3),
                    Stroke = OxyColors.Gray,
                    StrokeThickness = 1,
                    Tag = seg,
                    Layer = AnnotationLayer.AboveSeries,
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Top
                };
                model.Annotations.Add(textAnn);
            }
        }

        private void UpdateStateLabelsVisibility(PlotModel model)
        {
            var xAxis = model.Axes.FirstOrDefault(a => a.Key == "X");
            var yAxis = model.Axes.FirstOrDefault(a => a.Key == "Y");
            if (xAxis == null || yAxis == null) return;

            double viewRange = xAxis.ActualMaximum - xAxis.ActualMinimum;
            double yTop = yAxis.ActualMaximum;

            foreach (var ann in model.Annotations.OfType<TextAnnotation>())
            {
                if (ann.Tag is MachineStateSegment seg)
                {
                    double segDuration = seg.End - seg.Start;
                    bool isVisible = (segDuration / viewRange) > 0.05;

                    ann.TextColor = isVisible ? OxyColors.Black : OxyColors.Transparent;
                    ann.Background = isVisible ? OxyColor.FromAColor(150, OxyColors.White) : OxyColors.Transparent;
                    ann.Stroke = isVisible ? OxyColors.Gray : OxyColors.Transparent;

                    ann.TextPosition = new DataPoint(ann.TextPosition.X, yTop - (yAxis.ActualMaximum - yAxis.ActualMinimum) * 0.05);
                }
            }
        }

        private void ZoomToState(object param)
        {
            if (param is MachineStateSegment s)
            {
                SetTimeRange(s.StartTimeValue, s.EndTimeValue);
            }
        }

        private void ZoomToAnalysisEvent(object param) { if (param is AnalysisEvent ev) UpdateGraphsView(ev.PeakTime.AddSeconds(-10), ev.PeakTime.AddSeconds(10)); }
        private void ClearThresholdMarkers(SingleChartViewModel c) { var toRemove = c.Model.Annotations.Where(a => a is PointAnnotation pa && pa.Shape == MarkerType.Diamond).ToList(); foreach (var a in toRemove) c.Model.Annotations.Remove(a); }

        private void StartPlayback()
        {
            if ((_logEndTime - FilterEndTime).TotalSeconds < 5)
            {
                double currentDuration = (FilterEndTime - FilterStartTime).TotalSeconds;
                DateTime newStart = _logStartTime;
                DateTime newEnd = newStart.AddSeconds(currentDuration);
                SetTimeRange(newStart, newEnd);
            }
            IsPlaying = true;
            _playbackTimer.Start();
        }

        private void StopPlayback() { IsPlaying = false; _playbackTimer.Stop(); }
        private void ChangeSpeed(bool increase)
        {
            var speeds = new[] { 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0 };
            int currentIndex = Array.FindIndex(speeds, s => s >= _playbackSpeed);
            if (currentIndex == -1) currentIndex = 3;
            if (increase && currentIndex < speeds.Length - 1) _playbackSpeed = speeds[currentIndex + 1];
            else if (!increase && currentIndex > 0) _playbackSpeed = speeds[currentIndex - 1];
            OnPropertyChanged(nameof(PlaybackSpeedText));
        }
        private void OnPlaybackTick(object sender, EventArgs e)
        {
            if (!IsPlaying) return;
            double stepSeconds = 0.5 * _playbackSpeed;
            DateTime newStart = FilterStartTime.AddSeconds(stepSeconds);
            DateTime newEnd = FilterEndTime.AddSeconds(stepSeconds);

            if (newEnd > _logEndTime)
            {
                StopPlayback();
                return;
            }

            UpdateGraphsView(newStart, newEnd);
        }

        private OxyColor GetNextColor(int i) { var colors = new[] { OxyColors.Blue, OxyColors.Red, OxyColors.Green, OxyColors.OrangeRed, OxyColors.Purple, OxyColors.Teal }; return colors[i % colors.Length]; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}