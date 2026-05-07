using System;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameTimerStateTests
    {
        [Fact]
        public void ApplyLifecycleEvents_WhenRoundOneStartIsObserved_CapturesBaselineFromLatestTimingRead()
        {
            GameTimerState state = new();

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:00");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenRoundOneStartArrivesBeforeValidTimingRead_WaitsForFirstValidTimingRead()
        {
            GameTimerState state = new();

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateInvalidTimingRead());

            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 42_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:00");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");
        }

        [Fact]
        public void ApplyTimingRead_WhenGameTimeSourceFreezesDuringPause_DoesNotAdvanceTimers()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 20_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:10");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:10");

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 20_000, isPaused: true));

            AssertGameTimer(state, TimerDisplayKind.Frozen, "0:10");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:10");
        }

        [Fact]
        public void ApplyTimingRead_WhenRoundOneStartWasMissed_StartsObservedRoundTimerOnly()
        {
            GameTimerState state = new();

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 3)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 180_000));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenLaterRoundStartHasTimingBaseline_StartsRoundTimerFromZero()
        {
            GameTimerState state = new();

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 120_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 4)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 135_999));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:15");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenRoundStartArrivesBeforeValidTimingRead_CapturesRoundBaselineLater()
        {
            GameTimerState state = new();

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 2)));
            state.ApplyTimingRead(CreateInvalidTimingRead());

            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 52_000));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenRoundEndIsObserved_FreezesRoundTimerAtLastKnownValue()
        {
            GameTimerState state = new();
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 5)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 31_999));

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundEnd(round: 5)));

            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:21");

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 45_000));

            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:21");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenBetweenRounds_KeepsPreviousRoundUntilNextRoundStartResets()
        {
            GameTimerState state = new();
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 5_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 2)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 35_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundEnd(round: 2)));

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 50_000));

            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:30");

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 3)));

            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 62_000));

            AssertRoundTimer(state, TimerDisplayKind.Active, "0:12");
        }

        [Theory]
        [InlineData((int)GameTimingReadStatus.UnsupportedTiming)]
        [InlineData((int)GameTimingReadStatus.InvalidTimingSourceState)]
        [InlineData((int)GameTimingReadStatus.GenericReadFailure)]
        public void ApplyTimingRead_WhenNonSupportedReadIsNotInactiveLobby_PreservesExistingTimers(
            int statusValue)
        {
            GameTimingReadStatus status = (GameTimingReadStatus)statusValue;
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 42_000));

            state.ApplyTimingRead(CreateTimingRead(status));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:32");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:32");
        }

        [Fact]
        public void ApplyTimingRead_WhenInactiveLobbyStateIsConfirmed_ClearsTimersAndAllowsNewMatch()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 40_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateGameEnd()));

            AssertGameTimer(state, TimerDisplayKind.Frozen, "0:30");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:30");

            state.ApplyTimingRead(CreateTimingRead(GameTimingReadStatus.InactiveLobbyState));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 2_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 12_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:10");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:10");
        }

        [Fact]
        public void ApplyTimingRead_WhenSupportedReadMovesGameTimeBackward_PreservesLastKnownTimers()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 40_000));

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 25_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:30");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:30");

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 45_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:35");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:35");
        }

        [Fact]
        public void ApplyTimingRead_WhenSupportedReadWouldMakeElapsedNegative_DoesNotClampIntoNormalOutput()
        {
            GameTimerState state = new();
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 100_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 4)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 130_000));

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 90_000));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:30");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenSequenceGapIsObserved_FreezesKnownTimersAndStopsAdvancementUntilLobby()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 40_000));

            state.ApplyLifecycleEvents(CreateGappedLifecycleBatch(CreateGameEnd()));

            Assert.True(state.HasUntrustedLifecycleSequence);
            AssertGameTimer(state, TimerDisplayKind.Frozen, "0:30");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:30");

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 60_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 2)));

            AssertGameTimer(state, TimerDisplayKind.Frozen, "0:30");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:30");

            state.ApplyTimingRead(CreateTimingRead(GameTimingReadStatus.InactiveLobbyState));

            Assert.False(state.HasUntrustedLifecycleSequence);
            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 5_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:00");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:00");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenSequenceGapIsObservedWithMissingBaselines_KeepsPlaceholders()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 2)));

            state.ApplyLifecycleEvents(CreateGappedLifecycleBatch());
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 50_000));

            Assert.True(state.HasUntrustedLifecycleSequence);
            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenEndGameIsObserved_FreezesKnownTimersAndPreservesThroughReadFailures()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 70_000));

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateGameEnd()));

            AssertGameTimer(state, TimerDisplayKind.Frozen, "1:00");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "1:00");

            state.ApplyTimingRead(CreateTimingRead(GameTimingReadStatus.GenericReadFailure));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 90_000));

            AssertGameTimer(state, TimerDisplayKind.Frozen, "1:00");
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "1:00");
        }

        [Fact]
        public void ApplyLifecycleEvents_WhenEndGameIsObservedWithMissingGameTimer_LeavesGamePlaceholderAndFreezesRound()
        {
            GameTimerState state = new();
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 100_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 4)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 125_000));

            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateGameEnd()));

            AssertPlaceholderGameTimer(state);
            AssertRoundTimer(state, TimerDisplayKind.Frozen, "0:25");
        }

        [Fact]
        public void Reset_WhenTimersExist_ClearsTimersAndAllowsFreshMatch()
        {
            GameTimerState state = new();
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 10_000));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 30_000));

            state.Reset();

            AssertPlaceholderGameTimer(state);
            AssertRoundTimerPlaceholder(state);

            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 5_000));
            state.ApplyLifecycleEvents(CreateLifecycleBatch(CreateRoundStart(round: 1)));
            state.ApplyTimingRead(CreateTimingRead(gameTimeMilliseconds: 15_000));

            AssertGameTimer(state, TimerDisplayKind.Active, "0:10");
            AssertRoundTimer(state, TimerDisplayKind.Active, "0:10");
        }

        private static GameTimerLifecycleEventBatch CreateLifecycleBatch(
            params GameTimerLifecycleEvent[] events)
        {
            return new GameTimerLifecycleEventBatch(events, HasSequenceGap: false);
        }

        private static GameTimerLifecycleEventBatch CreateGappedLifecycleBatch(
            params GameTimerLifecycleEvent[] events)
        {
            return new GameTimerLifecycleEventBatch(events, HasSequenceGap: true);
        }

        private static GameTimerLifecycleEvent CreateRoundStart(int round)
        {
            return new GameTimerLifecycleEvent(
                Sequence: (ulong)round,
                GameEventType.StartOfRound,
                round,
                new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        }

        private static GameTimerLifecycleEvent CreateRoundEnd(int round)
        {
            return new GameTimerLifecycleEvent(
                Sequence: (ulong)round,
                GameEventType.EndOfRound,
                round,
                new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        }

        private static GameTimerLifecycleEvent CreateGameEnd()
        {
            return new GameTimerLifecycleEvent(
                Sequence: 99,
                GameEventType.EndGame,
                0,
                new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        }

        private static GameTimingReadResult CreateTimingRead(
            int gameTimeMilliseconds,
            bool isPaused = false)
        {
            return GameTimingReadResult.SupportedTiming(
                CreateDetectedGame(),
                TimeSpan.FromMilliseconds(gameTimeMilliseconds),
                isPaused);
        }

        private static GameTimingReadResult CreateTimingRead(GameTimingReadStatus status)
        {
            return status switch
            {
                GameTimingReadStatus.UnsupportedTiming => GameTimingReadResult.UnsupportedTiming(CreateDetectedGame()),
                GameTimingReadStatus.InvalidTimingSourceState => GameTimingReadResult.InvalidTimingSourceState(CreateDetectedGame()),
                GameTimingReadStatus.InactiveLobbyState => GameTimingReadResult.InactiveLobbyState(CreateDetectedGame()),
                GameTimingReadStatus.GenericReadFailure => GameTimingReadResult.GenericReadFailure(CreateDetectedGame()),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private static GameTimingReadResult CreateInvalidTimingRead()
        {
            return GameTimingReadResult.InvalidTimingSourceState(CreateDetectedGame());
        }

        private static DetectedGame CreateDetectedGame()
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                1001,
                PlayerStatAddressMap.SteamZombies,
                null);
        }

        private static void AssertPlaceholderGameTimer(GameTimerState state)
        {
            AssertGameTimer(state, TimerDisplayKind.Placeholder, TimerDisplayState.PlaceholderText);
        }

        private static void AssertGameTimer(
            GameTimerState state,
            TimerDisplayKind expectedKind,
            string expectedText)
        {
            TimerDisplayState? timer = state.DisplayState.GameTime;
            Assert.NotNull(timer);
            TimerDisplayState actualTimer = timer!;
            Assert.Equal(expectedKind, actualTimer.Kind);
            Assert.Equal(expectedText, Render(actualTimer));
        }

        private static void AssertRoundTimerPlaceholder(GameTimerState state)
        {
            AssertRoundTimer(state, TimerDisplayKind.Placeholder, TimerDisplayState.PlaceholderText);
        }

        private static void AssertRoundTimer(
            GameTimerState state,
            TimerDisplayKind expectedKind,
            string expectedText)
        {
            TimerDisplayState? timer = state.DisplayState.RoundTime;
            Assert.NotNull(timer);
            TimerDisplayState actualTimer = timer!;
            Assert.Equal(expectedKind, actualTimer.Kind);
            Assert.Equal(expectedText, Render(actualTimer));
        }

        private static string Render(TimerDisplayState timerDisplayState)
        {
            return Assert.IsType<DisplayText.PlainText>(timerDisplayState.Text).Text;
        }
    }
}
