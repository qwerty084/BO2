using System;
using System.IO;
using System.Linq;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryStoreTests
    {
        [Fact]
        public void Load_WhenFileIsMissing_ReturnsDefaultDocument()
        {
            var store = new GameHistoryStore(CreateHistoryPath());

            GameHistoryDocument document = store.Load();

            Assert.Equal(GameHistoryDocument.CurrentVersion, document.Version);
            Assert.Empty(document.Entries);
            Assert.Null(store.LastLoadRecovery);
        }

        [Fact]
        public void SaveAndLoad_PreservesSummaryRoundsAndBoxEvents()
        {
            string historyPath = CreateHistoryPath();
            var store = new GameHistoryStore(historyPath);
            GameHistoryEntry entry = CreateEntry("saved-1", daysOffset: 0);
            entry.Rounds =
            [
                new GameHistoryRound
                {
                    RoundNumber = 1,
                    RoundDuration = TimeSpan.FromSeconds(45),
                    CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                    DeltaStats = CreateStats(500, 7, 0, 0, 3)
                }
            ];
            entry.BoxEvents =
            [
                new GameHistoryBoxEvent
                {
                    ReceivedAt = entry.StartedAt.AddMinutes(5),
                    RoundNumber = 1,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 100
                }
            ];

            store.Save(new GameHistoryDocument { Entries = [entry] });
            GameHistoryDocument loaded = store.Load();

            GameHistoryEntry loadedEntry = Assert.Single(loaded.Entries);
            Assert.Equal("saved-1", loadedEntry.Id);
            Assert.Equal("Town", loadedEntry.MapIdentity.FriendlyName);
            Assert.Equal(12, loadedEntry.FinalRound);
            Assert.Equal(12345, loadedEntry.FinalStats.Points);
            Assert.Equal(TimeSpan.FromMinutes(62), loadedEntry.GameDuration);
            GameHistoryRound round = Assert.Single(loadedEntry.Rounds);
            Assert.Equal(1, round.RoundNumber);
            Assert.Equal(TimeSpan.FromSeconds(45), round.RoundDuration);
            Assert.Equal(500, round.DeltaStats.Points);
            GameHistoryBoxEvent boxEvent = Assert.Single(loadedEntry.BoxEvents);
            Assert.Equal("ray_gun_zm", boxEvent.RawWeaponToken);
            Assert.Equal("Ray Gun", boxEvent.WeaponDisplayName);
            Assert.Equal((uint)7, boxEvent.OwnerId);
        }

        [Theory]
        [InlineData("buried-run", "zm_buried", "Buried")]
        [InlineData("die-rise-run", "zm_highrise", "Die Rise")]
        [InlineData("mob-of-the-dead-run", "zm_prison", "Mob of the Dead")]
        [InlineData("nuketown-run", "zm_nuked", "Nuketown")]
        [InlineData("origins-run", "zm_tomb", "Origins")]
        public void SaveAndLoad_PreservesStandaloneMapIdentity(
            string id,
            string mapToken,
            string friendlyName)
        {
            string historyPath = CreateHistoryPath();
            var store = new GameHistoryStore(historyPath);
            GameHistoryEntry entry = CreateEntry(id, daysOffset: 0);
            entry.MapIdentity = new GameHistoryMapIdentity(mapToken, null, mapToken, friendlyName);

            store.Save(new GameHistoryDocument { Entries = [entry] });
            GameHistoryDocument loaded = store.Load();

            GameHistoryEntry loadedEntry = Assert.Single(loaded.Entries);
            Assert.Equal(mapToken, loadedEntry.MapIdentity.BaseMapToken);
            Assert.Null(loadedEntry.MapIdentity.StartLocationToken);
            Assert.Equal(mapToken, loadedEntry.MapIdentity.InternalMapToken);
            Assert.Equal(friendlyName, loadedEntry.MapIdentity.FriendlyName);
        }

        [Fact]
        public void Load_WhenJsonIsInvalid_MovesBadFileToBackup()
        {
            string historyPath = CreateHistoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            File.WriteAllText(historyPath, "{not json");
            var store = new GameHistoryStore(historyPath);

            GameHistoryDocument document = store.Load();

            Assert.Empty(document.Entries);
            Assert.False(File.Exists(historyPath));
            GameHistoryLoadRecovery recovery = Assert.IsType<GameHistoryLoadRecovery>(store.LastLoadRecovery);
            Assert.NotNull(recovery.BackupPath);
            Assert.True(File.Exists(recovery.BackupPath));
            Assert.Equal("{not json", File.ReadAllText(recovery.BackupPath));
        }

        [Fact]
        public void Load_WhenVersionIsNewer_MovesUnsupportedFileToBackup()
        {
            string historyPath = CreateHistoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            File.WriteAllText(
                historyPath,
                """
                {
                  "Version": 999,
                  "Entries": []
                }
                """);
            var store = new GameHistoryStore(historyPath);

            GameHistoryDocument document = store.Load();

            Assert.Empty(document.Entries);
            Assert.False(File.Exists(historyPath));
            GameHistoryLoadRecovery recovery = Assert.IsType<GameHistoryLoadRecovery>(store.LastLoadRecovery);
            Assert.NotNull(recovery.BackupPath);
            Assert.True(File.Exists(recovery.BackupPath));
        }

        [Fact]
        public void LoadSummariesNewestFirst_PreservesUnboundedEntriesNewestFirst()
        {
            string historyPath = CreateHistoryPath();
            var store = new GameHistoryStore(historyPath);
            GameHistoryDocument document = new()
            {
                Entries =
                [
                    CreateEntry("older", daysOffset: -2),
                    CreateEntry("newer", daysOffset: 0),
                    CreateEntry("middle", daysOffset: -1)
                ]
            };

            store.Save(document);
            var summaries = store.LoadSummariesNewestFirst();

            Assert.Equal(["newer", "middle", "older"], summaries.Select(summary => summary.Id));
            Assert.Equal(3, summaries.Count);
        }

        private static GameHistoryEntry CreateEntry(string id, int daysOffset)
        {
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            startedAt = startedAt.AddDays(daysOffset);
            return new GameHistoryEntry
            {
                Id = id,
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(62),
                MapIdentity = new GameHistoryMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"),
                FinalRound = 12,
                FinalStats = CreateStats(12345, 98, 2, 4, 55),
                GameDuration = TimeSpan.FromMinutes(62)
            };
        }

        private static GameHistoryStats CreateStats(
            int points,
            int kills,
            int downs,
            int revives,
            int headshots)
        {
            return new GameHistoryStats
            {
                Points = points,
                Kills = kills,
                Downs = downs,
                Revives = revives,
                Headshots = headshots
            };
        }

        private static string CreateHistoryPath()
        {
            return Path.GetFullPath(Path.Join(
                Path.GetTempPath(),
                "BO2.Tests",
                Guid.NewGuid().ToString("N"),
                "game-history.json"));
        }
    }
}
