using System;

namespace BO2.Services
{
    public sealed class WidgetSettings
    {
        public const int DefaultWidth = 320;
        public const int DefaultHeight = 160;
        public const string DefaultBackgroundColor = "#FFFFFFFF";
        public const string DefaultTextColor = "#FF000000";

        public bool Enabled { get; set; }

        public int Width { get; set; } = DefaultWidth;

        public int Height { get; set; } = DefaultHeight;

        public int? X { get; set; }

        public int? Y { get; set; }

        public string BackgroundColor { get; set; } = DefaultBackgroundColor;

        public string TextColor { get; set; } = DefaultTextColor;

        public bool TransparentBackground { get; set; }

        public bool AlwaysOnTop { get; set; }

        public bool CenterAlign { get; set; } = true;

        public static WidgetSettings CreateDefault()
        {
            return new WidgetSettings();
        }

        public WidgetSettings Clone()
        {
            return new WidgetSettings
            {
                Enabled = Enabled,
                Width = Width,
                Height = Height,
                X = X,
                Y = Y,
                BackgroundColor = BackgroundColor,
                TextColor = TextColor,
                TransparentBackground = TransparentBackground,
                AlwaysOnTop = AlwaysOnTop,
                CenterAlign = CenterAlign
            };
        }

        public void Normalize()
        {
            Width = Math.Clamp(Width, 160, 3840);
            Height = Math.Clamp(Height, 80, 2160);

            if (!WidgetColorSerializer.IsValidColor(BackgroundColor))
            {
                BackgroundColor = DefaultBackgroundColor;
            }

            if (!WidgetColorSerializer.IsValidColor(TextColor))
            {
                TextColor = DefaultTextColor;
            }
        }
    }
}
