#include "SharedSnapshot.h"

#include <algorithm>
#include <cstring>

namespace BO2Monitor
{
    std::wstring BuildSharedMemoryName(DWORD processId)
    {
        return std::wstring(SharedMemoryNamePrefix) + std::to_wstring(processId);
    }

    std::wstring BuildEventHandleName(DWORD processId)
    {
        return std::wstring(EventHandleNamePrefix) + std::to_wstring(processId);
    }

    std::wstring BuildStopEventHandleName(DWORD processId)
    {
        return std::wstring(StopEventHandleNamePrefix) + std::to_wstring(processId);
    }

    void InitializeSharedSnapshot(SharedSnapshot& snapshot)
    {
        std::memset(&snapshot, 0, sizeof(SharedSnapshot));
        snapshot.Magic = SnapshotMagic;
        snapshot.Version = SnapshotVersion;
        snapshot.CompatibilityState = GameCompatibilityState::WaitingForMonitor;
        snapshot.WriteSequence = 0;
    }

    void SetSharedSnapshotCompatibility(SharedSnapshot& snapshot, GameCompatibilityState compatibilityState)
    {
        ++snapshot.WriteSequence;
        MemoryBarrier();
        snapshot.CompatibilityState = compatibilityState;
        MemoryBarrier();
        ++snapshot.WriteSequence;
    }

    void SetSharedSnapshotNotifyEventCounters(
        SharedSnapshot& snapshot,
        std::uint32_t droppedNotifyCount,
        std::uint32_t publishedNotifyCount)
    {
        ++snapshot.WriteSequence;
        MemoryBarrier();
        snapshot.DroppedNotifyCount = droppedNotifyCount;
        snapshot.PublishedNotifyCount = publishedNotifyCount;
        MemoryBarrier();
        ++snapshot.WriteSequence;
    }

    void AppendSharedSnapshotEvent(
        SharedSnapshot& snapshot,
        GameEventType eventType,
        const char* eventName,
        std::int32_t levelTime,
        std::uint32_t ownerId,
        std::uint32_t stringValue,
        std::uint32_t tick,
        const char* weaponName)
    {
        if (eventName == nullptr)
        {
            return;
        }

        ++snapshot.WriteSequence;
        MemoryBarrier();
        std::uint32_t slot = snapshot.EventWriteIndex;

        GameEventRecord& record = snapshot.Events[slot];
        record.EventType = eventType;
        record.LevelTime = levelTime;
        record.OwnerId = ownerId;
        record.StringValue = stringValue;
        record.Tick = tick == 0 ? GetTickCount() : tick;
        std::memset(record.EventName, 0, sizeof(record.EventName));
        const std::size_t sourceLength = std::strlen(eventName);
        const std::size_t copyLength = std::min(sourceLength, MaxEventNameBytes - 1);
        std::memcpy(record.EventName, eventName, copyLength);
        std::memset(record.WeaponName, 0, sizeof(record.WeaponName));
        if (weaponName != nullptr)
        {
            const std::size_t weaponNameLength = std::strlen(weaponName);
            const std::size_t weaponNameCopyLength = std::min(weaponNameLength, MaxWeaponNameBytes - 1);
            std::memcpy(record.WeaponName, weaponName, weaponNameCopyLength);
        }

        snapshot.EventWriteIndex = (snapshot.EventWriteIndex + 1) % MaxEventCount;

        if (snapshot.EventCount < MaxEventCount)
        {
            ++snapshot.EventCount;
        }
        else
        {
            ++snapshot.DroppedEventCount;
        }

        MemoryBarrier();
        ++snapshot.WriteSequence;
    }

    SharedSnapshotWriter::~SharedSnapshotWriter()
    {
        if (snapshot_ != nullptr)
        {
            UnmapViewOfFile(snapshot_);
            snapshot_ = nullptr;
        }

        if (eventHandle_ != nullptr)
        {
            CloseHandle(eventHandle_);
            eventHandle_ = nullptr;
        }

        if (stopEventHandle_ != nullptr)
        {
            CloseHandle(stopEventHandle_);
            stopEventHandle_ = nullptr;
        }

        if (mappingHandle_ != nullptr)
        {
            CloseHandle(mappingHandle_);
            mappingHandle_ = nullptr;
        }
    }

    bool SharedSnapshotWriter::Initialize()
    {
        const DWORD processId = GetCurrentProcessId();
        const std::wstring sharedMemoryName = BuildSharedMemoryName(processId);
        const std::wstring eventHandleName = BuildEventHandleName(processId);
        const std::wstring stopEventHandleName = BuildStopEventHandleName(processId);
        mappingHandle_ = CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            nullptr,
            PAGE_READWRITE,
            0,
            static_cast<DWORD>(sizeof(SharedSnapshot)),
            sharedMemoryName.c_str());
        if (mappingHandle_ == nullptr)
        {
            return false;
        }

        snapshot_ = static_cast<SharedSnapshot*>(MapViewOfFile(
            mappingHandle_,
            FILE_MAP_WRITE,
            0,
            0,
            sizeof(SharedSnapshot)));
        if (snapshot_ == nullptr)
        {
            return false;
        }

        eventHandle_ = CreateEventW(nullptr, FALSE, FALSE, eventHandleName.c_str());
        if (eventHandle_ == nullptr)
        {
            return false;
        }

        stopEventHandle_ = CreateEventW(nullptr, TRUE, FALSE, stopEventHandleName.c_str());
        if (stopEventHandle_ == nullptr)
        {
            return false;
        }

        InitializeSharedSnapshot(*snapshot_);
        return true;
    }

    void SharedSnapshotWriter::SetCompatibility(GameCompatibilityState compatibilityState)
    {
        if (snapshot_ == nullptr)
        {
            return;
        }

        SetSharedSnapshotCompatibility(*snapshot_, compatibilityState);
        if (eventHandle_ != nullptr)
        {
            SetEvent(eventHandle_);
        }
    }

    void SharedSnapshotWriter::SetNotifyEventCounters(std::uint32_t droppedNotifyCount, std::uint32_t publishedNotifyCount)
    {
        if (snapshot_ == nullptr)
        {
            return;
        }

        SetSharedSnapshotNotifyEventCounters(*snapshot_, droppedNotifyCount, publishedNotifyCount);
    }

    void SharedSnapshotWriter::PublishEvent(
        GameEventType eventType,
        const char* eventName,
        std::int32_t levelTime,
        std::uint32_t ownerId,
        std::uint32_t stringValue,
        std::uint32_t tick,
        const char* weaponName)
    {
        if (snapshot_ == nullptr || eventName == nullptr)
        {
            return;
        }

        AppendSharedSnapshotEvent(*snapshot_, eventType, eventName, levelTime, ownerId, stringValue, tick, weaponName);
        if (eventHandle_ != nullptr)
        {
            SetEvent(eventHandle_);
        }
    }

    bool SharedSnapshotWriter::WaitForStop(DWORD milliseconds) const
    {
        return stopEventHandle_ != nullptr
            && WaitForSingleObject(stopEventHandle_, milliseconds) == WAIT_OBJECT_0;
    }

}
