using System;
using System.Linq;

namespace BO2.Services
{
    internal sealed class GameConnectionSessionDisplayProjector
    {
        public GameConnectionSessionDisplayProjection Project(GameConnectionSnapshot snapshot)
        {
            var projection = GameConnectionSessionDisplayProjection.CreateDefault();
            ApplyReadResult(
                projection,
                snapshot.CurrentGame,
                snapshot.ConnectionPhase,
                snapshot.ReadResult);
            ApplyEventMonitorStatus(
                projection,
                snapshot.CurrentGame,
                snapshot.ConnectionPhase,
                snapshot.EventMonitorSummary,
                snapshot.HasInjectionAttemptForCurrentGame);
            UpdateConnectButtonState(projection, snapshot);
            ApplyCommandAvailability(projection, snapshot);
            return projection;
        }

        private static DisplayText EmptyStatText => GameConnectionSessionDisplayProjection.EmptyStatText;

        private static DisplayText FormatLine(string labelResourceId, DisplayText value)
        {
            return DisplayText.Format(
                "LabeledValueFormat",
                DisplayText.Resource(labelResourceId),
                value);
        }

        private static void ApplyReadResult(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? currentGame,
            GameConnectionPhase connectionPhase,
            PlayerStatsReadResult? result)
        {
            projection.DetectedGameText = currentGame is null
                ? DisplayText.Resource("NoGameDetected")
                : DisplayText.Plain(currentGame.DisplayName);
            ApplyConnectionStatus(
                projection,
                currentGame,
                connectionPhase);

            if (result?.Stats is null)
            {
                ClearStats(projection);
                return;
            }

            projection.PointsText = DisplayText.Integer(result.Stats.Points);
            projection.KillsText = DisplayText.Integer(result.Stats.Kills);
            projection.DownsText = DisplayText.Integer(result.Stats.Downs);
            projection.RevivesText = DisplayText.Integer(result.Stats.Revives);
            projection.HeadshotsText = DisplayText.Integer(result.Stats.Headshots);
            projection.PositionXText = FormatCandidate(result.Stats.Candidates.PositionX);
            projection.PositionYText = FormatCandidate(result.Stats.Candidates.PositionY);
            projection.PositionZText = FormatCandidate(result.Stats.Candidates.PositionZ);
            projection.PlayerCandidateDetailsText = FormatPlayerCandidateDetails(result.Stats.Candidates);
            projection.AmmoCandidateDetailsText = FormatAmmoCandidateDetails(result.Stats.Candidates);
            projection.CounterCandidateDetailsText = FormatCounterCandidateDetails(result.Stats.Candidates);
            projection.AddressCandidateDetailsText = currentGame?.AddressMap is PlayerStatAddressMap addressMap
                ? FormatAddressCandidateDetails(addressMap)
                : EmptyStatText;
        }

        private static DisplayText FormatCandidate(int? value)
        {
            return value.HasValue
                ? DisplayText.Integer(value.Value)
                : DisplayText.Resource("UnavailableValue");
        }

        private static DisplayText FormatCandidate(float? value)
        {
            return value.HasValue
                ? DisplayText.Float2(value.Value)
                : DisplayText.Resource("UnavailableValue");
        }

