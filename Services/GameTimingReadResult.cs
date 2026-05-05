using System;

namespace BO2.Services
{
    internal enum GameTimingReadStatus
    {
        SupportedTiming,
        UnsupportedTiming,
        InvalidTimingSourceState,
        InactiveLobbyState,
        GenericReadFailure
    }

    internal sealed record GameTimingReadResult
    {
        private GameTimingReadResult(
            DetectedGame detectedGame,
            GameTimingReadStatus status,
            TimeSpan? gameTime,
            bool? isPaused)
        {
            ArgumentNullException.ThrowIfNull(detectedGame);

            if (gameTime is { } elapsed && elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(gameTime), "Game timing cannot be negative.");
            }

            DetectedGame = detectedGame;
            Status = status;
            GameTime = gameTime;
            IsPaused = isPaused;
        }

        public DetectedGame DetectedGame { get; }

        public GameTimingReadStatus Status { get; }

        public TimeSpan? GameTime { get; }

        public bool? IsPaused { get; }

        public static GameTimingReadResult SupportedTiming(
            DetectedGame detectedGame,
            TimeSpan gameTime,
            bool isPaused)
        {
            return new GameTimingReadResult(
                detectedGame,
                GameTimingReadStatus.SupportedTiming,
                gameTime,
                isPaused);
        }

        public static GameTimingReadResult UnsupportedTiming(DetectedGame detectedGame)
        {
            return WithoutTiming(detectedGame, GameTimingReadStatus.UnsupportedTiming);
        }

        public static GameTimingReadResult InvalidTimingSourceState(DetectedGame detectedGame)
        {
            return WithoutTiming(detectedGame, GameTimingReadStatus.InvalidTimingSourceState);
        }

        public static GameTimingReadResult InactiveLobbyState(DetectedGame detectedGame)
        {
            return WithoutTiming(detectedGame, GameTimingReadStatus.InactiveLobbyState);
        }

        public static GameTimingReadResult GenericReadFailure(DetectedGame detectedGame)
        {
            return WithoutTiming(detectedGame, GameTimingReadStatus.GenericReadFailure);
        }

        private static GameTimingReadResult WithoutTiming(
            DetectedGame detectedGame,
            GameTimingReadStatus status)
        {
            return new GameTimingReadResult(detectedGame, status, null, null);
        }
    }
}
