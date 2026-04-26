#include "SharedSnapshot.h"

#include <algorithm>
#include <cstring>

namespace BO2Monitor
{
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

        if (mappingHandle_ != nullptr)
        {
            CloseHandle(mappingHandle_);
            mappingHandle_ = nullptr;
        }
    }

    bool SharedSnapshotWriter::Initialize()
    {
        mappingHandle_ = CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            nullptr,
            PAGE_READWRITE,
            0,
            static_cast<DWORD>(sizeof(SharedSnapshot)),
            SharedMemoryName);
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

        eventHandle_ = CreateEventW(nullptr, FALSE, FALSE, EventHandleName);
        if (eventHandle_ == nullptr)
        {
            return false;
        }

        InitializeSnapshot();
        return true;
    }

    void SharedSnapshotWriter::SetCompatibility(GameCompatibilityState compatibilityState)
    {
        if (snapshot_ == nullptr)
        {
            return;
        }

        snapshot_->CompatibilityState = compatibilityState;
        if (eventHandle_ != nullptr)
        {
            SetEvent(eventHandle_);
        }
    }

    void SharedSnapshotWriter::PublishEvent(GameEventType eventType, const char* eventName, std::int32_t levelTime)
    {
        if (snapshot_ == nullptr || eventName == nullptr)
        {
            return;
        }

        std::uint32_t slot = snapshot_->EventCount;
        if (snapshot_->EventCount == MaxEventCount)
        {
            for (std::size_t index = 1; index < MaxEventCount; ++index)
            {
                snapshot_->Events[index - 1] = snapshot_->Events[index];
            }

            slot = static_cast<std::uint32_t>(MaxEventCount - 1);
            ++snapshot_->DroppedEventCount;
        }
        else
        {
            ++snapshot_->EventCount;
        }

        GameEventRecord& record = snapshot_->Events[slot];
        record.EventType = eventType;
        record.LevelTime = levelTime;
        std::memset(record.EventName, 0, sizeof(record.EventName));
        const std::size_t sourceLength = std::strlen(eventName);
        const std::size_t copyLength = std::min(sourceLength, MaxEventNameBytes - 1);
        std::memcpy(record.EventName, eventName, copyLength);

        if (eventHandle_ != nullptr)
        {
            SetEvent(eventHandle_);
        }
    }

    void SharedSnapshotWriter::InitializeSnapshot()
    {
        std::memset(snapshot_, 0, sizeof(SharedSnapshot));
        snapshot_->Magic = SnapshotMagic;
        snapshot_->Version = SnapshotVersion;
        snapshot_->CompatibilityState = GameCompatibilityState::WaitingForMonitor;
    }
}
