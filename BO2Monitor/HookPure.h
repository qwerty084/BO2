#pragma once

#include <cstdint>

namespace BO2Monitor
{
    bool IsLikelyZombieWeaponAlias(const char* value);
    std::uint32_t SaturateCounter(std::uint64_t value);
}
