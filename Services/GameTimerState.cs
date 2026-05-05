using System;
using System.Collections.Generic;
using System.Linq;

namespace BO2.Services
{
    internal interface IGameTimerState
    {
        GameConnectionTimerDisplayState DisplayState { get; }

        void ApplyLifecycleEvents(GameTimerLifecycleEventBatch lifecycleEvents);

        void ApplyTimingRead(GameTimingReadResult timingRead);

        void Reset();
    }

    internal sealed class GameTimerState : IGameTimerState
    {
        private bool _hasUntrustedLifecycleSequence;
        private bool _hasObservedRoundOneStart;
        private bool _isAwaitingGameTimeBaseline;
        private TimeSpan? _latestGameTime;
        private bool _latestGameTimeIsPaused;
        private TimeSpan? _gameTimeBaseline;
        private TimeSpan? _currentGameTime;
        private bool _currentGameTimeIsPaused;

        public GameConnectionTimerDisplayState DisplayState => new(
            CreateGameTimerDisplayState(),
            TimerDisplayState.Placeholder);

        internal bool HasUntrustedLifecycleSequence => _hasUntrustedLifecycleSequence;

        public void ApplyLifecycleEvents(GameTimerLifecycleEventBatch lifecycleEvents)
        {
            ArgumentNullException.ThrowIfNull(lifecycleEvents);

            if (lifecycleEvents.HasSequenceGap)
            {
                _hasUntrustedLifecycleSequence = true;
                ClearGameTimer();
                return;
            }

            if (_hasUntrustedLifecycleSequence)
            {
                return;
            }

            foreach (GameTimerLifecycleEvent lifecycleEvent in lifecycleEvents.Events)
            {
                switch (lifecycleEvent.EventType)
                {
                    case GameEventType.StartOfRound when lifecycleEvent.LevelTime == 1:
                        ObserveRoundOneStart();
                        break;
                    case GameEventType.EndGame:
                        ClearGameTimer();
                        break;
                }
            }
        }

        public void ApplyTimingRead(GameTimingReadResult timingRead)
        {
            ArgumentNullException.ThrowIfNull(timingRead);

            if (timingRead.Status != GameTimingReadStatus.SupportedTiming
                || timingRead.GameTime is not TimeSpan gameTime)
            {
                return;
            }

            _latestGameTime = gameTime;
            _latestGameTimeIsPaused = timingRead.IsPaused == true;

            if (_isAwaitingGameTimeBaseline)
            {
                CaptureGameTimeBaseline();
                return;
            }

            if (_gameTimeBaseline is not null)
            {
                _currentGameTime = gameTime;
                _currentGameTimeIsPaused = _latestGameTimeIsPaused;
            }
        }

        public void Reset()
        {
            _hasUntrustedLifecycleSequence = false;
            _hasObservedRoundOneStart = false;
            _latestGameTime = null;
            _latestGameTimeIsPaused = false;
            ClearGameTimer();
        }

        private TimerDisplayState CreateGameTimerDisplayState()
        {
            if (_gameTimeBaseline is not TimeSpan baseline
                || _currentGameTime is not TimeSpan currentGameTime)
            {
                return TimerDisplayState.Placeholder;
            }

            TimeSpan elapsed = currentGameTime - baseline;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return _currentGameTimeIsPaused
                ? TimerDisplayState.Frozen(elapsed)
                : TimerDisplayState.Active(elapsed);
        }

        private void ObserveRoundOneStart()
        {
            _hasObservedRoundOneStart = true;
            _isAwaitingGameTimeBaseline = true;
            CaptureGameTimeBaseline();
        }

        private void CaptureGameTimeBaseline()
        {
            if (!_hasObservedRoundOneStart
                || _latestGameTime is not TimeSpan latestGameTime)
            {
                return;
            }

            _gameTimeBaseline = latestGameTime;
            _currentGameTime = latestGameTime;
            _currentGameTimeIsPaused = _latestGameTimeIsPaused;
            _isAwaitingGameTimeBaseline = false;
        }

