using System;
using System.IO;

namespace IndiLogs_3._0.Services
{
    public static class DebugHelper
    {
        private static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "IndiLogs_Debug.txt");

        public static void Log(string message)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} | {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
            catch { } // התעלמות משגיאות כתיבה ללוג הדיבוג
        }
    }
}