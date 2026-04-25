using BO2.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
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
                PlayerStatsReadResult result = await Task.Run(_memoryReader.ReadPlayerStats, cancellationToken);
                await RunOnDispatcherAsync(() => ApplyReadResult(result), cancellationToken);
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

        private void ApplyReadError(string message)
        {
            ClearStats();
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
