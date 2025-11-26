using System;
using System.IO;

namespace IndiLogs_3._0.Services
{
    public static class UpdateLogger
    {
        private static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "update_debug.log");

        public static void Log(string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, logEntry);
            }
            catch
            {
                // התעלמות משגיאות כתיבה ללוג כדי לא להקריס את התוכנה
            }
        }

        public static void Log(string context, Exception ex)
        {
            Log($"[ERROR] {context}: {ex.Message}\nStack: {ex.StackTrace}");
        }
    }
}