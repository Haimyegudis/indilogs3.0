using System;
using System.Deployment.Application; // Ensure System.Deployment reference exists
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class UpdateService
    {
        public async Task CheckForUpdatesSimpleAsync()
        {
            await Task.Run(() =>
            {
                // Check if running as ClickOnce
                if (ApplicationDeployment.IsNetworkDeployed)
                {
                    try
                    {
                        var deployment = ApplicationDeployment.CurrentDeployment;

                        // Check for update
                        UpdateCheckInfo info = deployment.CheckForDetailedUpdate();

                        if (info.UpdateAvailable)
                        {
                            // Back to UI thread
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var result = MessageBox.Show(
                                    $"A new version is available: {info.AvailableVersion}\n" +
                                    $"Current version: {deployment.CurrentVersion}\n\n" +
                                    "Do you want to update now?",
                                    "Update Available",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);

                                if (result == MessageBoxResult.Yes)
                                {
                                    deployment.Update();
                                    MessageBox.Show("The application has been updated and will now restart.");
                                    System.Windows.Forms.Application.Restart();
                                    Application.Current.Shutdown();
                                }
                            });
                        }
                    }
                    catch (Exception)
                    {
                        // Silent failure for network issues
                    }
                }
            });
        }
    }
}