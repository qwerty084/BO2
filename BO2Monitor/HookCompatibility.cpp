#include "HookCompatibility.h"

namespace BO2Monitor
{
    namespace
    {
        constexpr std::array<NotifyHookTargetDefinition, NotifyHookTargetCount> NotifyHookTargetDefinitions =
        {
            NotifyHookTargetDefinition{ "start_of_round", GameEventType::StartOfRound, true },
            NotifyHookTargetDefinition{ "end_of_round", GameEventType::EndOfRound, true },
            NotifyHookTargetDefinition{ "end_game", GameEventType::EndGame, false },
            NotifyHookTargetDefinition{ "randomization_done", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "user_grabbed_weapon", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "chest_accessed", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "box_moving", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "weapon_fly_away_start", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "weapon_fly_away_end", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "arrived", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "left", GameEventType::BoxEvent, false },
            NotifyHookTargetDefinition{ "closed", GameEventType::BoxEvent, false }
        };
    }

    const std::array<NotifyHookTargetDefinition, NotifyHookTargetCount>& GetNotifyHookTargetDefinitions()
    {
        return NotifyHookTargetDefinitions;
    }

    std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount> CreateUnresolvedNotifyHookTargets()
    {
        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount> targets{};
        for (std::size_t index = 0; index < NotifyHookTargetDefinitions.size(); ++index)
        {
            targets[index] = ResolvedNotifyHookTarget
            {
                NotifyHookTargetDefinitions[index].Name,
                NotifyHookTargetDefinitions[index].EventType,
                0,
                false,
                NotifyHookTargetDefinitions[index].ReadRoundValue
            };
        }

        return targets;
    }

    bool TryResolveNotifyHookTargets(
        IHookCompatibilityProbe& probe,
        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount>& notifyTargets)
    {
        notifyTargets = CreateUnresolvedNotifyHookTargets();

        for (ResolvedNotifyHookTarget& target : notifyTargets)
        {
            unsigned int stringValue = 0;
            if (!probe.TryResolveStringId(target.Name, stringValue) || stringValue == 0)
            {
                notifyTargets = CreateUnresolvedNotifyHookTargets();
                return false;
            }

            target.StringValue = stringValue;
            target.Resolved = true;
        }

        return true;
    }

    GameCompatibilityState DetermineHookCompatibility(
        const HookCompatibilityRequest& request,
        IHookCompatibilityProbe& probe,
        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount>& notifyTargets)
    {
        notifyTargets = CreateUnresolvedNotifyHookTargets();

        if (request.HookTargetAddress == 0
            || request.ExpectedPrologue == nullptr
            || request.ExpectedPrologueLength == 0)
        {
            return GameCompatibilityState::UnsupportedVersion;
        }

        if (!probe.IsExecutableAddress(request.HookTargetAddress)
            || !probe.PrologueMatches(
                request.HookTargetAddress,
                request.ExpectedPrologue,
                request.ExpectedPrologueLength))
        {
            return GameCompatibilityState::UnsupportedVersion;
        }

        if (!request.HookSupportEnabled)
        {
            return GameCompatibilityState::CaptureDisabled;
        }

        if (!TryResolveNotifyHookTargets(probe, notifyTargets))
        {
            return GameCompatibilityState::UnsupportedVersion;
        }

        if (!probe.TryInstallHook(request.HookTargetAddress))
        {
            return GameCompatibilityState::UnsupportedVersion;
        }

        return GameCompatibilityState::Compatible;
    }
}
