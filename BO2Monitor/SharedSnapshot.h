#pragma once

#include <Windows.h>
#include <array>
#include <cstdint>
#include <string>

namespace BO2Monitor
{
    constexpr wchar_t SharedMemoryNamePrefix[] = L"BO2MonitorSharedMem-";
    constexpr wchar_t EventHandleNamePrefix[] = L"BO2MonitorEvent-";
    constexpr std::uint32_t SnapshotMagic = 0x45324F42; // BO2E
    constexpr std::uint32_t SnapshotVersion = 4;
    constexpr std::size_t MaxEventCount = 128;
    constexpr std::size_t MaxEventNameBytes = 64;

    enum class GameCompatibilityState : std::int32_t
    {
        Unknown = 0,
        WaitingForMonitor = 1,
        Compatible = 2,
        UnsupportedVersion = 3,
        CaptureDisabled = 4,
        PollingFallback = 5
    };

    enum class GameEventType : std::int32_t
    {
        Unknown = 0,
        StartOfRound = 1,
        EndOfRound = 2,
        PowerUpGrabbed = 3,
        DogRoundStarting = 4,
        PowerOn = 5,
        EndGame = 6,
        PerkBought = 7,
        RoundChanged = 8,
        PointsChanged = 9,
        KillsChanged = 10,
        DownsChanged = 11,
        NotifyCandidateRejected = 12,
        NotifyEntryCandidate = 13,
        StringResolverCandidate = 14,
        NotifyObserved = 15,
        BoxEvent = 16
    };

#pragma pack(push, 1)
    struct GameEventRecord
    {
        GameEventType EventType;
        std::int32_t LevelTime;
        std::uint32_t OwnerId;
        std::uint32_t StringValue;
        char EventName[MaxEventNameBytes];
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

    static_assert(sizeof(GameEventRecord) == 80);
    static_assert(sizeof(SharedSnapshot) == 10276);

    std::wstring BuildSharedMemoryName(DWORD processId);
    std::wstring BuildEventHandleName(DWORD processId);

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
            std::uint32_t stringValue = 0);

    private:
        void InitializeSnapshot();
        void BeginSnapshotWrite();
        void EndSnapshotWrite();

        HANDLE mappingHandle_ = nullptr;
        HANDLE eventHandle_ = nullptr;
        SharedSnapshot* snapshot_ = nullptr;
    };
}
