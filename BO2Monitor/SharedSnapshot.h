#pragma once

#include <Windows.h>
#include <array>
#include <cstdint>
#include <string>

#include "Generated/EventMonitorSnapshotContract.g.h"

namespace BO2Monitor
{
    constexpr const wchar_t* SharedMemoryNamePrefix = Generated::SharedMemoryNamePrefix;
    constexpr const wchar_t* EventHandleNamePrefix = Generated::UpdateEventNamePrefix;
    constexpr const wchar_t* StopEventHandleNamePrefix = Generated::StopEventNamePrefix;
    constexpr std::uint32_t SnapshotMagic = Generated::SnapshotMagic;
    constexpr std::uint32_t SnapshotVersion = Generated::SnapshotVersion;
    constexpr std::size_t MaxEventCount = Generated::MaxEventCount;
    constexpr std::size_t MaxEventNameBytes = Generated::MaxEventNameBytes;
    constexpr std::size_t MaxWeaponNameBytes = Generated::MaxWeaponNameBytes;
    constexpr std::size_t HeaderSize = Generated::HeaderSize;
    constexpr std::size_t EventRecordSize = Generated::EventRecordSize;
    constexpr std::size_t SharedMemorySize = Generated::SharedMemorySize;

    enum class GameCompatibilityState : std::int32_t
    {
        Unknown = Generated::GameCompatibilityStateUnknown,
        WaitingForMonitor = Generated::GameCompatibilityStateWaitingForMonitor,
        Compatible = Generated::GameCompatibilityStateCompatible,
        UnsupportedVersion = Generated::GameCompatibilityStateUnsupportedVersion,
        CaptureDisabled = Generated::GameCompatibilityStateCaptureDisabled,
        PollingFallback = Generated::GameCompatibilityStatePollingFallback
    };

    enum class GameEventType : std::int32_t
    {
        Unknown = Generated::GameEventTypeUnknown,
        StartOfRound = Generated::GameEventTypeStartOfRound,
        EndOfRound = Generated::GameEventTypeEndOfRound,
        PowerUpGrabbed = Generated::GameEventTypePowerUpGrabbed,
        DogRoundStarting = Generated::GameEventTypeDogRoundStarting,
        PowerOn = Generated::GameEventTypePowerOn,
        EndGame = Generated::GameEventTypeEndGame,
        PerkBought = Generated::GameEventTypePerkBought,
        RoundChanged = Generated::GameEventTypeRoundChanged,
        PointsChanged = Generated::GameEventTypePointsChanged,
        KillsChanged = Generated::GameEventTypeKillsChanged,
        DownsChanged = Generated::GameEventTypeDownsChanged,
        NotifyCandidateRejected = Generated::GameEventTypeNotifyCandidateRejected,
        NotifyEntryCandidate = Generated::GameEventTypeNotifyEntryCandidate,
        StringResolverCandidate = Generated::GameEventTypeStringResolverCandidate,
        NotifyObserved = Generated::GameEventTypeNotifyObserved,
        BoxEvent = Generated::GameEventTypeBoxEvent
    };

#pragma pack(push, 1)
    struct GameEventRecord
    {
        GameEventType EventType;
        std::int32_t LevelTime;
        std::uint32_t OwnerId;
        std::uint32_t StringValue;
        std::uint32_t Tick;
        char EventName[MaxEventNameBytes];
        char WeaponName[MaxWeaponNameBytes];
    };

    struct SharedSnapshot
    {
        std::uint32_t Magic;
        std::uint32_t Version;
        GameCompatibilityState CompatibilityState;
        std::uint32_t EventWriteIndex;
        std::uint32_t DroppedEventCount;
        std::uint32_t EventCount;
        std::uint32_t DroppedNotifyCount;
        std::uint32_t PublishedNotifyCount;
        std::uint32_t WriteSequence;
        GameEventRecord Events[MaxEventCount];
    };
#pragma pack(pop)

    inline constexpr bool SharedSnapshotLayoutMatchesContract =
        (Generated::AssertSharedSnapshotLayout<SharedSnapshot, GameEventRecord>(), true);
    static_assert(SharedSnapshotLayoutMatchesContract);

    std::wstring BuildSharedMemoryName(DWORD processId);
    std::wstring BuildEventHandleName(DWORD processId);
    std::wstring BuildStopEventHandleName(DWORD processId);
    void InitializeSharedSnapshot(SharedSnapshot& snapshot);
    void SetSharedSnapshotCompatibility(SharedSnapshot& snapshot, GameCompatibilityState compatibilityState);
    void SetSharedSnapshotNotifyEventCounters(
        SharedSnapshot& snapshot,
        std::uint32_t droppedNotifyCount,
        std::uint32_t publishedNotifyCount);
    void AppendSharedSnapshotEvent(
        SharedSnapshot& snapshot,
        GameEventType eventType,
        const char* eventName,
        std::int32_t levelTime,
        std::uint32_t ownerId = 0,
        std::uint32_t stringValue = 0,
        std::uint32_t tick = 0,
        const char* weaponName = nullptr);

    class SharedSnapshotWriter
    {
    public:
        SharedSnapshotWriter() = default;
        SharedSnapshotWriter(const SharedSnapshotWriter&) = delete;
        SharedSnapshotWriter& operator=(const SharedSnapshotWriter&) = delete;
        ~SharedSnapshotWriter();

        bool Initialize();
        void SetCompatibility(GameCompatibilityState compatibilityState);
        void SetNotifyEventCounters(std::uint32_t droppedNotifyCount, std::uint32_t publishedNotifyCount);
        void PublishEvent(
            GameEventType eventType,
            const char* eventName,
            std::int32_t levelTime,
            std::uint32_t ownerId = 0,
            std::uint32_t stringValue = 0,
            std::uint32_t tick = 0,
            const char* weaponName = nullptr);
        bool WaitForStop(DWORD milliseconds) const;

    private:
        HANDLE mappingHandle_ = nullptr;
        HANDLE eventHandle_ = nullptr;
        HANDLE stopEventHandle_ = nullptr;
        SharedSnapshot* snapshot_ = nullptr;
    };
}
