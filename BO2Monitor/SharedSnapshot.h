#pragma once

#include <Windows.h>
#include <array>
#include <cstdint>

namespace BO2Monitor
{
    constexpr wchar_t SharedMemoryName[] = L"BO2MonitorSharedMem";
    constexpr wchar_t EventHandleName[] = L"BO2MonitorEvent";
    constexpr std::uint32_t SnapshotMagic = 0x45324F42; // BO2E
    constexpr std::uint32_t SnapshotVersion = 1;
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
        NotifyObserved = 15
    };

#pragma pack(push, 1)
    struct GameEventRecord
    {
        GameEventType EventType;
        std::int32_t LevelTime;
        char EventName[MaxEventNameBytes];
    };

    struct SharedSnapshot
    {
        std::uint32_t Magic;
        std::uint32_t Version;
        GameCompatibilityState CompatibilityState;
        std::uint32_t Reserved;
        std::uint32_t DroppedEventCount;
        std::uint32_t EventCount;
        GameEventRecord Events[MaxEventCount];
    };
#pragma pack(pop)

    static_assert(sizeof(GameEventRecord) == 72);
    static_assert(sizeof(SharedSnapshot) == 9240);

    class SharedSnapshotWriter
    {
    public:
        SharedSnapshotWriter() = default;
        SharedSnapshotWriter(const SharedSnapshotWriter&) = delete;
        SharedSnapshotWriter& operator=(const SharedSnapshotWriter&) = delete;
        ~SharedSnapshotWriter();

        bool Initialize();
        void SetCompatibility(GameCompatibilityState compatibilityState);
        void PublishEvent(GameEventType eventType, const char* eventName, std::int32_t levelTime);

    private:
        void InitializeSnapshot();

        HANDLE mappingHandle_ = nullptr;
        HANDLE eventHandle_ = nullptr;
        SharedSnapshot* snapshot_ = nullptr;
    };
}
