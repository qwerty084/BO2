#include "NotifyPublication.h"

#include "HookPure.h"

#include <cstring>

namespace BO2Monitor
{
    void PublishMatchedNotify(
        INotifyPublicationWriter& writer,
        INotifyRoundReader& roundReader,
        const RawNotifyRecord& record)
    {
        std::int32_t eventValue = static_cast<std::int32_t>(record.StringValue);
        if (record.ReadRoundValue)
        {
            std::int32_t roundValue = 0;
            if (roundReader.TryReadRoundValue(roundValue))
            {
                eventValue = roundValue;
            }
        }

        char weaponName[MaxWeaponNameBytes]{};
        const char* weaponNameToPublish = nullptr;
        if (record.WeaponName[0] != '\0' && IsLikelyZombieWeaponAlias(record.WeaponName))
        {
            std::memcpy(weaponName, record.WeaponName, MaxWeaponNameBytes);
            weaponName[MaxWeaponNameBytes - 1] = '\0';
            weaponNameToPublish = weaponName;
        }

        writer.PublishEvent(
            record.EventType,
            record.EventName,
            eventValue,
            record.OwnerId,
            record.StringValue,
            record.Tick,
            weaponNameToPublish);
    }

    void PublishNotifyEventCounters(
        INotifyPublicationWriter& writer,
        std::uint64_t droppedNotifyCount,
        std::uint64_t publishedNotifyCount)
    {
        writer.SetNotifyEventCounters(
            SaturateCounter(droppedNotifyCount),
            SaturateCounter(publishedNotifyCount));
    }
}
