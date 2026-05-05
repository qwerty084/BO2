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
        private bool _isAwaitingRoundTimeBaseline;
        private TimeSpan? _latestGameTime;
        private bool _latestGameTimeIsPaused;
        private TimeSpan? _gameTimeBaseline;
        private TimeSpan? _currentGameTime;
        private bool _currentGameTimeIsPaused;
        private TimeSpan? _roundTimeBaseline;
        private TimeSpan? _currentRoundGameTime;
        private bool _currentRoundGameTimeIsPaused;
        private bool _isRoundActive;

        public GameConnectionTimerDisplayState DisplayState => new(
            CreateGameTimerDisplayState(),
            CreateRoundTimerDisplayState());

        internal bool HasUntrustedLifecycleSequence => _hasUntrustedLifecycleSequence;

        public void ApplyLifecycleEvents(GameTimerLifecycleEventBatch lifecycleEvents)
        {
            ArgumentNullException.ThrowIfNull(lifecycleEvents);

            if (lifecycleEvents.HasSequenceGap)
            {
                _hasUntrustedLifecycleSequence = true;
                ClearTimers();
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
                    case GameEventType.StartOfRound when IsValidRound(lifecycleEvent.LevelTime):
                        ObserveRoundStart();
                        if (lifecycleEvent.LevelTime == 1)
                        {
                            ObserveRoundOneStart();
                        }

                        break;
                    case GameEventType.EndOfRound when IsValidRound(lifecycleEvent.LevelTime):
                        ObserveRoundEnd();
                        break;
                    case GameEventType.EndGame:
                        ClearTimers();
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
            }

            if (_isAwaitingRoundTimeBaseline)
            {
                CaptureRoundTimeBaseline();
            }

            if (_gameTimeBaseline is not null)
            {
                _currentGameTime = gameTime;
                _currentGameTimeIsPaused = _latestGameTimeIsPaused;
            }

            if (_roundTimeBaseline is not null && _isRoundActive)
            {
                _currentRoundGameTime = gameTime;
                _currentRoundGameTimeIsPaused = _latestGameTimeIsPaused;
            }
        }

        public void Reset()
        {
            _hasUntrustedLifecycleSequence = false;
            _hasObservedRoundOneStart = false;
            _latestGameTime = null;
            _latestGameTimeIsPaused = false;
            ClearTimers();
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

        private TimerDisplayState CreateRoundTimerDisplayState()
        {
            if (_roundTimeBaseline is not TimeSpan baseline
                || _currentRoundGameTime is not TimeSpan currentGameTime)
            {
                return TimerDisplayState.Placeholder;
            }

            TimeSpan elapsed = currentGameTime - baseline;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return !_isRoundActive || _currentRoundGameTimeIsPaused
                ? TimerDisplayState.Frozen(elapsed)
                : TimerDisplayState.Active(elapsed);
        }

        private void ObserveRoundStart()
        {
            _isRoundActive = true;
            _isAwaitingRoundTimeBaseline = true;
            _roundTimeBaseline = null;
            _currentRoundGameTime = null;
            _currentRoundGameTimeIsPaused = false;
            CaptureRoundTimeBaseline();
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

        private void CaptureRoundTimeBaseline()
        {
            if (!_isRoundActive
                || _latestGameTime is not TimeSpan latestGameTime)
            {
                return;
            }

            _roundTimeBaseline = latestGameTime;
            _currentRoundGameTime = latestGameTime;
            _currentRoundGameTimeIsPaused = _latestGameTimeIsPaused;
            _isAwaitingRoundTimeBaseline = false;
        }

        private void ObserveRoundEnd()
        {
            _isRoundActive = false;
            _isAwaitingRoundTimeBaseline = false;
        }

        private void ClearTimers()
        {
            _isAwaitingGameTimeBaseline = false;
            _isAwaitingRoundTimeBaseline = false;
            _latestGameTime = null;
            _latestGameTimeIsPaused = false;
            _gameTimeBaseline = null;
            _currentGameTime = null;
            _currentGameTimeIsPaused = false;
            _roundTimeBaseline = null;
            _currentRoundGameTime = null;
            _currentRoundGameTimeIsPaused = false;
            _isRoundActive = false;
        }

        private static bool IsValidRound(int round)
        {
            return round > 0;
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
