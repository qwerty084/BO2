using System;
using System.Linq;
using System.Reflection;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryDisplayProjectorTests
    {
        public enum RecordingStatusCase
        {
            ActiveMapRound,
            ActiveRoundOnly,
            ActiveTrimmedMap,
            ActiveNoMap,
            WaitingForRoundOneMap,
            WaitingForRoundOneNoMap,
            SavePendingMap,
            SavePendingNoMap,
            SavedMap,
            SavedNoMap,
            FailedSaveMap,
            FailedSaveNoMap,
            UnavailableNotConnected,
            UnavailableRequiresSupportedMap,
            UnavailableMissingMapIdentity,
            UnavailableMissingFriendlyMapName,
            UnavailableRequiresHookBackedEventMonitor,
            DiscardedSequenceGap,
            DiscardedDroppedLifecycleData,
            DiscardedPollingFallback,
            DiscardedMissingRequiredStats,
            DiscardedDisconnected,
            DiscardedDetectedGameChanged,
            DiscardedAppClosed,
            DiscardedMissingMapIdentity,
            DiscardedUnsupportedMapIdentity,
            DiscardedMissingFriendlyMapName
        }

        public static TheoryData<RecordingStatusCase, string, string> RecordingStatusCases { get; } = new()
        {
            {
                RecordingStatusCase.ActiveMapRound,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveMapRoundFormat(Farm, 3)"
            },
            {
                RecordingStatusCase.ActiveRoundOnly,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveRoundFormat(3)"
            },
            {
                RecordingStatusCase.ActiveTrimmedMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveMapFormat(Farm)"
            },
            {
                RecordingStatusCase.ActiveNoMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveText"
            },
            {
                RecordingStatusCase.WaitingForRoundOneMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusWaitingForRoundOneMapFormat(Farm)"
            },
            {
                RecordingStatusCase.WaitingForRoundOneNoMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusWaitingForRoundOneText"
            },
            {
                RecordingStatusCase.SavePendingMap,
                "GameHistoryRecordingStatusSavePendingTitle",
                "GameHistoryRecordingStatusSavePendingMapFormat(Farm)"
            },
            {
                RecordingStatusCase.SavePendingNoMap,
                "GameHistoryRecordingStatusSavePendingTitle",
                "GameHistoryRecordingStatusSavePendingText"
            },
            {
                RecordingStatusCase.SavedMap,
                "GameHistoryRecordingStatusSavedTitle",
                "GameHistoryRecordingStatusSavedMapFormat(Farm)"
            },
            {
                RecordingStatusCase.SavedNoMap,
                "GameHistoryRecordingStatusSavedTitle",
                "GameHistoryRecordingStatusSavedText"
            },
            {
                RecordingStatusCase.FailedSaveMap,
                "GameHistoryRecordingStatusFailedSaveTitle",
                "GameHistoryRecordingStatusFailedSaveMapFormat(Farm)"
            },
            {
                RecordingStatusCase.FailedSaveNoMap,
                "GameHistoryRecordingStatusFailedSaveTitle",
                "GameHistoryRecordingStatusFailedSaveText"
            },
            {
                RecordingStatusCase.UnavailableNotConnected,
                "GameHistoryRecordingStatusWaitingTitle",
                "GameHistoryRecordingStatusWaitingText"
            },
            {
                RecordingStatusCase.UnavailableRequiresSupportedMap,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableMissingMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableMissingFriendlyMapName,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableRequiresHookBackedEventMonitor,
                "GameHistoryRecordingStatusRequiresHookTitle",
                "GameHistoryRecordingStatusRequiresHookText"
            },
            {
                RecordingStatusCase.DiscardedSequenceGap,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedDroppedLifecycleData,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedPollingFallback,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedMissingRequiredStats,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedMissingStatsText"
            },
            {
                RecordingStatusCase.DiscardedDisconnected,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedDetectedGameChanged,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedAppClosed,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedMissingMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.DiscardedUnsupportedMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.DiscardedMissingFriendlyMapName,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
        };

        [Theory]
        [MemberData(nameof(RecordingStatusCases))]
        public void ProjectRecordingStatus_ReturnsRenderedTitleAndBodyText(
            RecordingStatusCase statusCase,
            string expectedTitle,
            string expectedBodyText)
        {
            GameHistoryRecordingStatus status = CreateStatus(statusCase);
            GameHistoryRecordingStatusDisplayState state = CreateProjector().ProjectRecordingStatus(status);

            Assert.Equal(expectedTitle, state.Title);
            Assert.Equal(expectedBodyText, state.BodyText);
        }

        [Fact]
        public void DisplayStateContract_IncludesRecordingStatusTextFieldsOnly()
        {
            string[] propertyNames =
            [
                .. typeof(GameHistoryRecordingStatusDisplayState)
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property => property.Name)
            ];
            string[] requiredProperties =
            [
                nameof(GameHistoryRecordingStatusDisplayState.Title),
                nameof(GameHistoryRecordingStatusDisplayState.BodyText)
            ];

            Assert.True(typeof(GameHistoryRecordingStatusDisplayState).IsSealed);
            Assert.Equal(
                requiredProperties.Order(StringComparer.Ordinal),
                propertyNames.Order(StringComparer.Ordinal));
        }

        private static GameHistoryDisplayProjector CreateProjector()
        {
            return new GameHistoryDisplayProjector();
        }

        private static GameHistoryRecordingStatus CreateStatus(RecordingStatusCase statusCase)
        {
            return statusCase switch
            {
                RecordingStatusCase.ActiveMapRound => GameHistoryRecordingStatus.Recording(3, "Farm"),
                RecordingStatusCase.ActiveRoundOnly => GameHistoryRecordingStatus.Recording(3),
                RecordingStatusCase.ActiveTrimmedMap => GameHistoryRecordingStatus.Active(" Farm "),
                RecordingStatusCase.ActiveNoMap => GameHistoryRecordingStatus.Active(null),
                RecordingStatusCase.WaitingForRoundOneMap => GameHistoryRecordingStatus.WaitingForRoundOne("Farm"),
                RecordingStatusCase.WaitingForRoundOneNoMap => GameHistoryRecordingStatus.WaitingForRoundOne(),
                RecordingStatusCase.SavePendingMap => GameHistoryRecordingStatus.SavePending("farm-run", "Farm"),
                RecordingStatusCase.SavePendingNoMap => GameHistoryRecordingStatus.SavePending("farm-run"),
                RecordingStatusCase.SavedMap => GameHistoryRecordingStatus.Saved("farm-run", "Farm"),
                RecordingStatusCase.SavedNoMap => GameHistoryRecordingStatus.Saved("farm-run"),
                RecordingStatusCase.FailedSaveMap => GameHistoryRecordingStatus.FailedSave(
                    "farm-run",
                    "Farm",
                    "database locked"),
                RecordingStatusCase.FailedSaveNoMap => GameHistoryRecordingStatus.FailedSave("farm-run"),
                RecordingStatusCase.UnavailableNotConnected => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.NotConnected),
                RecordingStatusCase.UnavailableRequiresSupportedMap => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresSupportedMap),
                RecordingStatusCase.UnavailableMissingMapIdentity => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.MissingMapIdentity),
                RecordingStatusCase.UnavailableMissingFriendlyMapName => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.MissingFriendlyMapName),
                RecordingStatusCase.UnavailableRequiresHookBackedEventMonitor => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor),
                RecordingStatusCase.DiscardedSequenceGap => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.SequenceGap),
                RecordingStatusCase.DiscardedDroppedLifecycleData => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.DroppedLifecycleData),
                RecordingStatusCase.DiscardedPollingFallback => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.PollingFallback),
                RecordingStatusCase.DiscardedMissingRequiredStats => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingRequiredStats),
                RecordingStatusCase.DiscardedDisconnected => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.Disconnected),
                RecordingStatusCase.DiscardedDetectedGameChanged => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.DetectedGameChanged),
                RecordingStatusCase.DiscardedAppClosed => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.AppClosed),
                RecordingStatusCase.DiscardedMissingMapIdentity => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingMapIdentity),
                RecordingStatusCase.DiscardedUnsupportedMapIdentity => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.UnsupportedMapIdentity),
                RecordingStatusCase.DiscardedMissingFriendlyMapName => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingFriendlyMapName),
                _ => throw new ArgumentOutOfRangeException(nameof(statusCase), statusCase, null)
            };
        }
    }
}
