using System;

namespace BO2.Services
{
    public enum GameConnectionEventMonitorState
    {
        Waiting,
        Connecting,
        Ready,
        UnsupportedVersion,
        CaptureDisabled,
        PollingFallback,
        ReadinessFailed,
        LoadingFailed
    }

    internal sealed record GameConnectionEventMonitorSummary(
        GameConnectionEventMonitorState State,
        GameEventMonitorStatus Status,
        string? FailureMessage = null)
    {
        public static GameConnectionEventMonitorSummary Waiting { get; } = new(
            GameConnectionEventMonitorState.Waiting,
            GameEventMonitorStatus.WaitingForMonitor);

        public static GameConnectionEventMonitorSummary Connecting { get; } = new(
            GameConnectionEventMonitorState.Connecting,
            GameEventMonitorStatus.WaitingForMonitor);

        public static GameConnectionEventMonitorSummary ReadinessFailed(string failureMessage)
        {
            return new GameConnectionEventMonitorSummary(
                GameConnectionEventMonitorState.ReadinessFailed,
                GameEventMonitorStatus.WaitingForMonitor,
                failureMessage);
        }

        public static GameConnectionEventMonitorSummary LoadingFailed(string failureMessage)
        {
            return new GameConnectionEventMonitorSummary(
                GameConnectionEventMonitorState.LoadingFailed,
                GameEventMonitorStatus.WaitingForMonitor,
                failureMessage);
        }

        public static GameConnectionEventMonitorSummary FromStatus(GameEventMonitorStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);

            GameConnectionEventMonitorState state = status.CompatibilityState switch
            {
                GameCompatibilityState.Compatible => GameConnectionEventMonitorState.Ready,
                GameCompatibilityState.UnsupportedVersion => GameConnectionEventMonitorState.UnsupportedVersion,
                GameCompatibilityState.CaptureDisabled => GameConnectionEventMonitorState.CaptureDisabled,
                GameCompatibilityState.PollingFallback => GameConnectionEventMonitorState.PollingFallback,
                _ => GameConnectionEventMonitorState.Waiting
            };

            return new GameConnectionEventMonitorSummary(state, status);
        }
    }
}
