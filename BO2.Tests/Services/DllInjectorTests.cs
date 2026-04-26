using System;
using System.IO;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class DllInjectorTests
    {
        [Fact]
        public void Inject_WhenNoGameDetected_DoesNotAttemptInjection()
        {
            var injector = new DllInjector();

            DllInjectionResult result = injector.Inject(null);

            Assert.Equal(DllInjectionState.NotAttempted, result.State);
        }

        [Fact]
        public void Inject_WhenGameIsUnsupported_ReturnsUnsupportedWithoutNativeCalls()
        {
            var injector = new DllInjector();
            var detectedGame = new DetectedGame(
                GameVariant.SteamMultiplayer,
                "Steam Multiplayer",
                "t6mp",
                42,
                null,
                "Unsupported");

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.UnsupportedGame, result.State);
        }

        [Fact]
        public void ResolveMonitorPath_UsesAppBaseDirectory()
        {
            string expectedPath = Path.Combine(AppContext.BaseDirectory, "BO2Monitor.dll");

            string actualPath = DllInjector.ResolveMonitorPath();

            Assert.Equal(expectedPath, actualPath);
        }

        [Fact]
        public void ResolveWow64PowerShellPath_UsesWindowsDirectory()
        {
            string expectedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SysWOW64",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            string actualPath = DllInjector.ResolveWow64PowerShellPath();

            Assert.Equal(expectedPath, actualPath);
        }
    }
}
