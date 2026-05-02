using System.Collections.Generic;

namespace BO2.Services
{
    public sealed class WidgetSettingsDocument
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        public Dictionary<string, WidgetSettings> Widgets { get; set; } = [];

        public static WidgetSettingsDocument CreateDefault()
        {
            var document = new WidgetSettingsDocument();
            document.SetWidget(WidgetKind.BoxTracker, WidgetSettings.CreateDefault());
            return document;
        }

        public WidgetSettings GetWidget(WidgetKind kind)
        {
            Widgets ??= [];
            string key = GetKey(kind);
            if (!Widgets.TryGetValue(key, out WidgetSettings? settings) || settings is null)
            {
                settings = WidgetSettings.CreateDefault();
                Widgets[key] = settings;
            }

            settings.Normalize();
            return settings;
        }

        public void SetWidget(WidgetKind kind, WidgetSettings settings)
        {
            settings.Normalize();
            Widgets[GetKey(kind)] = settings;
        }

        public void Normalize()
        {
            Widgets ??= [];
            if (Version != CurrentVersion)
            {
                Version = CurrentVersion;
            }

            GetWidget(WidgetKind.BoxTracker);
        }

        private static string GetKey(WidgetKind kind)
        {
            return kind.ToString();
        }
    }
}
