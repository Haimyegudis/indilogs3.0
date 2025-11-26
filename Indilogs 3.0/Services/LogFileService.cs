using IndiLogs_3._0.Models;
using Indigo.Infra.ICL.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public class LogFileService
    {
        // שינוי: ה-Progress מקבל כעת Tuple של (אחוז, הודעה)
        // שנה את החתימה לקבלת IProgress<(double, string)>
        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress)
        {
            return await Task.Run(() =>
            {
                var session = new LogSessionData();
                if (filePaths == null || filePaths.Length == 0) return session;

                long totalBytesAllFiles = 0;
                foreach (var p in filePaths)
                    if (File.Exists(p)) totalBytesAllFiles += new FileInfo(p).Length;

                long processedBytesGlobal = 0;

                try
                {
                    foreach (var filePath in filePaths)
                    {
                        if (!File.Exists(filePath)) continue;

                        string extension = Path.GetExtension(filePath).ToLower();
                        long currentFileSize = new FileInfo(filePath).Length;
                        string fileName = Path.GetFileName(filePath);

                        // דיווח התחלתי לקובץ
                        progress?.Report((CalculatePercent(processedBytesGlobal, totalBytesAllFiles), $"Opening {fileName}..."));

                        if (extension == ".zip")
                        {
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                long totalZipSize = archive.Entries.Sum(e => e.Length);
                                long currentZipProcessed = 0;

                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.Length == 0) continue;

                                    // דיווח על הפעולה הנוכחית
                                    string action = "Processing";
                                    if (entry.Name.EndsWith(".log")) action = "Parsing Log";
                                    else if (entry.Name.EndsWith(".csv")) action = "Reading Events";
                                    else if (entry.Name.EndsWith(".png")) action = "Loading Image";

                                    // חישוב אחוזים
                                    currentZipProcessed += entry.Length;
                                    double fileContribution = ((double)currentZipProcessed / totalZipSize) * currentFileSize;
                                    double totalProgress = ((processedBytesGlobal + fileContribution) / totalBytesAllFiles) * 100;

                                    // שליחת דיווח: אחוז + הודעה
                                    progress?.Report((Math.Min(99, totalProgress), $"{action}: {entry.Name}"));

                                    // לוגיקת הטעינה (זהה למקור)
                                    if (entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        entry.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                            session.Logs.AddRange(ParseLogStream(ms));
                                    }
                                    else if (entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                            session.Events.AddRange(ParseEventsCsv(ms));
                                    }
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var ms = CopyToMemory(entry);
                                        var bitmap = LoadBitmapFromStream(ms);
                                        if (bitmap != null) session.Screenshots.Add(bitmap);
                                    }
                                    else if (entry.Name.Equals("Readme.txt", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                        using (var r = new StreamReader(ms))
                                        {
                                            session.PressConfiguration = r.ReadToEnd();
                                            session.VersionsInfo = ExtractVersionsFromReadme(session.PressConfiguration);
                                        }
                                    }
                                    else if (entry.Name.EndsWith("_setupInfo.json", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using (var ms = CopyToMemory(entry))
                                        using (var r = new StreamReader(ms))
                                            session.SetupInfo = r.ReadToEnd();
                                    }
                                }
                            }
                        }
                        else
                        {
                            // טיפול בקבצים רגילים
                            progress?.Report((CalculatePercent(processedBytesGlobal, totalBytesAllFiles), $"Reading {fileName}..."));

                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var ms = new MemoryStream())
                            {
                                fs.CopyTo(ms);
                                ms.Position = 0;
                                session.Logs.AddRange(ParseLogStream(ms));
                            }
                        }

                        processedBytesGlobal += currentFileSize;
                    }

                    progress?.Report((99, "Sorting & Finalizing..."));

                    if (session.Logs.Count > 0)
                        session.Logs = session.Logs.OrderByDescending(x => x.Date).ToList();

                    if (session.Events.Count > 0)
                        session.Events = session.Events.OrderByDescending(x => x.Time).ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {ex.Message}");
                }

                return session;
            });
        }

        private double CalculatePercent(long processed, long total)
        {
            if (total == 0) return 0;
            return Math.Min(99, ((double)processed / total) * 100);
        }

        public List<LogEntry> ParseLogStream(Stream stream)
        {
            var list = new List<LogEntry>();
            try
            {
                var reader = new IndigoLogsReader(stream);
                while (reader.MoveToNext())
                {
                    if (reader.Current != null)
                    {
                        list.Add(new LogEntry
                        {
                            Level = reader.Current.Level?.ToString() ?? "Info",
                            Date = reader.Current.Time,
                            Message = reader.Current.Message ?? "",
                            ThreadName = reader.Current.ThreadName ?? "",
                            Logger = reader.Current.LoggerName ?? "",
                            ProcessName = reader.Current["ProcessName"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        private List<EventEntry> ParseEventsCsv(Stream stream)
        {
            var list = new List<EventEntry>();
            try
            {
                using (var reader = new StreamReader(stream))
                {
                    string header = reader.ReadLine();
                    if (header == null) return list;

                    var headers = header.Split(',');
                    int timeIdx = Array.IndexOf(headers, "Time");
                    int nameIdx = Array.IndexOf(headers, "Name");
                    int stateIdx = Array.IndexOf(headers, "State");
                    int severityIdx = Array.IndexOf(headers, "Severity");
                    int subsystemIdx = Array.IndexOf(headers, "Subsystem");

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = SplitCsvLine(line);

                        if (parts.Count > timeIdx && timeIdx >= 0)
                        {
                            string timeStr = parts[timeIdx].Trim('"');
                            if (DateTime.TryParse(timeStr, out DateTime time))
                            {
                                var entry = new EventEntry
                                {
                                    Time = time,
                                    Name = (nameIdx >= 0 && parts.Count > nameIdx) ? parts[nameIdx] : "",
                                    State = (stateIdx >= 0 && parts.Count > stateIdx) ? parts[stateIdx] : "",
                                    Severity = (severityIdx >= 0 && parts.Count > severityIdx) ? parts[severityIdx] : "",
                                    Description = (subsystemIdx >= 0 && parts.Count > subsystemIdx) ? parts[subsystemIdx] : ""
                                };

                                if (string.IsNullOrWhiteSpace(entry.Name) && string.IsNullOrWhiteSpace(entry.Description))
                                    continue;

                                list.Add(entry);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CSV ERROR] {ex.Message}");
            }
            return list;
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool inBraces = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == '{')
                {
                    inBraces = true;
                    current.Append(c);
                }
                else if (c == '}')
                {
                    inBraces = false;
                    current.Append(c);
                }
                else if (c == ',' && !inQuotes && !inBraces)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        private MemoryStream CopyToMemory(ZipArchiveEntry entry)
        {
            var ms = new MemoryStream();
            using (var stream = entry.Open()) { stream.CopyTo(ms); }
            ms.Position = 0;
            return ms;
        }

        private BitmapImage LoadBitmapFromStream(MemoryStream stream)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private string ExtractVersionsFromReadme(string content)
        {
            try
            {
                var sw = Regex.Match(content, @"Version[:=]\s*(.+)", RegexOptions.IgnoreCase);
                var plc = Regex.Match(content, @"PressPlcVersion[:=]\s*(.+)", RegexOptions.IgnoreCase);
                return $"SW: {(sw.Success ? sw.Groups[1].Value.Trim() : "Unknown")} | PLC: {(plc.Success ? plc.Groups[1].Value.Trim() : "Unknown")}";
            }
            catch { return ""; }
        }
    }
}