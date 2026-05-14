using System;

namespace BO2.Services
{
    internal enum GameMapIdentityReadStatus
    {
        SupportedMap,
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

        public bool IsSupportedMap => Status == GameMapIdentityReadStatus.SupportedMap
            && Identity is not null;

        public static GameMapIdentityReadResult SupportedMap(
            DetectedGame detectedGame,
            GameMapIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);

            return new GameMapIdentityReadResult(
                detectedGame,
                GameMapIdentityReadStatus.SupportedMap,
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
        private const string GreenRunBaseMapToken = "zm_transit";

        private static readonly SupportedGreenRunMap[] GreenRunMaps =
        [
            new("town", "zm_transit_gump_town", "Town"),
            new("farm", "zm_transit_gump_farm", "Farm")
        ];

        public static GameMapIdentityReadResult ResolveSupportedMap(
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

            if (!string.Equals(normalizedBaseToken, GreenRunBaseMapToken, StringComparison.Ordinal))
            {
                return GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame);
            }

            string? normalizedStartLocation = NormalizeToken(startLocationToken);
            if (normalizedStartLocation is null)
            {
                return GameMapIdentityReadResult.MissingMapIdentity(detectedGame);
            }

            foreach (SupportedGreenRunMap map in GreenRunMaps)
            {
                if (string.Equals(normalizedStartLocation, map.StartLocationToken, StringComparison.Ordinal))
                {
                    return GameMapIdentityReadResult.SupportedMap(
                        detectedGame,
                        new GameMapIdentity(
                            normalizedBaseToken,
                            normalizedStartLocation,
                            map.InternalMapToken,
                            map.DisplayName));
                }
            }

            return GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame);
        }

        private static string? NormalizeToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            return token.Trim().ToLowerInvariant();
        }

        private sealed record SupportedGreenRunMap(
            string StartLocationToken,
            string InternalMapToken,
            string DisplayName);
    }
}
