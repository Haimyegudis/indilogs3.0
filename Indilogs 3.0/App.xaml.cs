using IndiLogs_3._0.Services;
using System.Windows;

namespace IndiLogs_3._0
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. פתיחת החלון הראשי מיד
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Show();

            // 2. הרצת בדיקת העדכון ברקע (כמו בתוכנה הישנה)
            var updateService = new UpdateService();
            await updateService.CheckForUpdatesSimpleAsync();
        }
    }
}