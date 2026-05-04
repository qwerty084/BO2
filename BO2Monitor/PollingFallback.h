#pragma once

#include "NotifyPublication.h"

#include <array>
#include <cstdint>

namespace BO2Monitor
{
    enum class PollingFallbackStat : std::uint8_t
    {
        Round = 0,
        Points = 1,
        Kills = 2,
        Downs = 3
    };

    class IPollingFallbackReader
    {
    public:
        virtual ~IPollingFallbackReader() = default;

        virtual bool TryReadStat(PollingFallbackStat stat, std::int32_t& value) = 0;
    };

    class PollingFallbackState
    {
    public:
        GameCompatibilityState Initialize(IPollingFallbackReader& reader);
        bool IsInitialized() const noexcept;
        void PublishChanges(IPollingFallbackReader& reader, INotifyPublicationWriter& writer);

    private:
        std::array<std::int32_t, 4> previousValues_{};
        bool initialized_ = false;
    };
}
