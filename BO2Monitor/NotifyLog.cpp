#include "NotifyLog.h"

#include <array>
#include <atomic>
#include <mutex>

namespace BO2Monitor
{
    namespace
    {
        constexpr std::size_t NotifyEventQueueCapacity = 256;

        struct NotifyQueueSlot
        {
            RawNotifyRecord Record{};
            std::atomic<std::uint64_t> PublishedSequence{ 0 };
        };

        std::array<NotifyQueueSlot, NotifyEventQueueCapacity> notifyQueue{};
        std::mutex notifyQueueMutex;
        std::atomic<std::uint64_t> nextWriteSequence{ 0 };
        std::atomic<std::uint64_t> droppedNotifyEventCount{ 0 };
        std::uint64_t nextReadSequence = 0;
    }

    void ResetNotifyEventQueue()
    {
        std::lock_guard<std::mutex> lock(notifyQueueMutex);
        nextWriteSequence.store(0, std::memory_order_relaxed);
        droppedNotifyEventCount.store(0, std::memory_order_relaxed);
        nextReadSequence = 0;

        for (NotifyQueueSlot& slot : notifyQueue)
        {
            slot.Record = RawNotifyRecord{};
            slot.PublishedSequence.store(0, std::memory_order_relaxed);
        }
    }

    void EnqueueMatchedNotify(
        std::int32_t inst,
        unsigned int ownerId,
        unsigned int stringValue,
        void* top,
        GameEventType eventType,
        const char* eventName,
        bool readRoundValue)
    {
        std::unique_lock<std::mutex> lock(notifyQueueMutex, std::try_to_lock);
        if (!lock.owns_lock())
        {
            droppedNotifyEventCount.fetch_add(1, std::memory_order_relaxed);
            return;
        }

        const std::uint64_t sequence = nextWriteSequence.fetch_add(1, std::memory_order_relaxed);
        NotifyQueueSlot& slot = notifyQueue[sequence % notifyQueue.size()];
        slot.PublishedSequence.store(0, std::memory_order_release);
        slot.Record = RawNotifyRecord
        {
            sequence,
            GetTickCount(),
            inst,
            ownerId,
            stringValue,
            reinterpret_cast<std::uintptr_t>(top),
            eventType,
            eventName,
            readRoundValue
        };
        slot.PublishedSequence.store(sequence + 1, std::memory_order_release);
    }

    bool TryDequeueMatchedNotify(RawNotifyRecord& record, std::uint64_t& droppedSinceLastDrain)
    {
        droppedSinceLastDrain = 0;
        std::unique_lock<std::mutex> lock(notifyQueueMutex, std::try_to_lock);
        if (!lock.owns_lock())
        {
            return false;
        }

        NotifyQueueSlot& slot = notifyQueue[nextReadSequence % notifyQueue.size()];
        const std::uint64_t expectedPublishedSequence = nextReadSequence + 1;
        const std::uint64_t publishedSequence = slot.PublishedSequence.load(std::memory_order_acquire);
        if (publishedSequence < expectedPublishedSequence)
        {
            return false;
        }

        if (publishedSequence > expectedPublishedSequence)
        {
            droppedSinceLastDrain = publishedSequence - expectedPublishedSequence;
            droppedNotifyEventCount.fetch_add(droppedSinceLastDrain, std::memory_order_relaxed);
            nextReadSequence = publishedSequence - 1;
        }

        record = slot.Record;
        if (slot.PublishedSequence.load(std::memory_order_acquire) != publishedSequence)
        {
            ++droppedSinceLastDrain;
            droppedNotifyEventCount.fetch_add(1, std::memory_order_relaxed);
            nextReadSequence = slot.PublishedSequence.load(std::memory_order_acquire) - 1;
            return false;
        }

        ++nextReadSequence;
        return true;
    }

    std::uint64_t GetDroppedNotifyEventCount()
    {
        return droppedNotifyEventCount.load(std::memory_order_relaxed);
    }
}
