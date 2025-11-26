using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IndiLogs_3._0.Controls
{
    public class HighlightTextBlock : TextBlock
    {
        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.Register("HighlightText", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightTextChanged));

        public string HighlightText
        {
            get { return (string)GetValue(HighlightTextProperty); }
            set { SetValue(HighlightTextProperty, value); }
        }

        public new string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public new static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnTextChanged));

        private static void OnHighlightTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).UpdateHighlighting();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            Inlines.Clear();

            string text = Text;
            string highlight = HighlightText;

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.IsNullOrEmpty(highlight))
            {
                Inlines.Add(new Run(text));
                return;
            }

            try
            {
                string escapedHighlight = Regex.Escape(highlight);
                string pattern = $"({escapedHighlight})";
                string[] segments = Regex.Split(text, pattern, RegexOptions.IgnoreCase);

                foreach (string segment in segments)
                {
                    Run run = new Run(segment);

                    if (string.Equals(segment, highlight, StringComparison.OrdinalIgnoreCase))
                    {
                        run.Background = Brushes.Yellow;
                        run.Foreground = Brushes.Black;
                        run.FontWeight = FontWeights.Bold;
                    }

                    Inlines.Add(run);
                }
            }
            catch
            {
                Inlines.Add(new Run(text));
            }
        }
    }
}