        private void ClearGameTimer()
        {
            _isAwaitingGameTimeBaseline = false;
            _latestGameTime = null;
            _latestGameTimeIsPaused = false;
            _gameTimeBaseline = null;
            _currentGameTime = null;
            _currentGameTimeIsPaused = false;
        }
    }

    internal sealed class GameLifecycleEventSequencer
    {
        private ulong? _lastObservedEventSequence;

        public ulong? LastProcessedLifecycleEventSequence { get; private set; }

        public GameTimerLifecycleEventBatch ReadNewLifecycleEvents(GameEventMonitorStatus eventStatus)
        {
            ArgumentNullException.ThrowIfNull(eventStatus);

            GameEvent[] sequencedEvents = [.. eventStatus.RecentEvents
                .Where(static gameEvent => gameEvent.Sequence > 0)
                .OrderBy(static gameEvent => gameEvent.Sequence)];
            if (sequencedEvents.Length == 0)
            {
                return GameTimerLifecycleEventBatch.Empty;
            }

            ulong oldestVisibleSequence = sequencedEvents[0].Sequence;
            ulong newestVisibleSequence = sequencedEvents[^1].Sequence;
            ulong nextExpectedSequence = _lastObservedEventSequence is ulong previousObservedSequence
                ? previousObservedSequence + 1UL
                : 1UL;
            bool hasSequenceGap = oldestVisibleSequence > nextExpectedSequence;
            var lifecycleEvents = new List<GameTimerLifecycleEvent>();

            foreach (GameEvent gameEvent in sequencedEvents)
            {
                if (_lastObservedEventSequence is ulong lastObservedSequence
                    && gameEvent.Sequence <= lastObservedSequence)
                {
                    continue;
                }

                if (IsLifecycleEvent(gameEvent.EventType))
                {
                    lifecycleEvents.Add(GameTimerLifecycleEvent.FromGameEvent(gameEvent));
                }
            }

            _lastObservedEventSequence = _lastObservedEventSequence is ulong observedSequence
                ? Math.Max(observedSequence, newestVisibleSequence)
                : newestVisibleSequence;

            if (lifecycleEvents.Count > 0)
            {
                LastProcessedLifecycleEventSequence = lifecycleEvents[^1].Sequence;
            }

            return lifecycleEvents.Count > 0 || hasSequenceGap
                ? new GameTimerLifecycleEventBatch(lifecycleEvents, hasSequenceGap)
                : GameTimerLifecycleEventBatch.Empty;
        }

        public void Reset()
        {
            _lastObservedEventSequence = null;
            LastProcessedLifecycleEventSequence = null;
        }

        private static bool IsLifecycleEvent(GameEventType eventType)
        {
            return eventType is GameEventType.StartOfRound
                or GameEventType.EndOfRound
                or GameEventType.EndGame;
        }
    }

    internal sealed record GameTimerLifecycleEvent(
        ulong Sequence,
        GameEventType EventType,
        int LevelTime,
        DateTimeOffset ReceivedAt)
    {
        public static GameTimerLifecycleEvent FromGameEvent(GameEvent gameEvent)
        {
            ArgumentNullException.ThrowIfNull(gameEvent);

            return new GameTimerLifecycleEvent(
                gameEvent.Sequence,
                gameEvent.EventType,
                gameEvent.LevelTime,
                gameEvent.ReceivedAt);
        }
    }

    internal sealed record GameTimerLifecycleEventBatch(
        IReadOnlyList<GameTimerLifecycleEvent> Events,
        bool HasSequenceGap)
    {
        public static GameTimerLifecycleEventBatch Empty { get; } = new(
            Array.Empty<GameTimerLifecycleEvent>(),
            HasSequenceGap: false);
    }
}
