using IndiLogs_3._0.ViewModels;
using System.Windows;

namespace IndiLogs_3._0.Views
{
    public partial class FontsWindow : Window
    {
        private string _originalFont;
        private MainViewModel _viewModel;

        public FontsWindow()
        {
            InitializeComponent();

            // שמירת הפונט המקורי ברגע שהחלון נטען
            this.Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    _viewModel = vm;
                    _originalFont = vm.SelectedFont;
                }
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // המשתמש אישר - משאירים את הבחירה וסוגרים
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // המשתמש ביטל - אם יש לנו את הפונט המקורי, נחזיר אותו
            if (_viewModel != null && _originalFont != null)
            {
                _viewModel.SelectedFont = _originalFont;
            }

            DialogResult = false;
            Close();
        }
    }
}