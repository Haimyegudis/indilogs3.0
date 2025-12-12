using System.Collections.Generic;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Models.Analysis;

namespace IndiLogs_3._0.Views
{
    public partial class AnalysisReportWindow : Window
    {
        // המאפיין שאליו ה-ListBox בצד שמאל מתחבר
        public List<AnalysisResult> AllResults { get; set; }

        public AnalysisReportWindow(List<AnalysisResult> results)
        {
            InitializeComponent();
            AllResults = results;

            // קישור הנתונים לחלון עצמו
            this.DataContext = this;

            // בחירה אוטומטית של הריצה הראשונה (אם קיימת) כדי שהמשתמש יראה משהו מיד
            if (AllResults != null && AllResults.Any())
            {
                RunsList.SelectedIndex = 0;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}