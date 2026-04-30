using System;

namespace BO2.Services
{
    internal sealed class StatsRefreshService
    {
        private readonly GameSessionCoordinator _gameSession;

        public StatsRefreshService(GameSessionCoordinator gameSession)
        {
            ArgumentNullException.ThrowIfNull(gameSession);

            _gameSession = gameSession;
        }

        public StatsRefreshSnapshot Read(DetectedGame? detectedGame, DateTimeOffset receivedAt)
        {
            long diagnosticsStartedAt = RefreshDiagnostics.Start();
            try
            {
                PlayerStatsReadResult readResult = _gameSession.ReadPlayerStats(detectedGame);
                GameEventMonitorStatus eventStatus = _gameSession.ReadEventMonitorStatus(
                    receivedAt,
                    readResult.DetectedGame?.ProcessId);
                return new StatsRefreshSnapshot(readResult, eventStatus);
            }
            finally
            {
                RefreshDiagnostics.WriteElapsed("stats refresh", diagnosticsStartedAt);
            }
        }
    }

    internal readonly record struct StatsRefreshSnapshot(
        PlayerStatsReadResult ReadResult,
        GameEventMonitorStatus EventStatus);
}
