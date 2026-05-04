#pragma once

#include "NotifyLog.h"

#include <cstdint>

namespace BO2Monitor
{
    class INotifyPublicationWriter
    {
    public:
        virtual ~INotifyPublicationWriter() = default;

        virtual void PublishEvent(
            GameEventType eventType,
            const char* eventName,
            std::int32_t levelTime,
            std::uint32_t ownerId,
            std::uint32_t stringValue,
            std::uint32_t tick,
            const char* weaponName) = 0;

        virtual void SetNotifyEventCounters(
            std::uint32_t droppedNotifyCount,
            std::uint32_t publishedNotifyCount) = 0;
    };

    class INotifyRoundReader
    {
    public:
        virtual ~INotifyRoundReader() = default;

        virtual bool TryReadRoundValue(std::int32_t& roundValue) = 0;
    };

    void PublishMatchedNotify(
        INotifyPublicationWriter& writer,
        INotifyRoundReader& roundReader,
        const RawNotifyRecord& record);

    void PublishNotifyEventCounters(
        INotifyPublicationWriter& writer,
        std::uint64_t droppedNotifyCount,
        std::uint64_t publishedNotifyCount);
}
