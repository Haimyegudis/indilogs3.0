using System;
using System.Windows;
using IndiLogs_3._0.ViewModels;

namespace IndiLogs_3._0.Views
{
    public partial class FloatingChartWindow : Window
    {
        private readonly Action<SingleChartViewModel> _onCloseCallback;
        private readonly SingleChartViewModel _vm;

        // הוספנו callback שמופעל בסגירה
        public FloatingChartWindow(SingleChartViewModel vm, Action<SingleChartViewModel> onClose)
        {
            InitializeComponent();
            _vm = vm;
            _onCloseCallback = onClose;
            this.DataContext = vm;

            this.Closed += FloatingChartWindow_Closed;
        }

        private void FloatingChartWindow_Closed(object sender, EventArgs e)
        {
            _onCloseCallback?.Invoke(_vm);
        }
    }
}