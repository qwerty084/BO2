using BO2.Services;
using BO2.ViewModels;
using System;
using System.Globalization;
using System.Reflection;
using Xunit;

namespace BO2.Tests.ViewModels
{
    public sealed class HomeStatsViewModelTests
    {
        [Fact]
        public void ApplySnapshot_WhenSupportedStatsWithoutMonitor_ProjectsHomeDisplayState()
        {
            HomeStatsViewModel viewModel = new();
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                CreateStats(),
                "ConnectedStatus",
                ConnectionState.Connected);

            viewModel.ApplySnapshot(new GameConnectionSnapshot(
                detectedGame,
                readResult,
                GameEventMonitorStatus.WaitingForMonitor,
                DllInjectionResult.NotAttempted,
                IsConnecting: false,
                IsDisconnecting: false,
                CanAttemptConnect: true,
                HasInjectionAttemptForCurrentGame: false,
                IsMonitorConnectedForCurrentGame: false));

            Assert.Equal("Steam Zombies", viewModel.DetectedGameText);
            Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), viewModel.PointsText);
            Assert.Equal(12.345f.ToString("N2", CultureInfo.CurrentCulture), viewModel.PositionXText);
            Assert.Equal("GameProcessDetectorDisplayNameSteamZombies", viewModel.EventCompatibilityText);
            Assert.Equal("DllInjectionWaitingForConnect", viewModel.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForConnect", viewModel.EventMonitorStatusText);
            Assert.Equal("RecentEventsEmpty", viewModel.BoxEventsText);
        }

        [Fact]
        public void PresentationStateContract_ExposesReadOnlyStateWithoutConnectionCommands()
        {
            PropertyInfo[] properties = typeof(IHomeStatsPresentationState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);
            MethodInfo[] publicMethods = typeof(HomeStatsViewModel)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public);

            Assert.All(properties, property => Assert.Null(property.SetMethod));
            Assert.DoesNotContain(publicMethods, IsConnectionCommand);
        }

        private static bool IsConnectionCommand(MethodInfo method)
        {
            return method.Name.Contains("Connect", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Disconnect", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase);
        }

        private static PlayerStats CreateStats()
        {
            return new PlayerStats(
                1234,
                5,
                1,
                2,
                3,
                new PlayerCandidateStats(
                    PositionX: 12.345f,
                    PositionY: null,
                    PositionZ: -1.25f,
                    LegacyHealth: 100,
                    PlayerInfoHealth: null,
                    GEntityPlayerHealth: 90,
                    VelocityX: 1.5f,
                    VelocityY: null,
                    VelocityZ: null,
                    Gravity: 800,
                    Speed: null,
                    LastJumpHeight: null,
                    AdsAmount: null,
                    ViewAngleX: null,
                    ViewAngleY: null,
                    HeightInt: null,
                    HeightFloat: null,
                    AmmoSlot0: 30,
                    AmmoSlot1: null,
                    LethalAmmo: null,
                    AmmoSlot2: null,
                    TacticalAmmo: null,
                    AmmoSlot3: null,
                    AmmoSlot4: null,
                    AlternateKills: null,
                    AlternateHeadshots: null,
                    SecondaryKills: null,
                    SecondaryHeadshots: null,
                    Round: 7));
        }

        private static DetectedGame CreateSupportedGame(int processId)
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                processId,
                PlayerStatAddressMap.SteamZombies,
                null);
        }
    }
}
