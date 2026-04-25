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
                "Steam Zombies",
                "t6zm",
                null,
                PlayerStatAddressMap.SteamZombies,
                null),
            new(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zmv41",
                null,
                null,
                "No confirmed Redacted Zombies address map"),
            new(
                GameVariant.PlutoniumZombies,
                "Plutonium Zombies",
                "plutonium-bootstrapper-win32",
                "t6zm",
                null,
                "No confirmed Plutonium Zombies address map"),
            new(
                GameVariant.PlutoniumMultiplayer,
                "Plutonium Multiplayer",
                "plutonium-bootstrapper-win32",
                "t6mp",
                null,
                "No confirmed Plutonium Multiplayer address map"),
            new(
                GameVariant.PlutoniumUnknown,
                "Plutonium T6",
                "plutonium-bootstrapper-win32",
                null,
                null,
                "No confirmed Plutonium address map"),
            new(
                GameVariant.SteamMultiplayer,
                "Steam Multiplayer",
                "t6mp",
                null,
                null,
                "No confirmed Steam Multiplayer address map"),
            new(
                GameVariant.RedactedMultiplayer,
                "Redacted Multiplayer",
                "t6mpv43",
                null,
                null,
                "No confirmed Redacted Multiplayer address map"),
            new(
                GameVariant.SteamSinglePlayer,
                "Steam Campaign",
                "t6sp",
                null,
                null,
                "No confirmed Steam Campaign address map")
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
            if (_commandLineCache.TryGetValue(processId, out CommandLineCacheEntry? cacheEntry)
                && now - cacheEntry.CachedAt <= CommandLineCacheDuration)
            {
                return cacheEntry.CommandLine;
            }

            string? commandLine = GetCommandLine(processId);
            _commandLineCache[processId] = new CommandLineCacheEntry(commandLine, now);
            return commandLine;
        }

        private sealed record CommandLineCacheEntry(string? CommandLine, DateTimeOffset CachedAt);
    }
}
