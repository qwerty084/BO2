using System;
using System.Collections.Generic;

namespace BO2.Services
{
    internal sealed class GameHistoryDocument
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        public List<GameHistoryEntry> Entries { get; set; } = [];

        public static GameHistoryDocument CreateDefault()
        {
            return new GameHistoryDocument();
        }

        public void Normalize()
        {
            Version = CurrentVersion;
            Entries ??= [];

            foreach (GameHistoryEntry entry in Entries)
            {
                entry.Normalize();
            }
        }
    }

    internal record GameHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset EndedAt { get; set; }

        public DateTimeOffset SortDate => EndedAt == default ? StartedAt : EndedAt;

        public GameHistoryMapIdentity MapIdentity { get; set; } = new(
            string.Empty,
            null,
            string.Empty,
            string.Empty);

        public int FinalRound { get; set; }

        public GameHistoryStats FinalStats { get; set; } = new();

        public TimeSpan? GameDuration { get; set; }

        public IReadOnlyList<GameHistoryRound> Rounds { get; set; } = [];

        public IReadOnlyList<GameHistoryBoxEvent> BoxEvents { get; set; } = [];

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }

            MapIdentity ??= new GameHistoryMapIdentity(string.Empty, null, string.Empty, string.Empty);
            FinalStats ??= new GameHistoryStats();
            Rounds ??= [];
            BoxEvents ??= [];
        }
    }

    internal sealed record GameHistoryMapIdentity(
        string BaseMapToken,
        string? StartLocationToken,
        string InternalMapToken,
        string FriendlyName)
    {
        public static GameHistoryMapIdentity FromGameMapIdentity(GameMapIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);

            return new GameHistoryMapIdentity(
                identity.BaseMapToken,
                identity.StartLocationToken,
                identity.InternalMapToken,
                identity.DisplayName);
        }
    }

    internal sealed record GameHistoryStats
    {
        public GameHistoryStats()
        {
        }

        public GameHistoryStats(int Points, int Kills, int Downs, int Revives, int Headshots)
        {
            this.Points = Points;
            this.Kills = Kills;
            this.Downs = Downs;
            this.Revives = Revives;
            this.Headshots = Headshots;
        }

        public int Points { get; set; }

        public int Kills { get; set; }

        public int Downs { get; set; }

        public int Revives { get; set; }

        public int Headshots { get; set; }

        public static GameHistoryStats FromPlayerStats(PlayerStats stats)
        {
            ArgumentNullException.ThrowIfNull(stats);

            return new GameHistoryStats
            {
                Points = stats.Points,
                Kills = stats.Kills,
                Downs = stats.Downs,
                Revives = stats.Revives,
                Headshots = stats.Headshots
            };
        }

        public static GameHistoryStats Subtract(GameHistoryStats end, GameHistoryStats start)
        {
            ArgumentNullException.ThrowIfNull(end);
            ArgumentNullException.ThrowIfNull(start);

            return new GameHistoryStats
            {
                Points = end.Points - start.Points,
                Kills = end.Kills - start.Kills,
                Downs = end.Downs - start.Downs,
                Revives = end.Revives - start.Revives,
                Headshots = end.Headshots - start.Headshots
            };
        }
    }

    internal sealed record GameHistorySavedGame : GameHistoryEntry;

    internal sealed class GameHistoryRound
    {
        public int RoundNumber { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset EndedAt { get; set; }

        public GameHistoryStats CumulativeStats { get; set; } = new();

        public GameHistoryStats DeltaStats { get; set; } = new();

        public TimeSpan? RoundDuration { get; set; }

        public TimeSpan? Duration
        {
            get => RoundDuration;
            set => RoundDuration = value;
        }
    }

    internal sealed class GameHistoryBoxEvent
    {
        public DateTimeOffset ReceivedAt { get; set; }

        public int RoundNumber { get; set; }

        public string EventName { get; set; } = string.Empty;

        public string? RawWeaponToken { get; set; }

        public string? WeaponDisplayName { get; set; }

        public string? FriendlyWeaponName
        {
            get => WeaponDisplayName;
            set => WeaponDisplayName = value;
        }

        public uint OwnerId { get; set; }

        public uint StringValue { get; set; }
    }

    internal sealed record GameHistorySummary(
        string Id,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        GameHistoryMapIdentity MapIdentity,
        int FinalRound,
        GameHistoryStats FinalStats,
        TimeSpan? GameDuration);
}
