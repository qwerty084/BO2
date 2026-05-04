#include "PollingFallback.h"

#include <array>
#include <cstddef>

namespace BO2Monitor
{
    namespace
    {
        struct PollingFallbackStatDescriptor
        {
            PollingFallbackStat Stat;
            GameEventType EventType;
            const char* EventName;
            bool OnlyIncreasing;
            std::int32_t MinimumValue;
            std::int32_t MaximumValue;
        };

        constexpr std::array<PollingFallbackStatDescriptor, 4> StatDescriptors =
        {
            PollingFallbackStatDescriptor{ PollingFallbackStat::Round, GameEventType::RoundChanged, "round_changed", true, 2, 255 },
            PollingFallbackStatDescriptor{ PollingFallbackStat::Points, GameEventType::PointsChanged, "points_changed", false, 0, 2000000 },
            PollingFallbackStatDescriptor{ PollingFallbackStat::Kills, GameEventType::KillsChanged, "kills_changed", true, 0, 100000 },
            PollingFallbackStatDescriptor{ PollingFallbackStat::Downs, GameEventType::DownsChanged, "downs_changed", true, 0, 1000 }
        };

        std::size_t StatIndex(PollingFallbackStat stat)
        {
            return static_cast<std::size_t>(stat);
        }

        bool IsValidPublishedValue(
            const PollingFallbackStatDescriptor& descriptor,
            std::int32_t value)
        {
            return value >= descriptor.MinimumValue && value <= descriptor.MaximumValue;
        }

        bool ShouldPublishValue(
            const PollingFallbackStatDescriptor& descriptor,
            std::int32_t previousValue,
            std::int32_t currentValue)
        {
            if (!IsValidPublishedValue(descriptor, currentValue))
            {
                return false;
            }

            if (descriptor.OnlyIncreasing)
            {
                return currentValue > previousValue;
            }

            return currentValue != previousValue;
        }
    }

    GameCompatibilityState PollingFallbackState::Initialize(IPollingFallbackReader& reader)
    {
        initialized_ = false;
        previousValues_ = {};

        for (const PollingFallbackStatDescriptor& descriptor : StatDescriptors)
        {
            std::int32_t value = 0;
            if (!reader.TryReadStat(descriptor.Stat, value))
            {
                return GameCompatibilityState::UnsupportedVersion;
            }

            previousValues_[StatIndex(descriptor.Stat)] = value;
        }

        initialized_ = true;
        return GameCompatibilityState::PollingFallback;
    }

    bool PollingFallbackState::IsInitialized() const noexcept
    {
        return initialized_;
    }

    void PollingFallbackState::PublishChanges(
        IPollingFallbackReader& reader,
        INotifyPublicationWriter& writer)
    {
        if (!initialized_)
        {
            return;
        }

        for (const PollingFallbackStatDescriptor& descriptor : StatDescriptors)
        {
            std::int32_t currentValue = 0;
            if (!reader.TryReadStat(descriptor.Stat, currentValue))
            {
                continue;
            }

            const std::size_t statIndex = StatIndex(descriptor.Stat);
            if (!ShouldPublishValue(descriptor, previousValues_[statIndex], currentValue))
            {
                continue;
            }

            previousValues_[statIndex] = currentValue;
            writer.PublishEvent(
                descriptor.EventType,
                descriptor.EventName,
                currentValue,
                0,
                0,
                0,
                nullptr);
        }
    }
}
