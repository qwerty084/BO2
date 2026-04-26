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
        public void ResolveWow64HelperPath_UsesAppBaseDirectory()
        {
            string expectedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "BO2InjectorHelper.exe"));

            string actualPath = DllInjector.ResolveWow64HelperPath();

            Assert.Equal(expectedPath, actualPath);
        }

        [Fact]
        public void Inject_WhenHelperIsMissing_MapsToWrongProcessArchitecture()
        {
            var injector = new DllInjector(
                is64BitProcess: () => true,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Valid,
                isMonitorAlreadyLoaded: _ => false,
                injectLibrary: (_, _) => throw new InvalidOperationException("direct path should not run"),
                injectLibraryViaWow64Helper: (_, _) => throw new DllInjector.WrongProcessArchitectureException("missing helper"));
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.WrongProcessArchitecture, result.State);
        }

        [Fact]
        public void Inject_WhenMonitorLoads_ReturnsLoadedUntilReadinessIsObserved()
        {
            var injector = new DllInjector(
                is64BitProcess: () => false,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Valid,
                isMonitorAlreadyLoaded: _ => false,
                injectLibrary: (_, _) => { },
                injectLibraryViaWow64Helper: (_, _) => throw new InvalidOperationException("helper path should not run"));
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.Loaded, result.State);
        }

        [Fact]
        public void Inject_WhenMonitorIsAlreadyLoaded_StillRequestsStartup()
        {
            bool startupRequested = false;
            var injector = new DllInjector(
                is64BitProcess: () => false,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Valid,
                isMonitorAlreadyLoaded: _ => true,
                injectLibrary: (_, _) => startupRequested = true,
                injectLibraryViaWow64Helper: (_, _) => throw new InvalidOperationException("helper path should not run"));
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.AlreadyInjected, result.State);
            Assert.True(startupRequested);
        }

        [Fact]
        public void Inject_WhenStartMonitorExportResolutionFails_ReturnsFailed()
        {
            var injector = new DllInjector(
                is64BitProcess: () => false,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Valid,
                isMonitorAlreadyLoaded: _ => false,
                injectLibrary: (_, _) => throw new InvalidOperationException("BO2Monitor export not found: StartMonitor"),
                injectLibraryViaWow64Helper: (_, _) => throw new InvalidOperationException("helper path should not run"));
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.Failed, result.State);
            Assert.Contains("StartMonitor", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Inject_WhenStartMonitorReturnsFailure_ReturnsFailed()
        {
            var injector = new DllInjector(
                is64BitProcess: () => false,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Valid,
                isMonitorAlreadyLoaded: _ => false,
                injectLibrary: (_, _) => throw new InvalidOperationException("BO2Monitor StartMonitor failed with result 0"),
                injectLibraryViaWow64Helper: (_, _) => throw new InvalidOperationException("helper path should not run"));
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.Failed, result.State);
            Assert.Contains("StartMonitor", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Inject_WhenMonitorDllValidationFails_ReturnsFailedBeforeNativeCalls()
        {
            bool nativeCalled = false;
            var injector = new DllInjector(
                is64BitProcess: () => false,
                resolveMonitorPath: () => @"C:\app\BO2Monitor.dll",
                fileExists: _ => true,
                validateMonitorDll: _ => DllInjector.DllPayloadValidationResult.Invalid("bad payload"),
                isMonitorAlreadyLoaded: _ => false,
                injectLibrary: (_, _) => nativeCalled = true,
                injectLibraryViaWow64Helper: (_, _) => nativeCalled = true);
            var detectedGame = CreateSupportedGame();

            DllInjectionResult result = injector.Inject(detectedGame);

            Assert.Equal(DllInjectionState.Failed, result.State);
            Assert.False(nativeCalled);
        }

        [Fact]
        public void HasExpectedPeMachine_WhenMachineMatches_ReturnsTrue()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
            try
            {
                File.WriteAllBytes(path, CreateMinimalPe(machine: 0x014c));

                Assert.True(DllInjector.HasExpectedPeMachine(path, 0x014c));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void HasExpectedPeMachine_WhenMachineDiffers_ReturnsFalse()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
            try
            {
                File.WriteAllBytes(path, CreateMinimalPe(machine: 0x8664));

                Assert.False(DllInjector.HasExpectedPeMachine(path, 0x014c));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ValidateInjectorHelper_WhenMachineMatches_ReturnsValid()
        {
            string path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".exe");
            try
            {
                File.WriteAllBytes(path, CreateMinimalPe(machine: 0x014c));

                DllInjector.DllPayloadValidationResult result = DllInjector.ValidateInjectorHelper(path);

                Assert.True(result.IsValid);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ValidateInjectorHelper_WhenMachineDiffers_ReturnsInvalid()
        {
            string path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".exe");
            try
            {
                File.WriteAllBytes(path, CreateMinimalPe(machine: 0x8664));

                DllInjector.DllPayloadValidationResult result = DllInjector.ValidateInjectorHelper(path);

                Assert.False(result.IsValid);
                Assert.Contains("DllInjectionInvalidHelperMachineFormat", result.Message, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static DetectedGame CreateSupportedGame()
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                42,
                PlayerStatAddressMap.SteamZombies,
                "Supported");
        }

        private static byte[] CreateMinimalPe(ushort machine)
        {
            byte[] bytes = new byte[0x80];
            bytes[0] = 0x4D;
            bytes[1] = 0x5A;
            BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3C);
            bytes[0x40] = 0x50;
            bytes[0x41] = 0x45;
            bytes[0x42] = 0;
            bytes[0x43] = 0;
            BitConverter.GetBytes(machine).CopyTo(bytes, 0x44);
            return bytes;
        }
    }
}
