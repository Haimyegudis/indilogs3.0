using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IndiLogs_3._0.Views
{
    public partial class ColorPaletteWindow : Window
    {
        public Color SelectedColor { get; private set; }

        public ColorPaletteWindow()
        {
            InitializeComponent();
            LoadColors();
        }

        private void LoadColors()
        {
            // רשימת צבעים ידנית שנראית טוב על רקע כהה (פסטלים וצבעים חיים)
            var colorList = new List<SolidColorBrush>();

            // שורה 1: אדומים/כתומים
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 179, 186))); // Pastel Red
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 105, 97)));  // Salmon
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 87, 87)));   // Red
            colorList.Add(new SolidColorBrush(Color.FromRgb(204, 0, 0)));     // Dark Red
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 223, 186))); // Pastel Orange
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 179, 71)));  // Orange
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 140, 0)));   // Dark Orange
            colorList.Add(new SolidColorBrush(Color.FromRgb(139, 69, 19)));   // Brown

            // שורה 2: צהובים/ירוקים
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 255, 186))); // Pastel Yellow
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 255, 84)));  // Yellow
            colorList.Add(new SolidColorBrush(Color.FromRgb(186, 255, 201))); // Pastel Green
            colorList.Add(new SolidColorBrush(Color.FromRgb(144, 238, 144))); // Light Green
            colorList.Add(new SolidColorBrush(Color.FromRgb(60, 179, 113)));  // Sea Green
            colorList.Add(new SolidColorBrush(Color.FromRgb(34, 139, 34)));   // Forest Green
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 100, 0)));     // Dark Green
            colorList.Add(new SolidColorBrush(Color.FromRgb(85, 107, 47)));   // Olive

            // שורה 3: כחולים/תכלת
            colorList.Add(new SolidColorBrush(Color.FromRgb(186, 225, 255))); // Pastel Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(135, 206, 250))); // Sky Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(100, 149, 237))); // Cornflower
            colorList.Add(new SolidColorBrush(Color.FromRgb(65, 105, 225)));  // Royal Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 0, 255)));     // Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 0, 139)));     // Dark Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(25, 25, 112)));   // Midnight Blue
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 128, 128)));   // Teal

            // שורה 4: סגולים/ורודים
            colorList.Add(new SolidColorBrush(Color.FromRgb(230, 230, 250))); // Lavender
            colorList.Add(new SolidColorBrush(Color.FromRgb(216, 191, 216))); // Thistle
            colorList.Add(new SolidColorBrush(Color.FromRgb(221, 160, 221))); // Plum
            colorList.Add(new SolidColorBrush(Color.FromRgb(238, 130, 238))); // Violet
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 0, 255)));   // Magenta
            colorList.Add(new SolidColorBrush(Color.FromRgb(148, 0, 211)));   // Violet Dark
            colorList.Add(new SolidColorBrush(Color.FromRgb(75, 0, 130)));    // Indigo
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 20, 147)));  // Deep Pink

            // שורה 5: אפורים/מיוחדים
            colorList.Add(new SolidColorBrush(Color.FromRgb(255, 255, 255))); // White
            colorList.Add(new SolidColorBrush(Color.FromRgb(211, 211, 211))); // Light Gray
            colorList.Add(new SolidColorBrush(Color.FromRgb(169, 169, 169))); // Dark Gray
            colorList.Add(new SolidColorBrush(Color.FromRgb(105, 105, 105))); // Dim Gray
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 0, 0)));       // Black
            colorList.Add(new SolidColorBrush(Color.FromRgb(128, 0, 0)));     // Maroon
            colorList.Add(new SolidColorBrush(Color.FromRgb(128, 128, 0)));   // Olive
            colorList.Add(new SolidColorBrush(Color.FromRgb(0, 128, 128)));   // Teal

            ColorsGrid.ItemsSource = colorList;
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Background is SolidColorBrush brush)
            {
                SelectedColor = brush.Color;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}