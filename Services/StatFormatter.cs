using System.Globalization;

namespace BO2.Services
{
    // Pure formatting helpers extracted from MainWindowViewModel so they can be unit-tested
    // without any WinUI or WinAppSDK dependencies.
    internal sealed class StatFormatter(string unavailableText)
    {
        private readonly string _unavailableText = unavailableText;

        public string FormatStat(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

        public string FormatCoordinate(float value) => value.ToString("N2", CultureInfo.CurrentCulture);

        public string FormatCandidate(int? value) => value.HasValue ? FormatStat(value.Value) : _unavailableText;

        public string FormatCandidate(float? value) => value.HasValue ? FormatCoordinate(value.Value) : _unavailableText;

        public static string FormatAddress(uint address) => $"0x{address:X8}";
    }
}
