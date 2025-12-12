// BILINGUAL-HEADER-START
// EN: File: VisualGraphService.cs - Service for generating Gantt charts from logs.
// HE: קובץ: VisualGraphService.cs - שירות ליצירת תרשימי גאנט מלוגים.

using IndiLogs_3._0.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace IndiLogs_3._0.Services
{
    public class VisualGraphService
    {
        public async Task<PlotModel> CreateGanttModelAsync(List<StateEntry> states, List<LogEntry> errorLogs)
        {
            return await Task.Run(() =>
            {
                var model = new PlotModel
                {
                    Title = "Process Timeline Analysis",
                    TextColor = OxyColors.White,
                    PlotAreaBorderColor = OxyColors.Gray,
                    Background = OxyColor.Parse("#1E1E24"), // Dark theme background
                    IsLegendVisible = true,
                    TitleFontSize = 14
                };

                // 1. ציר הזמן (X Axis - Time)
                var dateAxis = new DateTimeAxis
                {
                    Position = AxisPosition.Bottom,
                    StringFormat = "HH:mm:ss",
                    Title = "Time",
                    MajorGridlineStyle = LineStyle.Solid,
                    MinorGridlineStyle = LineStyle.Dot,
                    MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.White),
                    TextColor = OxyColors.White,
                    TitleColor = OxyColors.Gray
                };
                model.Axes.Add(dateAxis);

                // 2. ציר הקטגוריות (Y Axis - States/Processes)
                var categoryAxis = new CategoryAxis
                {
                    Position = AxisPosition.Left,
                    Title = "State / Process",
                    TextColor = OxyColors.White,
                    GapWidth = 0.1,
                    TitleColor = OxyColors.Gray
                };

                // אוספים שמות ייחודיים של סטייטים כדי ליצור קטגוריות בציר ה-Y
                // נסדר אותם לפי סדר ההופעה שלהם כרונולוגית
                var uniqueStates = states.OrderBy(s => s.StartTime).Select(s => s.StateName).Distinct().ToList();
                categoryAxis.Labels.AddRange(uniqueStates);
                model.Axes.Add(categoryAxis);

                // 3. סדרת הברים (Gantt Series)
                var intervalSeries = new IntervalBarSeries
                {
                    Title = "State Duration",
                    StrokeThickness = 1,
                    // פורמט הטולטיפ: שם הסטייט, התחלה, סוף, משך
                    TrackerFormatString = "{2}\nStart: {3:HH:mm:ss}\nEnd: {4:HH:mm:ss}\nDuration: {5:0.00}s"
                };

                foreach (var state in states)
                {
                    int categoryIndex = uniqueStates.IndexOf(state.StateName);
                    if (categoryIndex == -1) continue;

                    // לוגיקה לבחירת צבע הבר
                    OxyColor color = OxyColors.SeaGreen; // ברירת מחדל - ירוק

                    if (state.Status == "FAILED" || state.StateName.Contains("OFF") || state.StateName.Contains("FAULT"))
                        color = OxyColors.IndianRed;
                    else if (state.StateName.Contains("INIT") || state.StateName.Contains("GET_READY"))
                        color = OxyColors.Orange;

                    var item = new IntervalBarItem
                    {
                        Start = DateTimeAxis.ToDouble(state.StartTime),
                        End = DateTimeAxis.ToDouble(state.EndTime ?? state.StartTime.AddSeconds(1)), // הגנה מפני Null
                        CategoryIndex = categoryIndex,
                        Color = color,
                        Title = state.TransitionTitle // כותרת עבור ה-Tracker
                    };

                    intervalSeries.Items.Add(item);
                }

                model.Series.Add(intervalSeries);

                // 4. סדרת אירועים/שגיאות (Event Markers)
                if (errorLogs != null && errorLogs.Any())
                {
                    var errorSeries = new ScatterSeries
                    {
                        Title = "Errors / Events",
                        MarkerType = MarkerType.Diamond,
                        MarkerSize = 5,
                        MarkerFill = OxyColors.Red,
                        MarkerStroke = OxyColors.White,
                        TrackerFormatString = "Error: {Tag}\nTime: {2:HH:mm:ss}"
                    };

                    foreach (var err in errorLogs)
                    {
                        // מציאת הסטייט שהשגיאה התרחשה בו כדי למקם אותה בגובה המתאים
                        double yVal = -1;
                        var relatedState = states.FirstOrDefault(s => s.StartTime <= err.Date && (s.EndTime ?? DateTime.MaxValue) >= err.Date);

                        if (relatedState != null)
                            yVal = uniqueStates.IndexOf(relatedState.StateName);

                        // אם לא נמצא סטייט, נמקם בתחתית
                        if (yVal == -1) yVal = 0;

                        // הוספת הנקודה. ה-Tag מכיל את ההודעה המלאה
                        errorSeries.Points.Add(new ScatterPoint(DateTimeAxis.ToDouble(err.Date), yVal) { Tag = err.Message });
                    }
                    model.Series.Add(errorSeries);
                }

                return model;
            });
        }
    }
}