using System;
using System.Linq;

namespace BO2.Services
{
    internal sealed class GameConnectionSessionDisplayProjector
    {
        private readonly StatFormatter _formatter;

        public GameConnectionSessionDisplayProjector(string unavailableText)
        {
            _formatter = new StatFormatter(unavailableText);
        }

        public GameConnectionSessionDisplayState Project(GameConnectionRefreshResult snapshot)
        {
            var state = GameConnectionSessionDisplayState.CreateDefault();
            bool readResultIsForCurrentGame = Equals(snapshot.ReadResult.DetectedGame, snapshot.CurrentGame);
            ApplyReadResult(
                state,
                snapshot.ReadResult,
                snapshot.IsConnecting,
                snapshot.IsDisconnecting,
                readResultIsForCurrentGame && snapshot.IsMonitorConnectedForCurrentGame);
            ApplyEventMonitorStatus(
                state,
                snapshot.CurrentGame,
                snapshot.InjectionResult,
                snapshot.EventStatus,
                snapshot.IsConnecting,
                snapshot.IsDisconnecting,
                snapshot.HasInjectionAttemptForCurrentGame,
                snapshot.IsMonitorConnectedForCurrentGame);
            UpdateConnectButtonState(state, snapshot);
            return state;
        }

        private static string FormatLine(string labelResourceId, string value)
        {
            return AppStrings.Format("LabeledValueFormat", AppStrings.Get(labelResourceId), value);
        }

        private void ApplyReadResult(
            GameConnectionSessionDisplayState state,
            PlayerStatsReadResult result,
            bool isConnecting,
            bool isDisconnecting,
            bool isMonitorConnectedForDetectedGame)
        {
            state.DetectedGameText = result.DetectedGame?.DisplayName ?? AppStrings.Get("NoGameDetected");
            ApplyConnectionStatus(
                state,
                result.DetectedGame,
                result.StatusText,
                isConnecting,
                isDisconnecting,
                isMonitorConnectedForDetectedGame);

            if (result.Stats is null)
            {
                ClearStats(state);
                return;
            }

            state.PointsText = _formatter.FormatStat(result.Stats.Points);
            state.KillsText = _formatter.FormatStat(result.Stats.Kills);
            state.DownsText = _formatter.FormatStat(result.Stats.Downs);
            state.RevivesText = _formatter.FormatStat(result.Stats.Revives);
            state.HeadshotsText = _formatter.FormatStat(result.Stats.Headshots);
            state.PositionXText = _formatter.FormatCandidate(result.Stats.Candidates.PositionX);
            state.PositionYText = _formatter.FormatCandidate(result.Stats.Candidates.PositionY);
            state.PositionZText = _formatter.FormatCandidate(result.Stats.Candidates.PositionZ);
            state.PlayerCandidateDetailsText = FormatPlayerCandidateDetails(result.Stats.Candidates);
            state.AmmoCandidateDetailsText = FormatAmmoCandidateDetails(result.Stats.Candidates);
            state.CounterCandidateDetailsText = FormatCounterCandidateDetails(result.Stats.Candidates);
            state.AddressCandidateDetailsText = result.DetectedGame?.AddressMap is PlayerStatAddressMap addressMap
                ? FormatAddressCandidateDetails(addressMap)
                : GameConnectionSessionDisplayState.EmptyStatText;
        }

        private static void ApplyConnectionStatus(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            string? connectedStatusText,
            bool isConnecting,
            bool isDisconnecting,
            bool isMonitorConnectedForDetectedGame)
        {
            if (detectedGame is null)
            {
                state.StatusText = AppStrings.Get("GameNotRunning");
                SetConnectionState(state, detectedGame, ConnectionState.Disconnected, isConnecting, isDisconnecting);
                return;
            }

            if (!detectedGame.IsStatsSupported)
            {
                state.StatusText = FormatUnsupportedStatus(detectedGame);
                SetConnectionState(state, detectedGame, ConnectionState.Unsupported, isConnecting, isDisconnecting);
                return;
            }

            if (isDisconnecting)
            {
                state.StatusText = AppStrings.Get("ConnectionStatusDisconnecting");
                SetConnectionState(state, detectedGame, ConnectionState.Disconnecting, isConnecting, isDisconnecting);
                return;
            }

            if (isConnecting)
            {
                state.StatusText = AppStrings.Get("ConnectionStatusConnecting");
                SetConnectionState(state, detectedGame, ConnectionState.Detected, isConnecting, isDisconnecting);
                return;
            }

            if (isMonitorConnectedForDetectedGame)
            {
                state.StatusText = connectedStatusText ?? AppStrings.Format("ConnectedStatusFormat", detectedGame.DisplayName);
                SetConnectionState(state, detectedGame, ConnectionState.Connected, isConnecting, isDisconnecting);
                return;
            }

            state.StatusText = AppStrings.Format("GameDetectedConnectPromptFormat", detectedGame.DisplayName);
            SetConnectionState(state, detectedGame, ConnectionState.Detected, isConnecting, isDisconnecting);
        }

