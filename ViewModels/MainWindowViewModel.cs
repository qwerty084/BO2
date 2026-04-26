using BO2.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BO2.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string EmptyStatText = "--";

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly GameMemoryReader _memoryReader = new();
        private readonly DllInjector _dllInjector = new();
        private readonly IGameEventMonitor _eventMonitor = new GameEventMonitor();
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private string _pointsText = EmptyStatText;
        private string _killsText = EmptyStatText;
        private string _downsText = EmptyStatText;
        private string _revivesText = EmptyStatText;
        private string _headshotsText = EmptyStatText;
        private string _positionXText = EmptyStatText;
        private string _positionYText = EmptyStatText;
        private string _positionZText = EmptyStatText;
        private string _playerCandidateDetailsText = EmptyStatText;
        private string _ammoCandidateDetailsText = EmptyStatText;
        private string _counterCandidateDetailsText = EmptyStatText;
        private string _addressCandidateDetailsText = EmptyStatText;
        private string _detectedGameText = AppStrings.Get("NoGameDetected");
        private string _eventCompatibilityText = AppStrings.Get("NoGameDetected");
        private string _injectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
        private string _eventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
        private string _currentRoundText = EmptyStatText;
        private string _boxEventsText = AppStrings.Get("RecentEventsEmpty");
        private string _recentGameEventsText = AppStrings.Get("RecentEventsEmpty");
        private string _statusText = AppStrings.Get("GameNotRunning");
        private Visibility _connectedStatusVisibility = Visibility.Collapsed;
        private Visibility _unsupportedStatusVisibility = Visibility.Collapsed;
        private Visibility _disconnectedStatusVisibility = Visibility.Visible;
        private readonly StatFormatter _formatter;

        public MainWindowViewModel(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
            _formatter = new StatFormatter(AppStrings.Get("UnavailableValue"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string PointsText
        {
            get => _pointsText;
            private set => SetProperty(ref _pointsText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string DetectedGameText
        {
            get => _detectedGameText;
            private set => SetProperty(ref _detectedGameText, value);
        }

        public string EventCompatibilityText
        {
            get => _eventCompatibilityText;
            private set => SetProperty(ref _eventCompatibilityText, value);
        }

        public string InjectionStatusText
        {
            get => _injectionStatusText;
            private set => SetProperty(ref _injectionStatusText, value);
        }

        public string EventMonitorStatusText
        {
            get => _eventMonitorStatusText;
            private set => SetProperty(ref _eventMonitorStatusText, value);
        }

        public string RecentGameEventsText
        {
            get => _recentGameEventsText;
            private set => SetProperty(ref _recentGameEventsText, value);
        }

        public string CurrentRoundText
        {
            get => _currentRoundText;
            private set => SetProperty(ref _currentRoundText, value);
        }

        public string BoxEventsText
        {
            get => _boxEventsText;
            private set => SetProperty(ref _boxEventsText, value);
        }

        public Visibility ConnectedStatusVisibility
        {
            get => _connectedStatusVisibility;
            private set => SetProperty(ref _connectedStatusVisibility, value);
        }

        public Visibility UnsupportedStatusVisibility
        {
            get => _unsupportedStatusVisibility;
            private set => SetProperty(ref _unsupportedStatusVisibility, value);
        }

        public Visibility DisconnectedStatusVisibility
        {
            get => _disconnectedStatusVisibility;
            private set => SetProperty(ref _disconnectedStatusVisibility, value);
        }

        public string KillsText
        {
            get => _killsText;
            private set => SetProperty(ref _killsText, value);
        }

        public string DownsText
        {
            get => _downsText;
            private set => SetProperty(ref _downsText, value);
        }

        public string RevivesText
        {
            get => _revivesText;
            private set => SetProperty(ref _revivesText, value);
        }

        public string HeadshotsText
        {
            get => _headshotsText;
            private set => SetProperty(ref _headshotsText, value);
        }

        public string PositionXText
        {
            get => _positionXText;
            private set => SetProperty(ref _positionXText, value);
        }

        public string PositionYText
        {
            get => _positionYText;
            private set => SetProperty(ref _positionYText, value);
        }

        public string PositionZText
        {
            get => _positionZText;
            private set => SetProperty(ref _positionZText, value);
        }

        public string PlayerCandidateDetailsText
        {
            get => _playerCandidateDetailsText;
            private set => SetProperty(ref _playerCandidateDetailsText, value);
        }

        public string AmmoCandidateDetailsText
        {
            get => _ammoCandidateDetailsText;
            private set => SetProperty(ref _ammoCandidateDetailsText, value);
        }

        public string CounterCandidateDetailsText
        {
            get => _counterCandidateDetailsText;
            private set => SetProperty(ref _counterCandidateDetailsText, value);
        }

        public string AddressCandidateDetailsText
        {
            get => _addressCandidateDetailsText;
            private set => SetProperty(ref _addressCandidateDetailsText, value);
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                (
                    PlayerStatsReadResult readResult,
                    DllInjectionResult injectionResult,
                    GameEventMonitorStatus eventStatus) = await Task.Run(
                    () =>
                    {
                        PlayerStatsReadResult readResult = _memoryReader.ReadPlayerStats();
                        DllInjectionResult injectionResult = EnsureMonitorInjected(readResult.DetectedGame);
                        GameEventMonitorStatus eventStatus = _eventMonitor.ReadStatus(
                            DateTimeOffset.UtcNow,
                            readResult.DetectedGame?.ProcessId);
                        return (readResult, injectionResult, eventStatus);
                    },
                    cancellationToken);
                await RunOnDispatcherAsync(
                    () =>
                    {
                        ApplyReadResult(readResult);
                        ApplyEventMonitorStatus(readResult, injectionResult, eventStatus);
                    },
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
        }

        public Task TryApplyRefreshErrorAsync(string message, CancellationToken cancellationToken)
        {
            return TryApplyReadErrorAsync(message, cancellationToken);
        }

        public void Dispose()
        {
            _eventMonitor.Dispose();
            _memoryReader.Dispose();
        }

        private static string FormatLine(string labelResourceId, string value)
        {
            return AppStrings.Format("LabeledValueFormat", AppStrings.Get(labelResourceId), value);
        }

        private static async Task RunOnDispatcherAsync(DispatcherQueue dispatcherQueue, Action action, CancellationToken cancellationToken)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                return;
            }

            TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
                () => completionSource.TrySetCanceled(cancellationToken));

            bool queued = dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    action();
                    completionSource.TrySetResult();
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            });

            if (!queued)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw new InvalidOperationException(AppStrings.Get("DispatcherQueueFailed"));
            }

            await completionSource.Task;
        }

        private Task RunOnDispatcherAsync(Action action, CancellationToken cancellationToken)
        {
            return RunOnDispatcherAsync(_dispatcherQueue, action, cancellationToken);
        }

        private async Task TryApplyReadErrorAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                await RunOnDispatcherAsync(() => ApplyReadError(message), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void ApplyReadResult(PlayerStatsReadResult result)
        {
            StatusText = result.StatusText;
            DetectedGameText = result.DetectedGame?.DisplayName ?? AppStrings.Get("NoGameDetected");
            SetConnectionState(result.ConnectionState);

            if (result.Stats is null)
            {
                ClearStats();
                return;
            }

            PointsText = _formatter.FormatStat(result.Stats.Points);
            KillsText = _formatter.FormatStat(result.Stats.Kills);
            DownsText = _formatter.FormatStat(result.Stats.Downs);
            RevivesText = _formatter.FormatStat(result.Stats.Revives);
            HeadshotsText = _formatter.FormatStat(result.Stats.Headshots);
            PositionXText = _formatter.FormatCandidate(result.Stats.Candidates.PositionX);
            PositionYText = _formatter.FormatCandidate(result.Stats.Candidates.PositionY);
            PositionZText = _formatter.FormatCandidate(result.Stats.Candidates.PositionZ);
            PlayerCandidateDetailsText = FormatPlayerCandidateDetails(result.Stats.Candidates);
            AmmoCandidateDetailsText = FormatAmmoCandidateDetails(result.Stats.Candidates);
            CounterCandidateDetailsText = FormatCounterCandidateDetails(result.Stats.Candidates);
            AddressCandidateDetailsText = result.DetectedGame?.AddressMap is PlayerStatAddressMap addressMap
                ? FormatAddressCandidateDetails(addressMap)
                : EmptyStatText;
        }

        private string FormatPlayerCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("VelocityXLabel", _formatter.FormatCandidate(candidates.VelocityX)),
                FormatLine("VelocityYLabel", _formatter.FormatCandidate(candidates.VelocityY)),
                FormatLine("VelocityZLabel", _formatter.FormatCandidate(candidates.VelocityZ)),
                FormatLine("GravityFieldLabel", _formatter.FormatCandidate(candidates.Gravity)),
                FormatLine("SpeedFieldLabel", _formatter.FormatCandidate(candidates.Speed)),
                FormatLine("LastJumpHeightLabel", _formatter.FormatCandidate(candidates.LastJumpHeight)),
                FormatLine("AdsAmountLabel", _formatter.FormatCandidate(candidates.AdsAmount)),
                FormatLine("ViewAngleXLabel", _formatter.FormatCandidate(candidates.ViewAngleX)),
                FormatLine("ViewAngleYLabel", _formatter.FormatCandidate(candidates.ViewAngleY)),
                FormatLine("HeightIntLabel", _formatter.FormatCandidate(candidates.HeightInt)),
                FormatLine("HeightFloatLabel", _formatter.FormatCandidate(candidates.HeightFloat)),
                FormatLine("LegacyHealthLabel", _formatter.FormatCandidate(candidates.LegacyHealth)),
                FormatLine("PlayerInfoHealthLabel", _formatter.FormatCandidate(candidates.PlayerInfoHealth)),
                FormatLine("GEntityPlayerHealthLabel", _formatter.FormatCandidate(candidates.GEntityPlayerHealth))
            ]);
        }

        private string FormatAmmoCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("AmmoSlot0Label", _formatter.FormatCandidate(candidates.AmmoSlot0)),
                FormatLine("AmmoSlot1Label", _formatter.FormatCandidate(candidates.AmmoSlot1)),
                FormatLine("LethalAmmoLabel", _formatter.FormatCandidate(candidates.LethalAmmo)),
                FormatLine("AmmoSlot2Label", _formatter.FormatCandidate(candidates.AmmoSlot2)),
                FormatLine("TacticalAmmoLabel", _formatter.FormatCandidate(candidates.TacticalAmmo)),
                FormatLine("AmmoSlot3Label", _formatter.FormatCandidate(candidates.AmmoSlot3)),
                FormatLine("AmmoSlot4Label", _formatter.FormatCandidate(candidates.AmmoSlot4))
            ]);
        }

        private string FormatCounterCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("RoundCandidateLabel", _formatter.FormatCandidate(candidates.Round)),
                FormatLine("AlternateKillsLabel", _formatter.FormatCandidate(candidates.AlternateKills)),
                FormatLine("AlternateHeadshotsLabel", _formatter.FormatCandidate(candidates.AlternateHeadshots)),
                FormatLine("SecondaryKillsLabel", _formatter.FormatCandidate(candidates.SecondaryKills)),
                FormatLine("SecondaryHeadshotsLabel", _formatter.FormatCandidate(candidates.SecondaryHeadshots))
            ]);
        }

        private static string FormatAddressCandidateDetails(PlayerStatAddressMap addressMap)
        {
            DerivedPlayerStateAddresses derivedPlayerState = addressMap.DerivedPlayerState;
            PlayerCandidateAddresses candidates = addressMap.Candidates;
            return string.Join(Environment.NewLine,
            [
                FormatLine("LocalPlayerBaseLabel", StatFormatter.FormatAddress(derivedPlayerState.LocalPlayerBaseAddress)),
                FormatLine("GEntityArrayLabel", StatFormatter.FormatAddress(candidates.GEntityArrayAddress)),
                FormatLine("Zombie0GEntityLabel", StatFormatter.FormatAddress(candidates.Zombie0GEntityAddress)),
                FormatLine("GEntitySizeLabel", StatFormatter.FormatAddress(candidates.GEntitySize))
            ]);
        }

        private static string FormatEventCompatibility(GameCompatibilityState compatibilityState)
        {
            return compatibilityState switch
            {
                GameCompatibilityState.WaitingForMonitor => AppStrings.Get("EventMonitorWaitingForMonitor"),
                GameCompatibilityState.Compatible => AppStrings.Get("EventMonitorCompatible"),
                GameCompatibilityState.UnsupportedVersion => AppStrings.Get("EventMonitorUnsupportedVersion"),
                GameCompatibilityState.CaptureDisabled => AppStrings.Get("EventMonitorCaptureDisabled"),
                GameCompatibilityState.PollingFallback => AppStrings.Get("EventMonitorPollingFallback"),
                _ => AppStrings.Get("EventMonitorUnknown")
            };
        }

        private static string FormatRecentGameEvents(GameEventMonitorStatus eventStatus)
        {
            if (eventStatus.RecentEvents.Count == 0)
            {
                return AppStrings.Get("RecentEventsEmpty");
            }

            return string.Join(
                Environment.NewLine,
                eventStatus.RecentEvents
                    .TakeLast(6)
                    .Select(gameEvent => AppStrings.Format(
                        "RecentEventFormat",
                        gameEvent.ReceivedAt.ToLocalTime().ToString("HH:mm:ss"),
                        gameEvent.EventType,
                        gameEvent.EventName,
                        gameEvent.LevelTime,
                        gameEvent.OwnerId,
                        gameEvent.StringValue)));
        }

        private static string FormatRoundSession(GameEventMonitorStatus eventStatus)
        {
            GameEvent? sessionEvent = eventStatus.RecentEvents
                .LastOrDefault(gameEvent => gameEvent.EventType is GameEventType.StartOfRound or GameEventType.EndOfRound or GameEventType.EndGame);
            if (sessionEvent is null)
            {
                return EmptyStatText;
            }

            if (sessionEvent.EventType == GameEventType.EndGame)
            {
                return AppStrings.Get("RoundSessionEnded");
            }

            if (sessionEvent.LevelTime <= 0)
            {
                return EmptyStatText;
            }

            return AppStrings.Format("CurrentRoundFormat", sessionEvent.LevelTime, sessionEvent.EventName);
        }

        private static string FormatRecentBoxEvents(GameEventMonitorStatus eventStatus)
        {
            GameEvent[] boxEvents = eventStatus.RecentEvents
                .Where(gameEvent => gameEvent.EventType == GameEventType.BoxEvent)
                .TakeLast(6)
                .ToArray();
            if (boxEvents.Length == 0)
            {
                return AppStrings.Get("RecentEventsEmpty");
            }

            return string.Join(
                Environment.NewLine,
                boxEvents.Select(gameEvent => AppStrings.Format(
                    "BoxEventFormat",
                    gameEvent.ReceivedAt.ToLocalTime().ToString("HH:mm:ss"),
                    gameEvent.EventName,
                    gameEvent.OwnerId,
                    gameEvent.StringValue)));
        }

        private DllInjectionResult EnsureMonitorInjected(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                _lastInjectionProcessId = null;
                _lastInjectionResult = DllInjectionResult.NotAttempted;
                return _lastInjectionResult;
            }

            if (_lastInjectionProcessId == detectedGame.ProcessId && IsCachedInjectionResult(_lastInjectionResult.State))
            {
                return _lastInjectionResult;
            }

            _lastInjectionProcessId = detectedGame.ProcessId;
            _lastInjectionResult = _dllInjector.Inject(detectedGame);
            return _lastInjectionResult;
        }

        private static bool IsCachedInjectionResult(DllInjectionState state)
        {
            return state is DllInjectionState.UnsupportedGame
                or DllInjectionState.WrongProcessArchitecture
                or DllInjectionState.AlreadyInjected
                or DllInjectionState.Loaded
                or DllInjectionState.MonitorReady
                or DllInjectionState.PollingFallback
                or DllInjectionState.UnsupportedVersion;
        }

        private static string FormatInjectionStatus(
            DllInjectionResult injectionResult,
            GameEventMonitorStatus eventStatus)
        {
            if (injectionResult.State is not (DllInjectionState.Loaded or DllInjectionState.AlreadyInjected))
            {
                return injectionResult.Message;
            }

            return eventStatus.CompatibilityState switch
            {
                GameCompatibilityState.Compatible => AppStrings.Get("DllInjectionMonitorReady"),
                GameCompatibilityState.PollingFallback => AppStrings.Get("DllInjectionPollingFallback"),
                GameCompatibilityState.UnsupportedVersion => AppStrings.Get("DllInjectionUnsupportedVersion"),
                GameCompatibilityState.CaptureDisabled => AppStrings.Get("DllInjectionCaptureDisabled"),
                GameCompatibilityState.WaitingForMonitor => AppStrings.Get("DllInjectionWaitingForReadiness"),
                _ => injectionResult.Message
            };
        }

        private void ApplyEventMonitorStatus(
            PlayerStatsReadResult readResult,
            DllInjectionResult injectionResult,
            GameEventMonitorStatus eventStatus)
        {
            InjectionStatusText = FormatInjectionStatus(injectionResult, eventStatus);

            if (readResult.DetectedGame is null)
            {
                EventCompatibilityText = AppStrings.Get("NoGameDetected");
                EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
                CurrentRoundText = EmptyStatText;
                BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            if (readResult.DetectedGame.Variant != GameVariant.SteamZombies || readResult.DetectedGame.AddressMap is null)
            {
                EventCompatibilityText = AppStrings.Format(
                    "EventMonitorUnsupportedGameFormat",
                    readResult.DetectedGame.DisplayName);
                EventMonitorStatusText = AppStrings.Get("EventMonitorCaptureDisabled");
                CurrentRoundText = EmptyStatText;
                BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            string monitorStatusText = FormatEventCompatibility(eventStatus.CompatibilityState);
            if (eventStatus.DroppedEventCount > 0 || eventStatus.DroppedNotifyCount > 0)
            {
                monitorStatusText = AppStrings.Format(
                    "EventMonitorCaptureDropsFormat",
                    monitorStatusText,
                    eventStatus.DroppedEventCount,
                    eventStatus.DroppedNotifyCount,
                    eventStatus.PublishedNotifyCount);
            }
            else if (eventStatus.PublishedNotifyCount > 0)
            {
                monitorStatusText = AppStrings.Format(
                    "EventMonitorPublishedEventsFormat",
                    monitorStatusText,
                    eventStatus.PublishedNotifyCount);
            }

            EventCompatibilityText = AppStrings.Get("GameProcessDetectorDisplayNameSteamZombies");
            EventMonitorStatusText = monitorStatusText;
            CurrentRoundText = FormatRoundSession(eventStatus);
            BoxEventsText = FormatRecentBoxEvents(eventStatus);
            RecentGameEventsText = FormatRecentGameEvents(eventStatus);
        }

        private void ApplyReadError(string message)
        {
            ClearStats();
            EventCompatibilityText = AppStrings.Get("NoGameDetected");
            InjectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
            EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
            CurrentRoundText = EmptyStatText;
            BoxEventsText = AppStrings.Get("RecentEventsEmpty");
            RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
            StatusText = message;
            SetConnectionState(ConnectionState.Disconnected);
        }

        private void SetConnectionState(ConnectionState connectionState)
        {
            ConnectedStatusVisibility = connectionState == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
            UnsupportedStatusVisibility = connectionState == ConnectionState.Unsupported ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedStatusVisibility = connectionState == ConnectionState.Disconnected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearStats()
        {
            PointsText = EmptyStatText;
            KillsText = EmptyStatText;
            DownsText = EmptyStatText;
            RevivesText = EmptyStatText;
            HeadshotsText = EmptyStatText;
            PositionXText = EmptyStatText;
            PositionYText = EmptyStatText;
            PositionZText = EmptyStatText;
            PlayerCandidateDetailsText = EmptyStatText;
            AmmoCandidateDetailsText = EmptyStatText;
            CounterCandidateDetailsText = EmptyStatText;
            AddressCandidateDetailsText = EmptyStatText;
        }

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
