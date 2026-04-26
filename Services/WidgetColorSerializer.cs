using System;
using System.Globalization;
using Windows.UI;

namespace BO2.Services
{
    internal static class WidgetColorSerializer
    {
        public static bool IsValidColor(string? value)
        {
            return TryParse(value, out _);
        }

        public static Color ParseOrDefault(string? value, Color defaultColor)
        {
            return TryParse(value, out Color color) ? color : defaultColor;
        }

        public static string Format(Color color)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        private static bool TryParse(string? value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text[1..];
            }

            if (text.Length != 8
                || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return false;
            }

            color = Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);
            return true;
        }
    }
}
