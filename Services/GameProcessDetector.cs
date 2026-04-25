using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    public sealed class GameProcessDetector
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

        public DetectedGame? Detect()
        {
            List<DetectedGame> detectedGames = [];

            foreach (GameProcessDefinition definition in Definitions)
            {
                Process[] processes = Process.GetProcessesByName(definition.ProcessName);

                foreach (Process process in processes)
                {
                    if (!IsCommandLineMatch(definition, process.Id))
                    {
                        process.Dispose();
                        continue;
                    }

                    detectedGames.Add(new DetectedGame(
                        definition.Variant,
                        definition.DisplayName,
                        definition.ProcessName,
                        process.Id,
                        definition.AddressMap,
                        definition.UnsupportedReason));

                    process.Dispose();
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
            return commandLine?.Contains(definition.CommandLineToken, System.StringComparison.OrdinalIgnoreCase) == true;
        }

        private readonly Dictionary<int, CommandLineCacheEntry> _commandLineCache = [];

        private static string? GetCommandLine(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId.ToString(CultureInfo.InvariantCulture)}";

            try
            {
                using ManagementObjectSearcher searcher = new(query);
                using ManagementObjectCollection results = searcher.Get();

                foreach (ManagementBaseObject result in results)
                {
                    using (result)
                    {
                        return result["CommandLine"] as string;
                    }
                }
            }
            catch (ManagementException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }

            return null;
        }

        private string? GetCachedCommandLine(int processId)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RemoveExpiredCommandLineCacheEntries(now);

            if (_commandLineCache.TryGetValue(processId, out CommandLineCacheEntry? cacheEntry)
                && now - cacheEntry.CachedAt <= CommandLineCacheDuration)
            {
                return cacheEntry.CommandLine;
            }

            string? commandLine = GetCommandLine(processId);
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
    }
}
