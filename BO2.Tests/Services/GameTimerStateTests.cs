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

        private static GameTimerLifecycleEventBatch CreateLifecycleBatch(
            params GameTimerLifecycleEvent[] events)
        {
            return new GameTimerLifecycleEventBatch(events, HasSequenceGap: false);
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

        private static GameTimingReadResult CreateTimingRead(
            int gameTimeMilliseconds,
            bool isPaused = false)
        {
            return GameTimingReadResult.SupportedTiming(
                CreateDetectedGame(),
                TimeSpan.FromMilliseconds(gameTimeMilliseconds),
                isPaused);
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
