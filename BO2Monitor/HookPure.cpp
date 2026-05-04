#include "HookPure.h"
#include "SharedSnapshot.h"

#include <limits>

namespace BO2Monitor
{
    bool IsLikelyZombieWeaponAlias(const char* value)
    {
        if (value == nullptr)
        {
            return false;
        }

        std::size_t length = 0;
        for (; length < MaxWeaponNameBytes && value[length] != '\0'; ++length)
        {
            const char character = value[length];
            const bool allowed = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return length > 3
            && length < MaxWeaponNameBytes
            && value[length - 3] == '_'
            && value[length - 2] == 'z'
            && value[length - 1] == 'm';
    }

    std::uint32_t SaturateCounter(std::uint64_t value)
    {
        constexpr std::uint32_t MaxCounterValue = std::numeric_limits<std::uint32_t>::max();
        return value > MaxCounterValue ? MaxCounterValue : static_cast<std::uint32_t>(value);
    }
}
