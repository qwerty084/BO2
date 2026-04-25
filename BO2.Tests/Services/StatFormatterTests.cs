using System.Globalization;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class StatFormatterTests
    {
        // Use "Unavailable" as the unavailable-value sentinel; tests that assert on the sentinel
        // check for exactly this string so they remain stable across resource changes.
        private const string UnavailableText = "Unavailable";
        private static StatFormatter MakeFormatter() => new(UnavailableText);

        // ── FormatStat ──────────────────────────────────────────────────────────────

        [Fact]
        public void FormatStat_Zero_FormatsWithoutGroupSeparator()
        {
            // Verify integer formatting matches what CultureInfo.CurrentCulture would produce.
            string expected = 0.ToString("N0", CultureInfo.CurrentCulture);
            Assert.Equal(expected, MakeFormatter().FormatStat(0));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(999)]
        [InlineData(1_000)]
        [InlineData(1_500_000)]
        public void FormatStat_MatchesN0Format(int value)
        {
            string expected = value.ToString("N0", CultureInfo.CurrentCulture);
            Assert.Equal(expected, MakeFormatter().FormatStat(value));
        }

        // ── FormatCoordinate ────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0f)]
        [InlineData(1.5f)]
        [InlineData(-123.456f)]
        public void FormatCoordinate_MatchesN2Format(float value)
        {
            string expected = value.ToString("N2", CultureInfo.CurrentCulture);
            Assert.Equal(expected, MakeFormatter().FormatCoordinate(value));
        }

        // ── FormatCandidate(int?) ───────────────────────────────────────────────────

        [Fact]
        public void FormatCandidateInt_WhenNull_ReturnsUnavailableText()
        {
            Assert.Equal(UnavailableText, MakeFormatter().FormatCandidate((int?)null));
        }

        [Fact]
        public void FormatCandidateInt_WhenHasValue_FormatsWithFormatStat()
        {
            const int value = 42;
            string expected = MakeFormatter().FormatStat(value);
            Assert.Equal(expected, MakeFormatter().FormatCandidate((int?)value));
        }

        [Fact]
        public void FormatCandidateInt_ZeroIsNotTreatedAsNull()
        {
            Assert.NotEqual(UnavailableText, MakeFormatter().FormatCandidate((int?)0));
        }

        // ── FormatCandidate(float?) ─────────────────────────────────────────────────

        [Fact]
        public void FormatCandidateFloat_WhenNull_ReturnsUnavailableText()
        {
            Assert.Equal(UnavailableText, MakeFormatter().FormatCandidate((float?)null));
        }

        [Fact]
        public void FormatCandidateFloat_WhenHasValue_FormatsWithFormatCoordinate()
        {
            const float value = 3.14f;
            string expected = MakeFormatter().FormatCoordinate(value);
            Assert.Equal(expected, MakeFormatter().FormatCandidate((float?)value));
        }

        [Fact]
        public void FormatCandidateFloat_ZeroIsNotTreatedAsNull()
        {
            Assert.NotEqual(UnavailableText, MakeFormatter().FormatCandidate((float?)0f));
        }

        // ── FormatAddress ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0x00000000U, "0x00000000")]
        [InlineData(0xDEADBEEFU, "0xDEADBEEF")]
        [InlineData(0x0234C068U, "0x0234C068")]  // known SteamZombies points address
        public void FormatAddress_ProducesUppercaseEightDigitHex(uint address, string expected)
        {
            Assert.Equal(expected, StatFormatter.FormatAddress(address));
        }

        // ── UnavailableText is preserved ────────────────────────────────────────────

        [Fact]
        public void FormatCandidate_UnavailableTextMatchesConstructorArgument()
        {
            const string customText = "N/A";
            var formatter = new StatFormatter(customText);

            Assert.Equal(customText, formatter.FormatCandidate((int?)null));
            Assert.Equal(customText, formatter.FormatCandidate((float?)null));
        }
    }
}
