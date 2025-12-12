using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class SnakeWindow : Window
    {
        private const int Size = 20; // גודל המשבצת
        private DispatcherTimer _timer;

        // משתני משחק
        private List<Point> _snake;
        private Point _food;
        private int _score;
        private bool _isGameOver;

        // ניהול תנועה חכם (למניעת באגים)
        private Point _currentDirection;
        private Queue<Point> _inputQueue; // תור הוראות תנועה

        // נתיב שמירת שיאים
        private string _scoreFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndiLogs", "snake_scores.json");

        public SnakeWindow()
        {
            InitializeComponent();
            LoadHighScores();
            InitializeGame();
        }

        private void InitializeGame()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += GameLoop;
            StartNewGame();
        }

        private void StartNewGame()
        {
            // הגדרת מהירות לפי הרמה שנבחרה
            if (DifficultySelector.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out int speed))
            {
                _timer.Interval = TimeSpan.FromMilliseconds(speed);
            }
            else
            {
                _timer.Interval = TimeSpan.FromMilliseconds(100);
            }

            // איפוס הנחש (מתחיל במרכז)
            _snake = new List<Point>
            {
                new Point(100, 100),
                new Point(80, 100),
                new Point(60, 100)
            };

            _score = 0;
            ScoreText.Text = "0";
            _isGameOver = false;
            Overlay.Visibility = Visibility.Collapsed;

            // איפוס כיוונים
            _currentDirection = new Point(1, 0); // מתחילים ימינה
            _inputQueue = new Queue<Point>();

            SpawnFood();
            Draw();
            _timer.Start();
        }

        // לולאת המשחק - רצה כל X מילישניות
        private void GameLoop(object sender, EventArgs e)
        {
            if (_isGameOver) return;

            // 1. שליפת הכיוון הבא מהתור (אם המשתמש לחץ)
            // זה התיקון לבאג הלחיצה המהירה!
            if (_inputQueue.Count > 0)
            {
                _currentDirection = _inputQueue.Dequeue();
            }

            // 2. חישוב מיקום הראש החדש
            Point currentHead = _snake[0];
            Point newHead = new Point(
                currentHead.X + (_currentDirection.X * Size),
                currentHead.Y + (_currentDirection.Y * Size)
            );

            // 3. בדיקת התנגשות בקירות
            if (newHead.X < 0 || newHead.X >= GameCanvas.ActualWidth ||
                newHead.Y < 0 || newHead.Y >= GameCanvas.ActualHeight)
            {
                EndGame();
                return;
            }

            // 4. בדיקת התנגשות עצמית
            // בודקים עד Count-1 כי הזנב יזוז תכף, אז מותר להיכנס למשבצת שלו
            for (int i = 0; i < _snake.Count - 1; i++)
            {
                if (IsPointsEqual(_snake[i], newHead))
                {
                    EndGame();
                    return;
                }
            }

            // 5. הוספת הראש החדש
            _snake.Insert(0, newHead);

            // 6. בדיקת אכילה
            if (IsPointsEqual(newHead, _food))
            {
                _score++;
                ScoreText.Text = _score.ToString();
                SpawnFood();
                // לא מוחקים זנב -> הנחש גדל
            }
            else
            {
                // תזוזה רגילה -> מוחקים זנב
                _snake.RemoveAt(_snake.Count - 1);
            }

            Draw();
        }

        // שימוש ב-PreviewKeyDown מבטיח שהחלון יקבל את המקש לפני הפקדים האחרים
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isGameOver)
            {
                if (e.Key == Key.R) StartNewGame();
                if (e.Key == Key.Escape) Close();
                return;
            }

            Point nextDir = new Point(0, 0);
            bool isArrowKey = true;

            switch (e.Key)
            {
                case Key.Up: nextDir = new Point(0, -1); break;
                case Key.Down: nextDir = new Point(0, 1); break;
                case Key.Left: nextDir = new Point(-1, 0); break;
                case Key.Right: nextDir = new Point(1, 0); break;
                case Key.Escape: Close(); return;
                default: isArrowKey = false; break;
            }

            if (!isArrowKey) return;

            // מניעת באג האיפוס: מסמנים שהמקש טופל כדי שה-ComboBox לא יגיב
            e.Handled = true;

            // חישוב הכיוון האחרון הידוע (מהתור או מהנחש)
            Point lastKnownDir = _inputQueue.Count > 0 ? _inputQueue.Last() : _currentDirection;

            // מניעת פניית פרסה (למשל ימינה ואז שמאלה)
            if ((lastKnownDir.X + nextDir.X == 0) && (lastKnownDir.Y + nextDir.Y == 0))
            {
                return;
            }

            // מגבלה על גודל התור למניעת דיליי אם לוחצים בטירוף
            if (_inputQueue.Count < 2)
            {
                _inputQueue.Enqueue(nextDir);
            }
        }

        private void SpawnFood()
        {
            if (GameCanvas.ActualWidth == 0) return;

            Random r = new Random();
            int maxX = (int)(GameCanvas.ActualWidth / Size);
            int maxY = (int)(GameCanvas.ActualHeight / Size);

            while (true)
            {
                int x = r.Next(0, maxX) * Size;
                int y = r.Next(0, maxY) * Size;
                Point p = new Point(x, y);

                // וודא שהאוכל לא נוצר על הנחש
                bool onSnake = false;
                foreach (var part in _snake)
                {
                    if (IsPointsEqual(part, p)) { onSnake = true; break; }
                }

                if (!onSnake)
                {
                    _food = p;
                    break;
                }
            }
        }

        private void Draw()
        {
            GameCanvas.Children.Clear();

            // ציור אוכל
            Ellipse foodParams = new Ellipse
            {
                Width = Size,
                Height = Size,
                Fill = Brushes.Red,
                Effect = new DropShadowEffect { Color = Colors.Red, BlurRadius = 8, ShadowDepth = 0 }
            };
            Canvas.SetLeft(foodParams, _food.X);
            Canvas.SetTop(foodParams, _food.Y);
            GameCanvas.Children.Add(foodParams);

            // ציור נחש
            for (int i = 0; i < _snake.Count; i++)
            {
                Rectangle rect = new Rectangle
                {
                    Width = Size,
                    Height = Size,
                    Fill = (i == 0) ? Brushes.Lime : Brushes.ForestGreen, // ראש בצבע שונה
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(rect, _snake[i].X);
                Canvas.SetTop(rect, _snake[i].Y);
                GameCanvas.Children.Add(rect);
            }
        }

        private void EndGame()
        {
            _isGameOver = true;
            _timer.Stop();
            FinalScoreText.Text = $"Final Score: {_score}";
            SaveScore();
            LoadHighScores();
            Overlay.Visibility = Visibility.Visible;
        }

        private void RestartBtn_Click(object sender, RoutedEventArgs e) => StartNewGame();

        private void DifficultySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // אם המשחק רץ ומשנים רמה, מתחילים מחדש
            if (_timer != null) StartNewGame();
        }

        // --- ניהול שיאים ---

        private void SaveScore()
        {
            if (_score == 0) return;
            try
            {
                List<int> scores = new List<int>();
                if (File.Exists(_scoreFile))
                    scores = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(_scoreFile)) ?? new List<int>();

                scores.Add(_score);
                scores = scores.OrderByDescending(s => s).Take(3).ToList(); // שמירת טופ 3

                // יצירת תיקייה אם לא קיימת
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_scoreFile));
                File.WriteAllText(_scoreFile, JsonConvert.SerializeObject(scores));
            }
            catch { }
        }

        private void LoadHighScores()
        {
            try
            {
                if (File.Exists(_scoreFile))
                {
                    var scores = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(_scoreFile));
                    HighScoresText.Text = string.Join("\n", scores.Select((s, i) => $"#{i + 1} : {s}"));
                }
            }
            catch { HighScoresText.Text = "Error"; }
        }

        // עזר להשוואת נקודות (בגלל ש-Point משתמש ב-Double)
        private bool IsPointsEqual(Point p1, Point p2)
        {
            return Math.Abs(p1.X - p2.X) < 0.1 && Math.Abs(p1.Y - p2.Y) < 0.1;
        }
    }
}