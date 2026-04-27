using System;

namespace BO2.Services
{
    public sealed class AppPreferences
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

        public static AppPreferences CreateDefault()
        {
            return new AppPreferences();
        }

        public void Normalize()
        {
            if (Version != CurrentVersion)
            {
                Version = CurrentVersion;
            }

            if (!Enum.IsDefined(ThemeMode))
            {
                ThemeMode = ThemeMode.System;
            }
        }
    }
}
