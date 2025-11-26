using System.Windows.Media;

namespace IndiLogs_3._0.Models
{
    // זה המקום היחיד שבו המחלקה הזו צריכה להיות מוגדרת!
    public class ColoringCondition
    {
        public string Field { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }

        // משתמשים ב-Color של WPF (System.Windows.Media)
        public Color Color { get; set; }

        // פונקציה לשכפול (Deep Copy)
        public ColoringCondition Clone()
        {
            return new ColoringCondition
            {
                Field = this.Field,
                Operator = this.Operator,
                Value = this.Value,
                Color = this.Color
            };
        }
    }

    public class FilterCondition
    {
        public string Field { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
        public bool IsActive { get; set; } = true;
    }
}