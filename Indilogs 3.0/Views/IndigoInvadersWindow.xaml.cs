// BILINGUAL-HEADER-START
// EN: File: IndigoInvadersWindow.xaml.cs - Auto-added bilingual header.
// HE: קובץ: IndigoInvadersWindow.xaml.cs - כותרת דו-לשונית שנוספה אוטומטית.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class IndigoInvadersWindow : Window
    {
        // קבועים וגודל
        private const double PlayerSpeed = 7;
        private const double BulletSpeed = 12;
        private const double AlienDropDistance = 15;
        private const int AlienRows = 4;
        private const int AlienCols = 9;

        // טיימר ושליטה
        private DispatcherTimer _gameTimer;
        private bool _moveLeft, _moveRight, _isShooting;
        private bool _gameRunning = false;

        // אובייקטים במשחק
        private Rectangle _player;
        private List<Rectangle> _playerBullets = new List<Rectangle>();
        private List<Rectangle> _alienBullets = new List<Rectangle>();
        private List<Invader> _invaders = new List<Invader>();

        // סטטוס משחק
        private int _score = 0;
        private int _lives = 3;
        private int _level = 1;
        private double _alienSpeedX = 2;
        private int _alienDirection = 1; // 1 ימינה, -1 שמאלה
        private DateTime _lastShotTime = DateTime.MinValue;

        public IndigoInvadersWindow()
        {
            InitializeComponent();
            GenerateStars();

            _gameTimer = new DispatcherTimer();
            _gameTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _gameTimer.Tick += GameLoop;
        }

        private void StartGame_Click(object sender, RoutedEventArgs e)
        {
            ResetGame();
        }

        private void ResetGame()
        {
            GameCanvas.Children.Clear();
            _playerBullets.Clear();
            _alienBullets.Clear();
            _invaders.Clear();

            _score = 0;
            _lives = 3;
            _level = 1;
            UpdateUI();

            CreatePlayer();
            SpawnInvaders();

            Overlay.Visibility = Visibility.Collapsed;
            _gameRunning = true;
            _gameTimer.Start();
            this.Focus();
        }

        private void NextLevel()
        {
            _level++;
            _playerBullets.ForEach(b => GameCanvas.Children.Remove(b));
            _playerBullets.Clear();
            _alienBullets.ForEach(b => GameCanvas.Children.Remove(b));
            _alienBullets.Clear();

            SpawnInvaders();
            UpdateUI();
        }

        private void GameOver()
        {
            _gameRunning = false;
            _gameTimer.Stop();
            OverlayTitle.Text = "GAME OVER";
            OverlayMessage.Text = $"Final Score: {_score}";
            Overlay.Visibility = Visibility.Visible;
        }

        // --- Game Logic Loop ---

        private void GameLoop(object sender, EventArgs e)
        {
            if (!_gameRunning) return;

            MovePlayer();
            MoveBullets();
            MoveAliens();
            AlienShootLogic();
            CheckCollisions();

            if (_invaders.Count == 0)
            {
                NextLevel();
            }
        }

        // --- Player ---

        private void CreatePlayer()
        {
            _player = new Rectangle
            {
                Width = 40,
                Height = 20,
                Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)), // Primary Blue
                RadiusX = 3,
                RadiusY = 3
            };

            // תותח קטן מעל השחקן
            var cannon = new Rectangle { Width = 6, Height = 8, Fill = Brushes.LightBlue };

            Canvas.SetLeft(_player, (GameCanvas.ActualWidth / 2) - 20);
            Canvas.SetTop(_player, GameCanvas.ActualHeight - 50);

            GameCanvas.Children.Add(_player);
        }

        private void MovePlayer()
        {
            double currentLeft = Canvas.GetLeft(_player);

            if (_moveLeft && currentLeft > 0)
                Canvas.SetLeft(_player, currentLeft - PlayerSpeed);

            if (_moveRight && currentLeft < (GameCanvas.ActualWidth - _player.Width))
                Canvas.SetLeft(_player, currentLeft + PlayerSpeed);

            if (_isShooting && (DateTime.Now - _lastShotTime).TotalMilliseconds > 400)
            {
                ShootPlayerBullet();
                _lastShotTime = DateTime.Now;
            }
        }

        // --- Aliens ---

        private void SpawnInvaders()
        {
            _invaders.Clear();
            _alienSpeedX = 1.5 + (_level * 0.5); // מהירות עולה בכל שלב
            _alienDirection = 1;

            double startX = 50;
            double startY = 50;
            double gap = 15;
            double width = 30;
            double height = 20;

            for (int row = 0; row < AlienRows; row++)
            {
                for (int col = 0; col < AlienCols; col++)
                {
                    var color = row == 0 ? Brushes.Purple : (row == 1 ? Brushes.MediumOrchid : Brushes.Violet);

                    var alienBody = new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Fill = color,
                        RadiusX = 5,
                        RadiusY = 5,
                        Tag = "Alien"
                    };

                    Canvas.SetLeft(alienBody, startX + col * (width + gap));
                    Canvas.SetTop(alienBody, startY + row * (height + gap));

                    GameCanvas.Children.Add(alienBody);
                    _invaders.Add(new Invader { UIElement = alienBody });
                }
            }
        }

        private void MoveAliens()
        {
            bool hitEdge = false;
            double rightEdge = GameCanvas.ActualWidth - 40;

            foreach (var invader in _invaders)
            {
                double x = Canvas.GetLeft(invader.UIElement);
                if ((_alienDirection == 1 && x > rightEdge) || (_alienDirection == -1 && x < 10))
                {
                    hitEdge = true;
                    break; // מספיק שאחד נגע בקיר
                }
            }

            if (hitEdge)
            {
                _alienDirection *= -1;
                foreach (var invader in _invaders)
                {
                    double y = Canvas.GetTop(invader.UIElement);
                    Canvas.SetTop(invader.UIElement, y + AlienDropDistance);

                    // אם החייזרים הגיעו למטה מדי
                    if (y + AlienDropDistance > Canvas.GetTop(_player) - 30)
                    {
                        GameOver();
                        return;
                    }
                }
                // האצה קטנה בכל ירידה
                _alienSpeedX *= 1.05;
            }
            else
            {
                foreach (var invader in _invaders)
                {
                    double x = Canvas.GetLeft(invader.UIElement);
                    Canvas.SetLeft(invader.UIElement, x + (_alienSpeedX * _alienDirection));
                }
            }
        }

        private void AlienShootLogic()
        {
            // סיכוי לירי גדל ככל שיש פחות חייזרים וככל שהשלב גבוה יותר
            int chance = 100 - (_level * 2);
            if (chance < 20) chance = 20;

            var random = new Random();
            if (random.Next(0, chance) == 0 && _invaders.Count > 0)
            {
                // בוחרים חייזר אקראי שיורה
                var shooter = _invaders[random.Next(_invaders.Count)];
                ShootAlienBullet(shooter.UIElement);
            }
        }

        // --- Bullets & Collisions ---

        private void ShootPlayerBullet()
        {
            var bullet = new Rectangle { Width = 4, Height = 10, Fill = Brushes.Cyan };
            Canvas.SetLeft(bullet, Canvas.GetLeft(_player) + 18);
            Canvas.SetTop(bullet, Canvas.GetTop(_player) - 10);
            GameCanvas.Children.Add(bullet);
            _playerBullets.Add(bullet);
        }

        private void ShootAlienBullet(Rectangle alien)
        {
            var bullet = new Rectangle { Width = 4, Height = 10, Fill = Brushes.Red };
            Canvas.SetLeft(bullet, Canvas.GetLeft(alien) + 13);
            Canvas.SetTop(bullet, Canvas.GetTop(alien) + 20);
            GameCanvas.Children.Add(bullet);
            _alienBullets.Add(bullet);
        }

        private void MoveBullets()
        {
            // Player Bullets (Up)
            for (int i = _playerBullets.Count - 1; i >= 0; i--)
            {
                var b = _playerBullets[i];
                double y = Canvas.GetTop(b);
                if (y < 0)
                {
                    GameCanvas.Children.Remove(b);
                    _playerBullets.RemoveAt(i);
                }
                else
                {
                    Canvas.SetTop(b, y - BulletSpeed);
                }
            }

            // Alien Bullets (Down)
            for (int i = _alienBullets.Count - 1; i >= 0; i--)
            {
                var b = _alienBullets[i];
                double y = Canvas.GetTop(b);
                if (y > GameCanvas.ActualHeight)
                {
                    GameCanvas.Children.Remove(b);
                    _alienBullets.RemoveAt(i);
                }
                else
                {
                    Canvas.SetTop(b, y + (BulletSpeed * 0.6)); // יריות חייזרים איטיות יותר
                }
            }
        }

        private void CheckCollisions()
        {
            Rect playerRect = new Rect(Canvas.GetLeft(_player), Canvas.GetTop(_player), _player.Width, _player.Height);

            // 1. כדורי שחקן פוגעים בחייזרים
            for (int i = _playerBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _playerBullets[i];
                Rect bulletRect = new Rect(Canvas.GetLeft(bullet), Canvas.GetTop(bullet), bullet.Width, bullet.Height);
                bool hit = false;

                for (int j = _invaders.Count - 1; j >= 0; j--)
                {
                    var alien = _invaders[j].UIElement;
                    Rect alienRect = new Rect(Canvas.GetLeft(alien), Canvas.GetTop(alien), alien.Width, alien.Height);

                    if (bulletRect.IntersectsWith(alienRect))
                    {
                        // פיצוץ חייזר
                        GameCanvas.Children.Remove(alien);
                        _invaders.RemoveAt(j);
                        hit = true;
                        _score += 10 * _level;
                        break;
                    }
                }

                if (hit)
                {
                    GameCanvas.Children.Remove(bullet);
                    _playerBullets.RemoveAt(i);
                    UpdateUI();
                }
            }

            // 2. כדורי חייזרים פוגעים בשחקן
            for (int i = _alienBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _alienBullets[i];
                Rect bulletRect = new Rect(Canvas.GetLeft(bullet), Canvas.GetTop(bullet), bullet.Width, bullet.Height);

                if (bulletRect.IntersectsWith(playerRect))
                {
                    GameCanvas.Children.Remove(bullet);
                    _alienBullets.RemoveAt(i);
                    PlayerHit();
                }
            }

            // 3. חייזרים נוגעים בשחקן
            foreach (var invader in _invaders)
            {
                var alien = invader.UIElement;
                Rect alienRect = new Rect(Canvas.GetLeft(alien), Canvas.GetTop(alien), alien.Width, alien.Height);
                if (alienRect.IntersectsWith(playerRect))
                {
                    GameOver();
                    return;
                }
            }
        }

        private void PlayerHit()
        {
            _lives--;
            UpdateUI();

            // אפקט פגיעה (הבהוב)
            _player.Opacity = 0.5;
            var dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            dt.Tick += (s, e) => { _player.Opacity = 1; dt.Stop(); };
            dt.Start();

            if (_lives <= 0)
            {
                GameOver();
            }
        }

        private void UpdateUI()
        {
            ScoreText.Text = _score.ToString();
            LivesText.Text = _lives.ToString();
            LevelText.Text = $"LEVEL {_level}";
        }

        // --- Input Handling ---

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left) _moveLeft = true;
            if (e.Key == Key.Right) _moveRight = true;
            if (e.Key == Key.Space) _isShooting = true;
            if (e.Key == Key.Escape) Close();
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left) _moveLeft = false;
            if (e.Key == Key.Right) _moveRight = false;
            if (e.Key == Key.Space) _isShooting = false;
        }

        // --- Utils ---

        private void GenerateStars()
        {
            Random r = new Random();
            for (int i = 0; i < 50; i++)
            {
                Ellipse star = new Ellipse
                {
                    Width = 2,
                    Height = 2,
                    Fill = Brushes.White,
                    Opacity = r.NextDouble()
                };
                Canvas.SetLeft(star, r.Next(0, 800));
                Canvas.SetTop(star, r.Next(0, 600));
                StarFieldCanvas.Children.Add(star);
            }
        }

        private class Invader
        {
            public Rectangle UIElement { get; set; }
        }
    }
}