        private static void ApplyConnectionStatus(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase)
        {
            if (connectionPhase == GameConnectionPhase.NoGame)
            {
                projection.StatusText = DisplayText.Resource("GameNotRunning");
                SetConnectionState(projection, detectedGame, ConnectionState.Disconnected, connectionPhase);
                return;
            }

            if (detectedGame is null)
            {
                projection.StatusText = DisplayText.Resource("GameNotRunning");
                SetConnectionState(projection, detectedGame, ConnectionState.Disconnected, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame)
            {
                projection.StatusText = FormatUnsupportedStatus(detectedGame);
                SetConnectionState(projection, detectedGame, ConnectionState.Unsupported, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Disconnecting)
            {
                projection.StatusText = DisplayText.Resource("ConnectionStatusDisconnecting");
                SetConnectionState(projection, detectedGame, ConnectionState.Disconnecting, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Connecting)
            {
                projection.StatusText = DisplayText.Resource("ConnectionStatusConnecting");
                SetConnectionState(projection, detectedGame, ConnectionState.Detected, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Connected)
            {
                projection.StatusText = DisplayText.Format(
                    "ConnectedStatusFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                SetConnectionState(projection, detectedGame, ConnectionState.Connected, connectionPhase);
                return;
            }

            if (connectionPhase is GameConnectionPhase.Detected or GameConnectionPhase.StatsOnlyDetected)
            {
                projection.StatusText = DisplayText.Format("GameDetectedConnectPromptFormat", DisplayText.Plain(detectedGame.DisplayName));
                SetConnectionState(projection, detectedGame, ConnectionState.Detected, connectionPhase);
            }
        }

        private static DisplayText FormatUnsupportedStatus(DetectedGame detectedGame)
        {
            return string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                ? DisplayText.Format("UnsupportedStatusFormat", DisplayText.Plain(detectedGame.DisplayName))
                : DisplayText.Format(
                    "UnsupportedStatusWithReasonFormat",
                    DisplayText.Plain(detectedGame.DisplayName),
                    DisplayText.Plain(detectedGame.UnsupportedReason));
        }

        private static DisplayText FormatPlayerCandidateDetails(PlayerCandidateStats candidates)
        {
            return DisplayText.Lines(
                FormatLine("VelocityXLabel", FormatCandidate(candidates.VelocityX)),
                FormatLine("VelocityYLabel", FormatCandidate(candidates.VelocityY)),
                FormatLine("VelocityZLabel", FormatCandidate(candidates.VelocityZ)),
                FormatLine("GravityFieldLabel", FormatCandidate(candidates.Gravity)),
                FormatLine("SpeedFieldLabel", FormatCandidate(candidates.Speed)),
                FormatLine("LastJumpHeightLabel", FormatCandidate(candidates.LastJumpHeight)),
                FormatLine("AdsAmountLabel", FormatCandidate(candidates.AdsAmount)),
                FormatLine("ViewAngleXLabel", FormatCandidate(candidates.ViewAngleX)),
                FormatLine("ViewAngleYLabel", FormatCandidate(candidates.ViewAngleY)),
                FormatLine("HeightIntLabel", FormatCandidate(candidates.HeightInt)),
                FormatLine("HeightFloatLabel", FormatCandidate(candidates.HeightFloat)),
                FormatLine("LegacyHealthLabel", FormatCandidate(candidates.LegacyHealth)),
                FormatLine("PlayerInfoHealthLabel", FormatCandidate(candidates.PlayerInfoHealth)),
                FormatLine("GEntityPlayerHealthLabel", FormatCandidate(candidates.GEntityPlayerHealth)));
        }

        private static DisplayText FormatAmmoCandidateDetails(PlayerCandidateStats candidates)
        {
            return DisplayText.Lines(
                FormatLine("AmmoSlot0Label", FormatCandidate(candidates.AmmoSlot0)),
                FormatLine("AmmoSlot1Label", FormatCandidate(candidates.AmmoSlot1)),
                FormatLine("LethalAmmoLabel", FormatCandidate(candidates.LethalAmmo)),
                FormatLine("AmmoSlot2Label", FormatCandidate(candidates.AmmoSlot2)),
                FormatLine("TacticalAmmoLabel", FormatCandidate(candidates.TacticalAmmo)),
                FormatLine("AmmoSlot3Label", FormatCandidate(candidates.AmmoSlot3)),
                FormatLine("AmmoSlot4Label", FormatCandidate(candidates.AmmoSlot4)));
        }

        private static DisplayText FormatCounterCandidateDetails(PlayerCandidateStats candidates)
        {
            return DisplayText.Lines(
                FormatLine("RoundCandidateLabel", FormatCandidate(candidates.Round)),
                FormatLine("AlternateKillsLabel", FormatCandidate(candidates.AlternateKills)),
                FormatLine("AlternateHeadshotsLabel", FormatCandidate(candidates.AlternateHeadshots)),
                FormatLine("SecondaryKillsLabel", FormatCandidate(candidates.SecondaryKills)),
                FormatLine("SecondaryHeadshotsLabel", FormatCandidate(candidates.SecondaryHeadshots)));
        }

        private static DisplayText FormatAddressCandidateDetails(PlayerStatAddressMap addressMap)
        {
            DerivedPlayerStateAddresses derivedPlayerState = addressMap.DerivedPlayerState;
            PlayerCandidateAddresses candidates = addressMap.Candidates;
            return DisplayText.Lines(
                FormatLine("LocalPlayerBaseLabel", DisplayText.Address(derivedPlayerState.LocalPlayerBaseAddress)),
                FormatLine("GEntityArrayLabel", DisplayText.Address(candidates.GEntityArrayAddress)),
                FormatLine("Zombie0GEntityLabel", DisplayText.Address(candidates.Zombie0GEntityAddress)),
                FormatLine("GEntitySizeLabel", DisplayText.Address(candidates.GEntitySize)));
        }

        private static DisplayText FormatEventCompatibility(GameConnectionEventMonitorState state)
        {
            return state switch
            {
                GameConnectionEventMonitorState.Waiting => DisplayText.Resource("EventMonitorWaitingForMonitor"),
                GameConnectionEventMonitorState.Connecting => DisplayText.Resource("EventMonitorWaitingForMonitor"),
                GameConnectionEventMonitorState.Ready => DisplayText.Resource("EventMonitorCompatible"),
                GameConnectionEventMonitorState.UnsupportedVersion => DisplayText.Resource("EventMonitorUnsupportedVersion"),
                GameConnectionEventMonitorState.CaptureDisabled => DisplayText.Resource("EventMonitorCaptureDisabled"),
                GameConnectionEventMonitorState.PollingFallback => DisplayText.Resource("EventMonitorPollingFallback"),
                _ => DisplayText.Resource("EventMonitorUnknown")
            };
        }

        private static DisplayText FormatRoundSession(GameConnectionEventMonitorSummary eventMonitor)
        {
            GameEvent? sessionEvent = eventMonitor.Status.RecentEvents
                .LastOrDefault(gameEvent => gameEvent.EventType is GameEventType.StartOfRound or GameEventType.EndOfRound or GameEventType.EndGame);
            if (sessionEvent is null)
            {
                return EmptyStatText;
            }

            if (sessionEvent.EventType == GameEventType.EndGame)
            {
                return DisplayText.Resource("RoundSessionEnded");
            }

            if (sessionEvent.LevelTime <= 0)
            {
                return EmptyStatText;
            }

            return DisplayText.Format(
                "CurrentRoundFormat",
                sessionEvent.LevelTime,
                DisplayText.Plain(sessionEvent.EventName));
        }

        private static void ApplyDisconnectingState(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase)
        {
            projection.StatusText = DisplayText.Resource("ConnectionStatusDisconnecting");
            projection.InjectionStatusText = DisplayText.Resource("DllInjectionDisconnecting");
            projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorDisconnecting");
            projection.LatestEventStatus = GameEventMonitorStatus.WaitingForMonitor;
            projection.ConnectionLastUpdateText = EmptyStatText;
            projection.CurrentRoundText = EmptyStatText;
            projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
            projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
            SetConnectionState(
                projection,
                detectedGame,
                ConnectionState.Disconnecting,
                connectionPhase);
        }

        private static DisplayText FormatInjectionStatus(GameConnectionEventMonitorSummary eventMonitor)
        {
            if (eventMonitor.State is GameConnectionEventMonitorState.ReadinessFailed or GameConnectionEventMonitorState.LoadingFailed)
            {
                return DisplayText.Plain(eventMonitor.FailureMessage ?? string.Empty);
            }

            return eventMonitor.State switch
            {
                GameConnectionEventMonitorState.Ready => DisplayText.Resource("DllInjectionMonitorReady"),
                GameConnectionEventMonitorState.PollingFallback => DisplayText.Resource("DllInjectionPollingFallback"),
                GameConnectionEventMonitorState.UnsupportedVersion => DisplayText.Resource("DllInjectionUnsupportedVersion"),
                GameConnectionEventMonitorState.CaptureDisabled => DisplayText.Resource("DllInjectionCaptureDisabled"),
                GameConnectionEventMonitorState.Waiting => DisplayText.Resource("DllInjectionWaitingForReadiness"),
                _ => DisplayText.Resource("DllInjectionNotAttempted")
            };
        }

        private static void ApplyEventMonitorStatus(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase,
            GameConnectionEventMonitorSummary eventMonitor,
            bool hasInjectionAttemptForDetectedGame)
        {
            projection.LatestEventStatus = eventMonitor.Status;

            if (connectionPhase == GameConnectionPhase.Disconnecting
                || eventMonitor.State is GameConnectionEventMonitorState.Disconnecting or GameConnectionEventMonitorState.StopPending)
            {
                ApplyDisconnectingState(projection, detectedGame, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.NoGame)
            {
                projection.InjectionStatusText = DisplayText.Resource("DllInjectionNotAttempted");
                projection.EventCompatibilityText = DisplayText.Resource("NoGameDetected");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForMonitor");
                projection.ConnectionLastUpdateText = EmptyStatText;
                projection.CurrentRoundText = EmptyStatText;
                projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
                projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
                return;
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame && detectedGame is not null)
            {
                projection.InjectionStatusText = DisplayText.Format(
                    "DllInjectionUnsupportedGameFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                projection.EventCompatibilityText = DisplayText.Format(
                    "EventMonitorUnsupportedGameFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorCaptureDisabled");
                projection.ConnectionLastUpdateText = EmptyStatText;
                projection.CurrentRoundText = EmptyStatText;
                projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
                projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
                return;
            }

            if (detectedGame is null)
            {
                projection.InjectionStatusText = DisplayText.Resource("DllInjectionNotAttempted");
                projection.EventCompatibilityText = DisplayText.Resource("NoGameDetected");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForMonitor");
                projection.ConnectionLastUpdateText = EmptyStatText;
                projection.CurrentRoundText = EmptyStatText;
                projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
                projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
                return;
            }

            projection.EventCompatibilityText = DisplayText.Resource("GameProcessDetectorDisplayNameSteamZombies");
            if (connectionPhase is GameConnectionPhase.Detected or GameConnectionPhase.StatsOnlyDetected
                || connectionPhase != GameConnectionPhase.Connected)
            {
                projection.InjectionStatusText = connectionPhase == GameConnectionPhase.Connecting
                    ? DisplayText.Resource("DllInjectionConnecting")
                    : eventMonitor.State is GameConnectionEventMonitorState.ReadinessFailed or GameConnectionEventMonitorState.LoadingFailed
                        ? DisplayText.Plain(eventMonitor.FailureMessage ?? string.Empty)
                    : hasInjectionAttemptForDetectedGame
                        ? FormatInjectionStatus(eventMonitor)
                        : DisplayText.Resource("DllInjectionWaitingForConnect");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForConnect");
                projection.ConnectionLastUpdateText = EmptyStatText;
                projection.CurrentRoundText = EmptyStatText;
                projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
                projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
                return;
            }

            projection.InjectionStatusText = FormatInjectionStatus(eventMonitor);
            projection.ConnectionLastUpdateText = DisplayText.Resource("ConnectionLastUpdateJustNow");
            DisplayText monitorStatusText = FormatEventCompatibility(eventMonitor.State);
            if (eventMonitor.Status.DroppedEventCount > 0 || eventMonitor.Status.DroppedNotifyCount > 0)
            {
                monitorStatusText = DisplayText.Format(
                    "EventMonitorCaptureDropsFormat",
                    monitorStatusText,
                    eventMonitor.Status.DroppedEventCount,
                    eventMonitor.Status.DroppedNotifyCount,
                    eventMonitor.Status.PublishedNotifyCount);
            }
            else if (eventMonitor.Status.PublishedNotifyCount > 0)
            {
                monitorStatusText = DisplayText.Format(
                    "EventMonitorPublishedEventsFormat",
                    monitorStatusText,
                    eventMonitor.Status.PublishedNotifyCount);
            }

            projection.EventMonitorStatusText = monitorStatusText;
            projection.CurrentRoundText = FormatRoundSession(eventMonitor);
            projection.BoxEventsText = GameEventDisplayTextProjector.FormatRecentBoxEvents(
                eventMonitor.Status,
                DisplayText.Resource("RecentEventsEmpty"));
            projection.RecentGameEventsText = GameEventDisplayTextProjector.FormatRecentGameEvents(
                eventMonitor.Status,
                DisplayText.Resource("RecentEventsEmpty"));
        }

        private static void UpdateConnectButtonState(
            GameConnectionSessionDisplayProjection projection,
            GameConnectionSnapshot snapshot)
        {
            projection.ConnectButtonText = GetConnectButtonText(
                snapshot.CurrentGame,
                snapshot.ConnectionPhase);
        }

        private static void ApplyCommandAvailability(
            GameConnectionSessionDisplayProjection projection,
            GameConnectionSnapshot snapshot)
        {
            projection.IsConnectButtonEnabled = snapshot.ConnectCommandAvailability.IsEnabled;
            projection.IsConnectButtonVisible = snapshot.ConnectCommandAvailability.IsVisible;
            projection.IsDisconnectButtonEnabled = snapshot.DisconnectCommandAvailability.IsEnabled;
            projection.IsDisconnectButtonVisible = snapshot.DisconnectCommandAvailability.IsVisible;
        }

        private static DisplayText GetConnectButtonText(
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase)
        {
            if (connectionPhase == GameConnectionPhase.NoGame || detectedGame is null)
            {
                return DisplayText.Resource("ConnectButtonWaitingForGameText");
            }

            if (connectionPhase == GameConnectionPhase.Connecting)
            {
                return DisplayText.Resource("ConnectButtonConnectingText");
            }

            if (connectionPhase == GameConnectionPhase.Disconnecting)
            {
                return DisplayText.Resource("ConnectionCardStatusDisconnecting");
            }

            if (connectionPhase == GameConnectionPhase.Connected)
            {
                return DisplayText.Resource("ConnectButtonConnectedText");
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame)
            {
                return DisplayText.Resource("ConnectButtonUnsupportedText");
            }

            return DisplayText.Resource("ConnectButtonText");
        }

        private static void SetConnectionState(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            GameConnectionPhase connectionPhase)
        {
            bool isConnecting = connectionPhase == GameConnectionPhase.Connecting;
            bool isDisconnecting = connectionPhase == GameConnectionPhase.Disconnecting;
            UpdateGameFooterState(projection, detectedGame);
            UpdateEventFooterState(projection, detectedGame, connectionState, isConnecting, isDisconnecting);
            UpdateFooterIndicator(projection, connectionState);
            UpdateConnectionCardState(projection, connectionState, isConnecting);
        }

        private static void UpdateGameFooterState(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                projection.GameStatusText = DisplayText.Resource("FooterGameNotRunning");
                return;
            }

            projection.GameStatusText = DisplayText.Format("FooterGameDetectedFormat", DisplayText.Plain(detectedGame.DisplayName));
        }

        private static void UpdateEventFooterState(
            GameConnectionSessionDisplayProjection projection,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            bool isConnecting,
            bool isDisconnecting)
        {
            if (connectionState == ConnectionState.Connected)
            {
                projection.EventConnectionStatusText = DisplayText.Resource("FooterEventsConnected");
                return;
            }

            if (connectionState == ConnectionState.Disconnecting || isDisconnecting)
            {
                projection.EventConnectionStatusText = DisplayText.Resource("FooterEventsDisconnecting");
                return;
            }

            if (isConnecting)
            {
                projection.EventConnectionStatusText = DisplayText.Resource("FooterEventsConnecting");
                return;
            }

            if (detectedGame is not null && !detectedGame.IsStatsSupported)
            {
                projection.EventConnectionStatusText = DisplayText.Resource("FooterEventsUnsupported");
                return;
            }

            projection.EventConnectionStatusText = DisplayText.Resource("FooterEventsNotConnected");
        }

        private static void UpdateFooterIndicator(GameConnectionSessionDisplayProjection projection, ConnectionState connectionState)
        {
            projection.IsFooterSuccessStatusVisible = connectionState == ConnectionState.Connected;
            projection.IsFooterPendingStatusVisible = connectionState is ConnectionState.Detected or ConnectionState.Disconnecting or ConnectionState.Unsupported;
            projection.IsFooterDisconnectedStatusVisible = connectionState == ConnectionState.Disconnected;
            projection.IsFooterErrorStatusVisible = false;
        }

        private static void UpdateConnectionCardState(
            GameConnectionSessionDisplayProjection projection,
            ConnectionState connectionState,
            bool isConnecting)
        {
            projection.ConnectionCardStatusText = connectionState switch
            {
                ConnectionState.Connected => DisplayText.Resource("ConnectionCardStatusConnected"),
                ConnectionState.Disconnecting => DisplayText.Resource("ConnectionCardStatusDisconnecting"),
                ConnectionState.Unsupported => DisplayText.Resource("ConnectionCardStatusUnsupported"),
                ConnectionState.Detected when isConnecting => DisplayText.Resource("ConnectionCardStatusConnecting"),
                ConnectionState.Detected => DisplayText.Resource("ConnectionCardStatusMonitoring"),
                _ => DisplayText.Resource("ConnectionCardStatusDisconnected")
            };
        }

        private static void ClearStats(GameConnectionSessionDisplayProjection projection)
        {
            projection.PointsText = EmptyStatText;
            projection.KillsText = EmptyStatText;
            projection.DownsText = EmptyStatText;
            projection.RevivesText = EmptyStatText;
            projection.HeadshotsText = EmptyStatText;
            projection.PositionXText = EmptyStatText;
            projection.PositionYText = EmptyStatText;
            projection.PositionZText = EmptyStatText;
            projection.PlayerCandidateDetailsText = EmptyStatText;
            projection.AmmoCandidateDetailsText = EmptyStatText;
            projection.CounterCandidateDetailsText = EmptyStatText;
            projection.AddressCandidateDetailsText = EmptyStatText;
        }
    }

    internal sealed class GameConnectionSessionDisplayProjection
    {
        public static DisplayText EmptyStatText => DisplayText.Plain("--");

        public DisplayText PointsText { get; set; } = EmptyStatText;

        public DisplayText KillsText { get; set; } = EmptyStatText;

        public DisplayText DownsText { get; set; } = EmptyStatText;

        public DisplayText RevivesText { get; set; } = EmptyStatText;

        public DisplayText HeadshotsText { get; set; } = EmptyStatText;

        public DisplayText PositionXText { get; set; } = EmptyStatText;

        public DisplayText PositionYText { get; set; } = EmptyStatText;

        public DisplayText PositionZText { get; set; } = EmptyStatText;

        public DisplayText PlayerCandidateDetailsText { get; set; } = EmptyStatText;

        public DisplayText AmmoCandidateDetailsText { get; set; } = EmptyStatText;

        public DisplayText CounterCandidateDetailsText { get; set; } = EmptyStatText;

        public DisplayText AddressCandidateDetailsText { get; set; } = EmptyStatText;

        public DisplayText DetectedGameText { get; set; } = DisplayText.Resource("NoGameDetected");

        public DisplayText EventCompatibilityText { get; set; } = DisplayText.Resource("NoGameDetected");

        public DisplayText InjectionStatusText { get; set; } = DisplayText.Resource("DllInjectionNotAttempted");

        public DisplayText EventMonitorStatusText { get; set; } = DisplayText.Resource("EventMonitorWaitingForMonitor");

        public DisplayText CurrentRoundText { get; set; } = EmptyStatText;

        public DisplayText BoxEventsText { get; set; } = DisplayText.Resource("RecentEventsEmpty");

        public DisplayText RecentGameEventsText { get; set; } = DisplayText.Resource("RecentEventsEmpty");

        public DisplayText StatusText { get; set; } = DisplayText.Resource("GameNotRunning");

        public DisplayText GameStatusText { get; set; } = DisplayText.Resource("FooterGameNotRunning");

        public DisplayText EventConnectionStatusText { get; set; } = DisplayText.Resource("FooterEventsNotConnected");

        public DisplayText ConnectButtonText { get; set; } = DisplayText.Resource("ConnectButtonText");

        public DisplayText ConnectionCardStatusText { get; set; } = DisplayText.Resource("ConnectionCardStatusDisconnected");

        public DisplayText ConnectionLastUpdateText { get; set; } = EmptyStatText;

        public bool IsConnectButtonEnabled { get; set; }

        public bool IsConnectButtonVisible { get; set; } = true;

        public bool IsDisconnectButtonVisible { get; set; }

        public bool IsDisconnectButtonEnabled { get; set; }

        public bool IsFooterSuccessStatusVisible { get; set; }

        public bool IsFooterPendingStatusVisible { get; set; }

        public bool IsFooterDisconnectedStatusVisible { get; set; } = true;

        public bool IsFooterErrorStatusVisible { get; set; }

        public GameEventMonitorStatus LatestEventStatus { get; set; } = GameEventMonitorStatus.WaitingForMonitor;

        public static GameConnectionSessionDisplayProjection CreateDefault()
        {
            return new GameConnectionSessionDisplayProjection();
        }
    }
}
