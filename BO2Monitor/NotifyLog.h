#pragma once

#include "SharedSnapshot.h"

#include <Windows.h>
#include <cstdint>

namespace BO2Monitor
{
    struct RawNotifyRecord
    {
        std::uint64_t Seq;
        DWORD Tick;
        std::int32_t Inst;
        unsigned int OwnerId;
        unsigned int StringValue;
        std::uintptr_t Top;
        GameEventType EventType;
        const char* EventName;
        char WeaponName[MaxWeaponNameBytes];
        bool ReadRoundValue;
    };

    void ResetNotifyEventQueue();
    void EnqueueMatchedNotify(
        std::int32_t inst,
        unsigned int ownerId,
        unsigned int stringValue,
        void* top,
        GameEventType eventType,
        const char* eventName,
        const char* weaponName,
        bool readRoundValue);
    bool TryDequeueMatchedNotify(RawNotifyRecord& record, std::uint64_t& droppedSinceLastDrain);
    std::uint64_t GetDroppedNotifyEventCount();
}
