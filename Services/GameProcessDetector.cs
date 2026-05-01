using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BO2.Services
{
    public sealed class GameProcessDetector : IGameProcessDetector
    {
        private static readonly TimeSpan CommandLineCacheDuration = TimeSpan.FromSeconds(5);
        private static readonly GameProcessDefinition[] Definitions =
        [
            new(
                GameVariant.SteamZombies,
                AppStrings.Get("GameProcessDetectorDisplayNameSteamZombies"),
                "t6zm",
                null,
                PlayerStatAddressMap.SteamZombies,
                null),
            new(
                GameVariant.RedactedZombies,
                AppStrings.Get("GameProcessDetectorDisplayNameRedactedZombies"),
                "t6zmv41",
                null,
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonRedactedZombies")),
            new(
                GameVariant.PlutoniumZombies,
                AppStrings.Get("GameProcessDetectorDisplayNamePlutoniumZombies"),
                "plutonium-bootstrapper-win32",
                "t6zm",
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonPlutoniumZombies")),
            new(
                GameVariant.PlutoniumMultiplayer,
                AppStrings.Get("GameProcessDetectorDisplayNamePlutoniumMultiplayer"),
                "plutonium-bootstrapper-win32",
                "t6mp",
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonPlutoniumMultiplayer")),
            new(
                GameVariant.PlutoniumUnknown,
                AppStrings.Get("GameProcessDetectorDisplayNamePlutoniumUnknown"),
                "plutonium-bootstrapper-win32",
                null,
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonPlutoniumUnknown")),
            new(
                GameVariant.SteamMultiplayer,
                AppStrings.Get("GameProcessDetectorDisplayNameSteamMultiplayer"),
                "t6mp",
                null,
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonSteamMultiplayer")),
            new(
                GameVariant.RedactedMultiplayer,
                AppStrings.Get("GameProcessDetectorDisplayNameRedactedMultiplayer"),
                "t6mpv43",
                null,
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonRedactedMultiplayer")),
            new(
                GameVariant.SteamSinglePlayer,
                AppStrings.Get("GameProcessDetectorDisplayNameSteamSinglePlayer"),
                "t6sp",
                null,
                null,
                AppStrings.Get("GameProcessDetectorUnsupportedReasonSteamSinglePlayer"))
        ];
        private static readonly string[] KnownProcessNames = BuildKnownProcessNames();

        private readonly IProcessInfoProvider _processInfoProvider;
        private readonly TimeProvider _timeProvider;
        private readonly Dictionary<int, CommandLineCacheEntry> _commandLineCache = [];

        public GameProcessDetector()
            : this(new WindowsProcessInfoProvider(), TimeProvider.System)
        {
        }

        // Internal constructor used by unit tests to inject fakes for process discovery and time.
        internal GameProcessDetector(IProcessInfoProvider processInfoProvider, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(processInfoProvider);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _processInfoProvider = processInfoProvider;
            _timeProvider = timeProvider;
        }

        internal static IReadOnlyCollection<string> ProcessNames => KnownProcessNames;

        internal static bool IsKnownProcessName(string? processName)
        {
            string normalizedProcessName = NormalizeProcessName(processName);
            if (normalizedProcessName.Length == 0)
            {
                return false;
            }

            return KnownProcessNames.Any(knownProcessName =>
                string.Equals(knownProcessName, normalizedProcessName, StringComparison.OrdinalIgnoreCase));
        }

        public DetectedGame? Detect()
        {
            List<DetectedGame> detectedGames = [];

            foreach (GameProcessDefinition definition in Definitions)
            {
                int[] processIds = _processInfoProvider.GetProcessIds(definition.ProcessName);

                foreach (int processId in processIds)
                {
                    if (!IsCommandLineMatch(definition, processId))
                    {
                        continue;
                    }

                    detectedGames.Add(new DetectedGame(
                        definition.Variant,
                        definition.DisplayName,
                        definition.ProcessName,
                        processId,
                        definition.AddressMap,
                        definition.UnsupportedReason));
                }
            }

            return detectedGames.Find(game => game.IsStatsSupported) ?? (detectedGames.Count > 0 ? detectedGames[0] : null);
        }

        private bool IsCommandLineMatch(GameProcessDefinition definition, int processId)
        {
            if (definition.CommandLineToken is null)
            {
                return true;
            }

            string? commandLine = GetCachedCommandLine(processId);
            return commandLine?.Contains(definition.CommandLineToken, StringComparison.OrdinalIgnoreCase) == true;
        }

        private string? GetCachedCommandLine(int processId)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            RemoveExpiredCommandLineCacheEntries(now);

            if (_commandLineCache.TryGetValue(processId, out CommandLineCacheEntry? cacheEntry)
                && now - cacheEntry.CachedAt <= CommandLineCacheDuration)
            {
                return cacheEntry.CommandLine;
            }

            string? commandLine = _processInfoProvider.GetCommandLine(processId);
            _commandLineCache[processId] = new CommandLineCacheEntry(commandLine, now);
            return commandLine;
        }

        private void RemoveExpiredCommandLineCacheEntries(DateTimeOffset now)
        {
            List<int>? expiredProcessIds = null;
            foreach (KeyValuePair<int, CommandLineCacheEntry> cacheEntry in _commandLineCache)
            {
                if (now - cacheEntry.Value.CachedAt <= CommandLineCacheDuration)
                {
                    continue;
                }

                expiredProcessIds ??= [];
                expiredProcessIds.Add(cacheEntry.Key);
            }

            if (expiredProcessIds is null)
            {
                return;
            }

            foreach (int processId in expiredProcessIds)
            {
                _commandLineCache.Remove(processId);
            }
        }

        private sealed record CommandLineCacheEntry(string? CommandLine, DateTimeOffset CachedAt);

        private static string[] BuildKnownProcessNames()
        {
            HashSet<string> processNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameProcessDefinition definition in Definitions)
            {
                processNames.Add(definition.ProcessName);
            }

            string[] result = new string[processNames.Count];
            processNames.CopyTo(result);
            return result;
        }

        private static string NormalizeProcessName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileName(processName.Trim());
            return Path.GetFileNameWithoutExtension(fileName);
        }
    }
}
