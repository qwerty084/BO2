using System;
using System.Reflection;
using BO2.Services;
using BO2.ViewModels;
using Xunit;

namespace BO2.Tests.ViewModels
{
    public sealed class CurrentGamePageViewModelTests
    {
        [Fact]
        public void PresentationStateContract_ExposesReadOnlyStateWithoutConnectionCommands()
        {
            PropertyInfo[] properties = typeof(CurrentGamePageViewModel)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);
            MethodInfo[] publicMethods = typeof(CurrentGamePageViewModel)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public);

            Assert.All(properties, property => Assert.Null(property.GetSetMethod()));
            Assert.DoesNotContain(publicMethods, IsConnectionCommand);
        }

        [Fact]
        public void PresentationStateContract_DoesNotExposeCandidateAddressOrDebugProperties()
        {
            PropertyInfo[] properties = typeof(CurrentGamePageViewModel)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.DoesNotContain(properties, property => ContainsPresentationDetail(property.Name));
        }

        [Fact]
        public void ApplySnapshot_UpdatesTimerTextProperties()
        {
            var viewModel = new CurrentGamePageViewModel();
            var snapshot = new GameConnectionSnapshot(
                CurrentGame: null,
                ConnectionPhase: GameConnectionPhase.NoGame,
                ReadResult: null,
                EventMonitorSummary: GameConnectionEventMonitorSummary.Waiting,
                TimerDisplayState: new GameConnectionTimerDisplayState(
                    TimerDisplayState.Active(TimeSpan.FromSeconds(65)),
                    TimerDisplayState.Frozen(TimeSpan.FromMilliseconds(125_999))),
                ConnectCommandAvailability: GameConnectionCommandAvailability.VisibleDisabled,
                DisconnectCommandAvailability: GameConnectionCommandAvailability.Hidden);

            viewModel.ApplySnapshot(snapshot);

            Assert.Equal("1:05", viewModel.GameTimeText);
            Assert.Equal("2:05", viewModel.RoundTimeText);
        }

        private static bool IsConnectionCommand(MethodInfo method)
        {
            return method.Name.Contains("Connect", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Disconnect", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsPresentationDetail(string memberName)
        {
            return memberName.Contains("Candidate", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Address", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Debug", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Evidence", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Injection", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("LowLevel", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Monitor", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Pause", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Position", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Reason", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Sequence", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Source", StringComparison.OrdinalIgnoreCase)
                || memberName.Contains("Stale", StringComparison.OrdinalIgnoreCase);
        }
    }
}