        private static string FormatUnsupportedStatus(DetectedGame detectedGame)
        {
            return string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                ? AppStrings.Format("UnsupportedStatusFormat", detectedGame.DisplayName)
                : AppStrings.Format("UnsupportedStatusWithReasonFormat", detectedGame.DisplayName, detectedGame.UnsupportedReason);
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

        private static string FormatRoundSession(GameEventMonitorStatus eventStatus)
        {
            GameEvent? sessionEvent = eventStatus.RecentEvents
                .LastOrDefault(gameEvent => gameEvent.EventType is GameEventType.StartOfRound or GameEventType.EndOfRound or GameEventType.EndGame);
            if (sessionEvent is null)
            {
                return GameConnectionSessionDisplayState.EmptyStatText;
            }

            if (sessionEvent.EventType == GameEventType.EndGame)
            {
                return AppStrings.Get("RoundSessionEnded");
            }

            if (sessionEvent.LevelTime <= 0)
            {
                return GameConnectionSessionDisplayState.EmptyStatText;
            }

            return AppStrings.Format("CurrentRoundFormat", sessionEvent.LevelTime, sessionEvent.EventName);
        }

        private static void ApplyDisconnectingState(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            bool isMonitorConnectedForDetectedGame)
        {
            state.StatusText = AppStrings.Get("ConnectionStatusDisconnecting");
            state.InjectionStatusText = AppStrings.Get("DllInjectionDisconnecting");
            state.EventMonitorStatusText = AppStrings.Get("EventMonitorDisconnecting");
            state.LatestEventStatus = GameEventMonitorStatus.WaitingForMonitor;
            state.ConnectionLastUpdateText = GameConnectionSessionDisplayState.EmptyStatText;
            state.CurrentRoundText = GameConnectionSessionDisplayState.EmptyStatText;
            state.BoxEventsText = AppStrings.Get("RecentEventsEmpty");
            state.RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
            SetConnectionState(
                state,
                detectedGame,
                ConnectionState.Disconnecting,
                isConnecting: false,
                isDisconnecting: true);
            UpdateConnectButtonState(
                state,
                detectedGame,
                canAttemptConnect: false,
                isConnecting: false,
                isDisconnecting: true,
                isMonitorConnectedForDetectedGame);
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

        private static void ApplyEventMonitorStatus(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            DllInjectionResult injectionResult,
            GameEventMonitorStatus eventStatus,
            bool isConnecting,
            bool isDisconnecting,
            bool hasInjectionAttemptForDetectedGame,
            bool isMonitorConnectedForDetectedGame)
        {
            state.LatestEventStatus = eventStatus;

            if (isDisconnecting)
            {
                ApplyDisconnectingState(state, detectedGame, isMonitorConnectedForDetectedGame);
                return;
            }

            if (detectedGame is null)
            {
                state.InjectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
                state.EventCompatibilityText = AppStrings.Get("NoGameDetected");
                state.EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
                state.ConnectionLastUpdateText = GameConnectionSessionDisplayState.EmptyStatText;
                state.CurrentRoundText = GameConnectionSessionDisplayState.EmptyStatText;
                state.BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                state.RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            if (detectedGame.Variant != GameVariant.SteamZombies || detectedGame.AddressMap is null)
            {
                state.InjectionStatusText = AppStrings.Format(
                    "DllInjectionUnsupportedGameFormat",
                    detectedGame.DisplayName);
                state.EventCompatibilityText = AppStrings.Format(
                    "EventMonitorUnsupportedGameFormat",
                    detectedGame.DisplayName);
                state.EventMonitorStatusText = AppStrings.Get("EventMonitorCaptureDisabled");
                state.ConnectionLastUpdateText = GameConnectionSessionDisplayState.EmptyStatText;
                state.CurrentRoundText = GameConnectionSessionDisplayState.EmptyStatText;
                state.BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                state.RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            state.EventCompatibilityText = AppStrings.Get("GameProcessDetectorDisplayNameSteamZombies");
            if (!isMonitorConnectedForDetectedGame)
            {
                state.InjectionStatusText = isConnecting
                    ? AppStrings.Get("DllInjectionConnecting")
                    : hasInjectionAttemptForDetectedGame
                        ? injectionResult.Message
                        : AppStrings.Get("DllInjectionWaitingForConnect");
                state.EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForConnect");
                state.ConnectionLastUpdateText = GameConnectionSessionDisplayState.EmptyStatText;
                state.CurrentRoundText = GameConnectionSessionDisplayState.EmptyStatText;
                state.BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                state.RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            state.InjectionStatusText = FormatInjectionStatus(injectionResult, eventStatus);
            state.ConnectionLastUpdateText = AppStrings.Get("ConnectionLastUpdateJustNow");
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

            state.EventMonitorStatusText = monitorStatusText;
            state.CurrentRoundText = FormatRoundSession(eventStatus);
            state.BoxEventsText = GameEventFormatter.FormatRecentBoxEvents(eventStatus);
            state.RecentGameEventsText = GameEventFormatter.FormatRecentGameEvents(eventStatus);
        }

        private static void UpdateConnectButtonState(
            GameConnectionSessionDisplayState state,
            GameConnectionRefreshResult snapshot)
        {
            UpdateConnectButtonState(
                state,
                snapshot.CurrentGame,
                snapshot.CanAttemptConnect,
                snapshot.IsConnecting,
                snapshot.IsDisconnecting,
                snapshot.IsMonitorConnectedForCurrentGame);
        }

        private static void UpdateConnectButtonState(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            bool canAttemptConnect,
            bool isConnecting,
            bool isDisconnecting,
            bool isMonitorConnectedForDetectedGame)
        {
            state.ConnectButtonText = GetConnectButtonText(
                detectedGame,
                isConnecting,
                isDisconnecting,
                isMonitorConnectedForDetectedGame);
            state.IsConnectButtonEnabled = canAttemptConnect;
        }

        private static string GetConnectButtonText(
            DetectedGame? detectedGame,
            bool isConnecting,
            bool isDisconnecting,
            bool isMonitorConnectedForDetectedGame)
        {
            if (detectedGame is null)
            {
                return AppStrings.Get("ConnectButtonWaitingForGameText");
            }

            if (isConnecting)
            {
                return AppStrings.Get("ConnectButtonConnectingText");
            }

            if (isDisconnecting)
            {
                return AppStrings.Get("ConnectionCardStatusDisconnecting");
            }

            if (isMonitorConnectedForDetectedGame)
            {
                return AppStrings.Get("ConnectButtonConnectedText");
            }

            if (detectedGame.Variant != GameVariant.SteamZombies || !detectedGame.IsStatsSupported)
            {
                return AppStrings.Get("ConnectButtonUnsupportedText");
            }

            return AppStrings.Get("ConnectButtonText");
        }

        private static void SetConnectionState(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            bool isConnecting,
            bool isDisconnecting)
        {
            UpdateGameFooterState(state, detectedGame);
            UpdateEventFooterState(state, detectedGame, connectionState, isConnecting, isDisconnecting);
            UpdateFooterIndicator(state, connectionState);
            UpdateConnectionCardState(state, connectionState, isConnecting);
        }

        private static void UpdateGameFooterState(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                state.GameStatusText = AppStrings.Get("FooterGameNotRunning");
                return;
            }

            state.GameStatusText = AppStrings.Format("FooterGameDetectedFormat", detectedGame.DisplayName);
        }

        private static void UpdateEventFooterState(
            GameConnectionSessionDisplayState state,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            bool isConnecting,
            bool isDisconnecting)
        {
            if (connectionState == ConnectionState.Connected)
            {
                state.EventConnectionStatusText = AppStrings.Get("FooterEventsConnected");
                return;
            }

            if (connectionState == ConnectionState.Disconnecting || isDisconnecting)
            {
                state.EventConnectionStatusText = AppStrings.Get("FooterEventsDisconnecting");
                return;
            }

            if (isConnecting)
            {
                state.EventConnectionStatusText = AppStrings.Get("FooterEventsConnecting");
                return;
            }

            if (detectedGame is not null && !detectedGame.IsStatsSupported)
            {
                state.EventConnectionStatusText = AppStrings.Get("FooterEventsUnsupported");
                return;
            }

            state.EventConnectionStatusText = AppStrings.Get("FooterEventsNotConnected");
        }

        private static void UpdateFooterIndicator(GameConnectionSessionDisplayState state, ConnectionState connectionState)
        {
            state.IsFooterSuccessStatusVisible = connectionState == ConnectionState.Connected;
            state.IsFooterPendingStatusVisible = connectionState is ConnectionState.Detected or ConnectionState.Disconnecting or ConnectionState.Unsupported;
            state.IsFooterDisconnectedStatusVisible = connectionState == ConnectionState.Disconnected;
            state.IsFooterErrorStatusVisible = false;
        }

        private static void UpdateConnectionCardState(
            GameConnectionSessionDisplayState state,
            ConnectionState connectionState,
            bool isConnecting)
        {
            state.ConnectionCardStatusText = connectionState switch
            {
                ConnectionState.Connected => AppStrings.Get("ConnectionCardStatusConnected"),
                ConnectionState.Disconnecting => AppStrings.Get("ConnectionCardStatusDisconnecting"),
                ConnectionState.Unsupported => AppStrings.Get("ConnectionCardStatusUnsupported"),
                ConnectionState.Detected when isConnecting => AppStrings.Get("ConnectionCardStatusConnecting"),
                ConnectionState.Detected => AppStrings.Get("ConnectionCardStatusMonitoring"),
                _ => AppStrings.Get("ConnectionCardStatusDisconnected")
            };

            state.IsConnectButtonVisible = connectionState is not (ConnectionState.Connected or ConnectionState.Disconnecting);
            state.IsDisconnectButtonVisible = connectionState == ConnectionState.Connected;
        }

        private static void ClearStats(GameConnectionSessionDisplayState state)
        {
            state.PointsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.KillsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.DownsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.RevivesText = GameConnectionSessionDisplayState.EmptyStatText;
            state.HeadshotsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.PositionXText = GameConnectionSessionDisplayState.EmptyStatText;
            state.PositionYText = GameConnectionSessionDisplayState.EmptyStatText;
            state.PositionZText = GameConnectionSessionDisplayState.EmptyStatText;
            state.PlayerCandidateDetailsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.AmmoCandidateDetailsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.CounterCandidateDetailsText = GameConnectionSessionDisplayState.EmptyStatText;
            state.AddressCandidateDetailsText = GameConnectionSessionDisplayState.EmptyStatText;
        }
    }

    internal sealed class GameConnectionSessionDisplayState
    {
        public const string EmptyStatText = "--";

        public string PointsText { get; set; } = EmptyStatText;

        public string KillsText { get; set; } = EmptyStatText;

        public string DownsText { get; set; } = EmptyStatText;

        public string RevivesText { get; set; } = EmptyStatText;

        public string HeadshotsText { get; set; } = EmptyStatText;

        public string PositionXText { get; set; } = EmptyStatText;

        public string PositionYText { get; set; } = EmptyStatText;

        public string PositionZText { get; set; } = EmptyStatText;

        public string PlayerCandidateDetailsText { get; set; } = EmptyStatText;

        public string AmmoCandidateDetailsText { get; set; } = EmptyStatText;

        public string CounterCandidateDetailsText { get; set; } = EmptyStatText;

        public string AddressCandidateDetailsText { get; set; } = EmptyStatText;

        public string DetectedGameText { get; set; } = AppStrings.Get("NoGameDetected");

        public string EventCompatibilityText { get; set; } = AppStrings.Get("NoGameDetected");

        public string InjectionStatusText { get; set; } = AppStrings.Get("DllInjectionNotAttempted");

        public string EventMonitorStatusText { get; set; } = AppStrings.Get("EventMonitorWaitingForMonitor");

        public string CurrentRoundText { get; set; } = EmptyStatText;

        public string BoxEventsText { get; set; } = AppStrings.Get("RecentEventsEmpty");

        public string RecentGameEventsText { get; set; } = AppStrings.Get("RecentEventsEmpty");

        public string StatusText { get; set; } = AppStrings.Get("GameNotRunning");

        public string GameStatusText { get; set; } = AppStrings.Get("FooterGameNotRunning");

        public string EventConnectionStatusText { get; set; } = AppStrings.Get("FooterEventsNotConnected");

        public string ConnectButtonText { get; set; } = AppStrings.Get("ConnectButtonText");

        public string ConnectionCardStatusText { get; set; } = AppStrings.Get("ConnectionCardStatusDisconnected");

        public string ConnectionLastUpdateText { get; set; } = EmptyStatText;

        public bool IsConnectButtonEnabled { get; set; }

        public bool IsConnectButtonVisible { get; set; } = true;

        public bool IsDisconnectButtonVisible { get; set; }

        public bool IsFooterSuccessStatusVisible { get; set; }

        public bool IsFooterPendingStatusVisible { get; set; }

        public bool IsFooterDisconnectedStatusVisible { get; set; } = true;

        public bool IsFooterErrorStatusVisible { get; set; }

        public GameEventMonitorStatus LatestEventStatus { get; set; } = GameEventMonitorStatus.WaitingForMonitor;

        public static GameConnectionSessionDisplayState CreateDefault()
        {
            return new GameConnectionSessionDisplayState();
        }
    }
}
