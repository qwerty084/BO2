using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class WeaponDisplayNameResolverTests
    {
        [Theory]
        [InlineData("fnfal_zm", "FAL")]
        [InlineData("rpd_zm", "RPD")]
        [InlineData("hamr_zm", "HAMR")]
        [InlineData("python_zm", "Python")]
        public void ResolveDisplayName_WhenAliasIsKnown_ReturnsDisplayName(
            string weaponAlias,
            string expectedDisplayName)
        {
            string displayName = WeaponDisplayNameResolver.ResolveDisplayName(weaponAlias);

            Assert.Equal(expectedDisplayName, displayName);
        }

        [Fact]
        public void ResolveDisplayName_WhenAliasIsUnknown_ReturnsAlias()
        {
            string displayName = WeaponDisplayNameResolver.ResolveDisplayName("unknown_weapon_zm");

            Assert.Equal("unknown_weapon_zm", displayName);
        }

        [Fact]
        public void FormatForEvent_WhenAliasIsKnown_IncludesAliasForDebugging()
        {
            string displayName = WeaponDisplayNameResolver.FormatForEvent("fnfal_zm");

            Assert.Equal("FAL (fnfal_zm)", displayName);
        }

        [Fact]
        public void FormatForEvent_WhenAliasIsUnknown_ReturnsAlias()
        {
            string displayName = WeaponDisplayNameResolver.FormatForEvent("unknown_weapon_zm");

            Assert.Equal("unknown_weapon_zm", displayName);
        }
    }
}
