using IndiLogs_3._0.Models;
using Indigo.Infra.ICL.Core.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IndiLogs_3._0.Services
{
    public class LogFileService
    {
        // Regex מותאם לפורמט APPDEV (וכעת גם ל-PRESS.HOST.APP)
        private const string AppDevRegexPattern =
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})\x1e" +
            @"(?<Thread>[^\x1e]*)\x1e" +
            @"(?<RootIFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowId>[^\x1e]*)\x1e" +
            @"(?<IFlowName>[^\x1e]*)\x1e" +
            @"(?<Pattern>[^\x1e]*)\x1e" +
            @"(?<Context>[^\x1e]*)\x1e" +
            @"(?<Level>\w+)\s(?<Logger>[^\x1e]*)\x1e" +
            @"(?<Location>[^\x1e]*)\x1e" +
            @"(?<Message>.*?)\x1e" +
            @"(?<Exception>.*?)\x1e" +
            @"(?<Data>.*?)(\x1e|$)";

        private readonly Regex _appDevRegex = new Regex(AppDevRegexPattern, RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly Regex _dateStartPattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}", RegexOptions.Compiled);

        public async Task<LogSessionData> LoadSessionAsync(string[] filePaths, IProgress<(double, string)> progress)
        {
            return await Task.Run(() =>
            {
                var session = new LogSessionData();
                if (filePaths == null || filePaths.Length == 0) return session;

                var logsBag = new ConcurrentBag<LogEntry>();
                var appDevLogsBag = new ConcurrentBag<LogEntry>();
                var eventsBag = new ConcurrentBag<EventEntry>();
                var screenshotsBag = new ConcurrentBag<BitmapImage>();

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

                        progress?.Report((CalculatePercent(processedBytesGlobal, totalBytesAllFiles), $"Reading {fileName}..."));

                        if (extension == ".zip")
                        {
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                var filesToProcess = new List<ZipEntryData>();
                                long totalEntries = archive.Entries.Count;
                                long extractedCount = 0;

                                foreach (var entry in archive.Entries)
                                {
                                    extractedCount++;

                                    // דיווח התקדמות: אופטימיזציה - דיווח כל 100 קבצים במקום 10
                                    if (extractedCount % 100 == 0)
                                    {
                                        double extractRatio = (double)extractedCount / totalEntries;
                                        double fileProgress = (extractRatio * 0.5) * currentFileSize;
                                        double totalPercent = ((processedBytesGlobal + fileProgress) / totalBytesAllFiles) * 100;
                                        progress?.Report((Math.Min(99, totalPercent), $"Scanning zip: {entry.Name}"));
                                    }

                                    if (entry.Length == 0) continue;

                                    // --- אופטימיזציה: סינון קבצים מיותרים ---
                                    // דלג על קבצים שנראים כמו גיבויים או קבצים זמניים
                                    if (entry.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                                        entry.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                                        entry.Name.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        continue;
                                    }

                                    var entryData = new ZipEntryData { Name = entry.Name };

                                    // 1. זיהוי APP LOGS (לפי נתיב ושם קובץ)
                                    bool isAppDevPath = entry.FullName.IndexOf("IndigoLogs/Logger Files", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        entry.FullName.IndexOf("IndigoLogs\\Logger Files", StringComparison.OrdinalIgnoreCase) >= 0;

                                    bool isAppLogName = entry.Name.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        entry.Name.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0;

                                    // 2. זיהוי MAIN LOGS - טוען רק engineGroupA ולא כל קובץ .log כדי לחסוך זמן
                                    bool isMainLogName = entry.Name.IndexOf("engineGroupA.file", StringComparison.OrdinalIgnoreCase) >= 0;

                                    // 3. זיהוי EVENTS - טוען רק Events.csv כדי למנוע טעינת CSV ענקיים לא רלוונטיים
                                    bool isEventsCsv = entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                                                       entry.Name.IndexOf("Events", StringComparison.OrdinalIgnoreCase) >= 0;

                                    // --- לוגיקת ההחלטה ---

                                    if (isAppDevPath && isAppLogName)
                                    {
                                        entryData.Type = FileType.AppDevLog;
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                    else if (isMainLogName)
                                    {
                                        // הסרנו את התנאי הכללי (entry.Name.EndsWith(".log")) כדי לא לטעון לוגים לא נחוצים
                                        entryData.Type = FileType.MainLog;
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                    else if (isEventsCsv)
                                    {
                                        entryData.Type = FileType.EventsCsv;
                                        entryData.Stream = CopyToMemory(entry);
                                        filesToProcess.Add(entryData);
                                    }
                                    else if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var ms = CopyToMemory(entry);
                                        var bmp = LoadBitmapFromStream(ms);
                                        if (bmp != null) screenshotsBag.Add(bmp);
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

                                int totalFilesToParse = filesToProcess.Count;
                                int processedCount = 0;

                                Parallel.ForEach(filesToProcess, item =>
                                {
                                    using (item.Stream)
                                    {
                                        if (item.Type == FileType.AppDevLog)
                                        {
                                            foreach (var l in ParseAppDevLogStream(item.Stream)) appDevLogsBag.Add(l);
                                        }
                                        else if (item.Type == FileType.MainLog)
                                        {
                                            foreach (var l in ParseLogStream(item.Stream)) logsBag.Add(l);
                                        }
                                        else if (item.Type == FileType.EventsCsv)
                                        {
                                            foreach (var e in ParseEventsCsv(item.Stream)) eventsBag.Add(e);
                                        }
                                    }

                                    int current = Interlocked.Increment(ref processedCount);
                                    if (current % 5 == 0 || current == totalFilesToParse)
                                    {
                                        double parseRatio = (double)current / totalFilesToParse;
                                        double fileProgress = (0.5 + (parseRatio * 0.5)) * currentFileSize;
                                        double totalPercent = ((processedBytesGlobal + fileProgress) / totalBytesAllFiles) * 100;
                                        progress?.Report((Math.Min(99, totalPercent), $"Parsing: {current}/{totalFilesToParse}"));
                                    }
                                });
                            }
                        }
                        else
                        {
                            // טעינת קובץ בודד (לא ZIP)
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var ms = new MemoryStream())
                            {
                                fs.CopyTo(ms);
                                ms.Position = 0;

                                bool isAppLog = fileName.IndexOf("APPDEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                fileName.IndexOf("PRESS.HOST.APP", StringComparison.OrdinalIgnoreCase) >= 0;

                                if (isAppLog)
                                    foreach (var l in ParseAppDevLogStream(ms)) appDevLogsBag.Add(l);
                                else
                                    foreach (var l in ParseLogStream(ms)) logsBag.Add(l);
                            }
                        }

                        processedBytesGlobal += currentFileSize;
                    }

                    progress?.Report((98, "Sorting..."));

                    session.Logs = logsBag.OrderByDescending(x => x.Date).ToList();
                    session.AppDevLogs = appDevLogsBag.OrderByDescending(x => x.Date).ToList();
                    session.Events = eventsBag.OrderByDescending(x => x.Time).ToList();
                    session.Screenshots = screenshotsBag.ToList();

                    progress?.Report((100, "Ready"));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {ex.Message}");
                }

                return session;
            });
        }

        private enum FileType { MainLog, AppDevLog, EventsCsv }
        private class ZipEntryData { public string Name; public FileType Type; public MemoryStream Stream; }

        private double CalculatePercent(long processed, long total) => total == 0 ? 0 : Math.Min(99, ((double)processed / total) * 100);

        private List<LogEntry> ParseAppDevLogStream(Stream stream)
        {
            var list = new List<LogEntry>();
            try
            {
                if (stream.Position != 0) stream.Position = 0;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    StringBuilder buffer = new StringBuilder();
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line == "!!![V2]") continue;
                        if (_dateStartPattern.IsMatch(line))
                        {
                            if (buffer.Length > 0)
                            {
                                var l = ProcessAppDevBuffer(buffer.ToString());
                                if (l != null) list.Add(l);
                                buffer.Clear();
                            }
                        }
                        buffer.AppendLine(line);
                    }
                    if (buffer.Length > 0)
                    {
                        var l = ProcessAppDevBuffer(buffer.ToString());
                        if (l != null) list.Add(l);
                    }
                }
            }
            catch { }
            return list;
        }

        private LogEntry ProcessAppDevBuffer(string rawText)
        {
            var match = _appDevRegex.Match(rawText);
            if (!match.Success) return null;

            string timestampStr = match.Groups["Timestamp"].Value;

            if (!DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                DateTime.TryParse(timestampStr, out date);
            }

            string message = match.Groups["Message"].Value.Trim();
            string exception = match.Groups["Exception"].Value.Trim();
            string data = match.Groups["Data"].Value.Trim();

            if (!string.IsNullOrEmpty(exception)) message += $"\n[EXC]: {exception}";
            if (!string.IsNullOrEmpty(data)) message += $"\n[DATA]: {data}";

            return new LogEntry
            {
                Date = date,
                ThreadName = match.Groups["Thread"].Value,
                Level = match.Groups["Level"].Value.ToUpper(),
                Logger = match.Groups["Logger"].Value,
                Message = message,
                ProcessName = "APP" // סימון שזה לוג של האפליקציה
            };
        }

        public List<LogEntry> ParseLogStream(Stream stream)
        {
            var list = new List<LogEntry>();
            try
            {
                if (stream.Position != 0) stream.Position = 0;
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
                if (stream.Position != 0) stream.Position = 0;
                using (var reader = new StreamReader(stream))
                {
                    string header = reader.ReadLine();
                    if (header == null) return list;
                    var headers = header.Split(',');

                    // מיפוי עמודות
                    int timeIdx = Array.IndexOf(headers, "Time");
                    int nameIdx = Array.IndexOf(headers, "Name");
                    int stateIdx = Array.IndexOf(headers, "State");
                    int severityIdx = Array.IndexOf(headers, "Severity");
                    int subsystemIdx = Array.IndexOf(headers, "Subsystem");
                    int paramsIdx = Array.IndexOf(headers, "Parameters");

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
                                    Description = (subsystemIdx >= 0 && parts.Count > subsystemIdx) ? parts[subsystemIdx] : "",
                                    Parameters = (paramsIdx >= 0 && parts.Count > paramsIdx) ? parts[paramsIdx] : ""
                                };
                                if (!string.IsNullOrWhiteSpace(entry.Name)) list.Add(entry);
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
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
                if (stream.Position != 0) stream.Position = 0;
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