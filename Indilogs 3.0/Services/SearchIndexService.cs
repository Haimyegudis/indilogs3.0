using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.Services
{
    public class SearchIndexService
    {
        // המילון ממפה מילה -> רשימת הלוגים שמכילים אותה
        private Dictionary<string, List<LogEntry>> _index;
        private readonly char[] _delimiters = new[] { ' ', '.', ':', ',', '[', ']', '(', ')', '\t', '-', '_', '/', '\\' };

        public SearchIndexService()
        {
            _index = new Dictionary<string, List<LogEntry>>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task BuildIndexAsync(IEnumerable<LogEntry> logs)
        {
            await Task.Run(() =>
            {
                _index.Clear();
                if (logs == null) return;

                foreach (var log in logs)
                {
                    if (string.IsNullOrEmpty(log.Message)) continue;

                    // פירוק ההודעה למילים לפי תווים מפרידים
                    var words = log.Message.Split(_delimiters, StringSplitOptions.RemoveEmptyEntries);

                    // שימוש ב-HashSet למניעת כפילויות של אותה מילה באותה שורה
                    foreach (var word in words.Distinct())
                    {
                        // אנו שומרים מילים באורך 2 ומעלה כדי לחסוך זיכרון על 'a', '1' וכו'
                        if (word.Length < 2) continue;

                        if (!_index.TryGetValue(word, out var list))
                        {
                            list = new List<LogEntry>();
                            _index[word] = list;
                        }
                        list.Add(log);
                    }
                }
            });
        }

        public List<LogEntry> Search(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return null;

            // 1. ניסיון חיפוש מדויק במילון (O(1))
            if (_index.TryGetValue(term, out var exactMatches))
            {
                return exactMatches;
            }

            // 2. אם לא נמצאה מילה מדויקת (למשל המשתמש מחפש ביטוי עם רווחים "Error 404"),
            // המילון לא יעזור ישירות, ואנחנו מחזירים null כדי שה-ViewModel יעבור לחיפוש רגיל.
            return null;
        }

        public void Clear()
        {
            _index.Clear();
        }
    }
}