using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class ColoringWindow : Window
    {
        public List<ColoringCondition> ResultConditions { get; private set; }

        private readonly string[] _fields = new[] { "Level", "Message", "ThreadName", "Logger" };
        // הוספנו כאן את ה-Regex
        private readonly string[] _operators = new[] { "Contains", "Equals", "Begins With", "Ends With", "Regex" };

        public ColoringWindow()
        {
            InitializeComponent();
            ResultConditions = new List<ColoringCondition>();
        }

        public void LoadSavedRules(List<ColoringCondition> savedRules)
        {
            ConditionsPanel.Children.Clear();

            if (savedRules != null && savedRules.Count > 0)
            {
                foreach (var rule in savedRules)
                {
                    AddConditionRow(rule);
                }
            }
            else
            {
                AddConditionRow();
            }
        }

        private void AddCondition_Click(object sender, RoutedEventArgs e) => AddConditionRow();

        private void AddConditionRow(ColoringCondition existingRule = null)
        {
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            // ComboBox: Field
            var cbField = new ComboBox { ItemsSource = _fields, Margin = new Thickness(2) };
            if (existingRule != null && !string.IsNullOrEmpty(existingRule.Field))
                cbField.SelectedItem = existingRule.Field;
            else
                cbField.SelectedIndex = 1; // Message

            // ComboBox: Operator
            var cbOperator = new ComboBox { ItemsSource = _operators, Margin = new Thickness(2) };
            if (existingRule != null && !string.IsNullOrEmpty(existingRule.Operator))
                cbOperator.SelectedItem = existingRule.Operator;
            else
                cbOperator.SelectedIndex = 0; // Contains

            // TextBox: Value
            var txtValue = new TextBox
            {
                Text = existingRule?.Value ?? "",
                Margin = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Button: Color
            var btnColor = new Button
            {
                Content = "Color",
                Margin = new Thickness(2),
                Background = existingRule != null ? new SolidColorBrush(existingRule.Color) : Brushes.LightGray,
                Tag = existingRule != null ? existingRule.Color : Color.FromRgb(255, 204, 204)
            };
            btnColor.Click += (s, e) => PickColor(btnColor);

            // Button: Remove
            var btnRemove = new Button
            {
                Content = "✕",
                Foreground = Brushes.Red,
                Margin = new Thickness(2),
                FontWeight = FontWeights.Bold
            };
            btnRemove.Click += (s, e) => ConditionsPanel.Children.Remove(rowGrid);

            Grid.SetColumn(cbField, 0);
            Grid.SetColumn(cbOperator, 1);
            Grid.SetColumn(txtValue, 2);
            Grid.SetColumn(btnColor, 3);
            Grid.SetColumn(btnRemove, 4);

            rowGrid.Children.Add(cbField);
            rowGrid.Children.Add(cbOperator);
            rowGrid.Children.Add(txtValue);
            rowGrid.Children.Add(btnColor);
            rowGrid.Children.Add(btnRemove);

            ConditionsPanel.Children.Add(rowGrid);
        }

        private void PickColor(Button btn)
        {
            // יצירת החלון החדש שלנו
            var paletteWindow = new ColorPaletteWindow();

            // אופציונלי: פתיחת החלון ליד הכפתור שנלחץ (כדי שיראה כמו Popup)
            Point location = btn.PointToScreen(new Point(0, 0));
            paletteWindow.Left = location.X;
            paletteWindow.Top = location.Y + btn.ActualHeight + 5;

            // פתיחת החלון כ-Dialog
            if (paletteWindow.ShowDialog() == true)
            {
                var c = paletteWindow.SelectedColor;

                // עדכון הכפתור בצבע הנבחר
                btn.Background = new SolidColorBrush(c);
                btn.Tag = c;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ResultConditions.Clear();

            foreach (Grid row in ConditionsPanel.Children)
            {
                var field = (row.Children[0] as ComboBox)?.SelectedItem?.ToString();
                var op = (row.Children[1] as ComboBox)?.SelectedItem?.ToString();
                var val = (row.Children[2] as TextBox)?.Text;
                var colorBtn = row.Children[3] as Button;

                Color color = (colorBtn?.Tag is Color c) ? c : Color.FromRgb(255, 204, 204);

                if (!string.IsNullOrWhiteSpace(field) &&
                    !string.IsNullOrWhiteSpace(op) &&
                    !string.IsNullOrWhiteSpace(val))
                {
                    ResultConditions.Add(new ColoringCondition
                    {
                        Field = field,
                        Operator = op,
                        Value = val,
                        Color = color
                    });
                }
            }

            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ConditionsPanel.Children.Clear();
            AddConditionRow();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}