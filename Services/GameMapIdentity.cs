using System;

namespace BO2.Services
{
    internal enum GameMapIdentityReadStatus
    {
        ConfirmedTown,
        UnsupportedVariant,
        MissingMapIdentity,
        UnsupportedMapIdentity,
        Unreadable,
        Malformed
    }

    internal sealed record GameMapIdentity(
        string BaseMapToken,
        string? StartLocationToken,
        string InternalMapToken,
        string DisplayName);

    internal sealed record GameMapIdentityReadResult
    {
        private GameMapIdentityReadResult(
            DetectedGame detectedGame,
            GameMapIdentityReadStatus status,
            GameMapIdentity? identity)
        {
            ArgumentNullException.ThrowIfNull(detectedGame);

            DetectedGame = detectedGame;
            Status = status;
            Identity = identity;
        }

        public DetectedGame DetectedGame { get; }

        public GameMapIdentityReadStatus Status { get; }

        public GameMapIdentity? Identity { get; }

        public bool IsConfirmedTown => Status == GameMapIdentityReadStatus.ConfirmedTown
            && string.Equals(Identity?.DisplayName, "Town", StringComparison.Ordinal);

        public static GameMapIdentityReadResult ConfirmedTown(
            DetectedGame detectedGame,
            GameMapIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);

            return new GameMapIdentityReadResult(
                detectedGame,
                GameMapIdentityReadStatus.ConfirmedTown,
                identity);
        }

        public static GameMapIdentityReadResult UnsupportedVariant(DetectedGame detectedGame)
        {
            return Unavailable(detectedGame, GameMapIdentityReadStatus.UnsupportedVariant);
        }

        public static GameMapIdentityReadResult MissingMapIdentity(DetectedGame detectedGame)
        {
            return Unavailable(detectedGame, GameMapIdentityReadStatus.MissingMapIdentity);
        }

        public static GameMapIdentityReadResult UnsupportedMapIdentity(DetectedGame detectedGame)
        {
            return Unavailable(detectedGame, GameMapIdentityReadStatus.UnsupportedMapIdentity);
        }

        public static GameMapIdentityReadResult Unreadable(DetectedGame detectedGame)
        {
            return Unavailable(detectedGame, GameMapIdentityReadStatus.Unreadable);
        }

        public static GameMapIdentityReadResult Malformed(DetectedGame detectedGame)
        {
            return Unavailable(detectedGame, GameMapIdentityReadStatus.Malformed);
        }

        private static GameMapIdentityReadResult Unavailable(
            DetectedGame detectedGame,
            GameMapIdentityReadStatus status)
        {
            return new GameMapIdentityReadResult(detectedGame, status, null);
        }
    }

    internal static class GameMapIdentityResolver
    {
        public static GameMapIdentityReadResult ResolveTownOnly(
            DetectedGame detectedGame,
            string? baseMapToken,
            string? startLocationToken)
        {
            ArgumentNullException.ThrowIfNull(detectedGame);

            string? normalizedBaseToken = NormalizeToken(baseMapToken);
            if (normalizedBaseToken is null)
            {
                return GameMapIdentityReadResult.MissingMapIdentity(detectedGame);
            }

            if (!string.Equals(normalizedBaseToken, "zm_transit", StringComparison.Ordinal))
            {
                return GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame);
            }

            string? normalizedStartLocation = NormalizeToken(startLocationToken);
            if (normalizedStartLocation is null)
            {
                return GameMapIdentityReadResult.MissingMapIdentity(detectedGame);
            }

            if (!string.Equals(normalizedStartLocation, "town", StringComparison.Ordinal))
            {
                return GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame);
            }

            return GameMapIdentityReadResult.ConfirmedTown(
                detectedGame,
                new GameMapIdentity(
                    normalizedBaseToken,
                    normalizedStartLocation,
                    "zm_transit_gump_town",
                    "Town"));
        }

        private static string? NormalizeToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            return token.Trim().ToLowerInvariant();
        }
    }
}
