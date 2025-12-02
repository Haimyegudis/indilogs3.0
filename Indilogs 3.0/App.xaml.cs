using IndiLogs_3._0.Services;
using System.Windows;

namespace IndiLogs_3._0
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // יצירת החלון הראשי
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;

            // אין צורך להעביר את הארגומנטים כאן (e.Args) 
            // כי MainWindow.xaml.cs כבר בודק את Environment.GetCommandLineArgs() בעצמו.

            mainWindow.Show();

            // בדיקת עדכונים ברקע
            try
            {
                var updateService = new UpdateService();
                await updateService.CheckForUpdatesSimpleAsync();
            }
            catch
            {
                // התעלמות משגיאות אם אין אינטרנט או שרת העדכונים למטה
            }
        }
    }
}