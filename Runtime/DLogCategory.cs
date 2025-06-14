using UnityEngine;

namespace NoSlimes.Logging
{
    /// <summary>
    /// Represents a category for a log message, allowing for color-coding and filtering.
    /// </summary>
    public class DLogCategory
    {
        public string Name { get; }
        public string ColorHex { get; }

        public DLogCategory(string name, Color color)
        {
            Name = name;
            ColorHex = $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }

        public DLogCategory(string name, string colorHex = "#FFFFFF")
        {
            Name = name;
            ColorHex = colorHex;
        }

        public override string ToString() => Name;

        // Predefined categories for common log types
        public static readonly DLogCategory Log = new("Log", Color.white);
        public static readonly DLogCategory Warning = new("Warning", Color.yellow);
        public static readonly DLogCategory Error = new("Error", Color.red);
    }
}
