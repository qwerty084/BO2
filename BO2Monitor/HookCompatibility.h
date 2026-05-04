#pragma once

#include "SharedSnapshot.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace BO2Monitor
{
    constexpr std::size_t NotifyHookTargetCount = 12;

    struct NotifyHookTargetDefinition
    {
        const char* Name;
        GameEventType EventType;
        bool ReadRoundValue;
    };

    struct ResolvedNotifyHookTarget
    {
        const char* Name;
        GameEventType EventType;
        unsigned int StringValue;
        bool Resolved;
        bool ReadRoundValue;
    };

    struct HookCompatibilityRequest
    {
        bool HookSupportEnabled;
        std::uintptr_t HookTargetAddress;
        const std::uint8_t* ExpectedPrologue;
        std::size_t ExpectedPrologueLength;
    };

    class IHookCompatibilityProbe
    {
    public:
        virtual ~IHookCompatibilityProbe() = default;

        virtual bool IsExecutableAddress(std::uintptr_t address) = 0;
        virtual bool PrologueMatches(
            std::uintptr_t address,
            const std::uint8_t* expected,
            std::size_t expectedLength) = 0;
        virtual bool TryResolveStringId(const char* name, unsigned int& stringValue) = 0;
        virtual bool TryInstallHook(std::uintptr_t address) = 0;
    };

    const std::array<NotifyHookTargetDefinition, NotifyHookTargetCount>& GetNotifyHookTargetDefinitions();
    std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount> CreateUnresolvedNotifyHookTargets();
    bool TryResolveNotifyHookTargets(
        IHookCompatibilityProbe& probe,
        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount>& notifyTargets);
    GameCompatibilityState DetermineHookCompatibility(
        const HookCompatibilityRequest& request,
        IHookCompatibilityProbe& probe,
        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount>& notifyTargets);
}
