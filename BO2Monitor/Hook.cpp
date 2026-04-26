#include "Hook.h"

#include <Windows.h>
#include <cstring>

namespace BO2Monitor
{
    namespace
    {
        constexpr std::uintptr_t PublicT6VmNotifyCandidate = 0x008F3620;
        constexpr std::uintptr_t LocalVmNotifyEntryCandidate = 0x008F31D0;
        constexpr std::array<std::uint8_t, 9> ExpectedLocalVmNotifyEntryPrologue =
        {
            0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x44
        };
        constexpr std::uintptr_t VmNotifyAddress = LocalVmNotifyEntryCandidate;
        constexpr std::array<std::uint8_t, ExpectedLocalVmNotifyEntryPrologue.size()> ExpectedVmNotifyPrologue =
            ExpectedLocalVmNotifyEntryPrologue;
        constexpr std::size_t VmNotifyStolenByteCount = ExpectedVmNotifyPrologue.size();
        constexpr std::uintptr_t PublicT6SlConvertCandidate = 0x00532230;
        constexpr std::uintptr_t SlGetStringOfSizeCandidate = 0x00418B40;
        constexpr std::array<std::uint8_t, 7> ExpectedSlGetStringOfSizePrologue =
        {
            0x83, 0xEC, 0x0C, 0x8B, 0x54, 0x24, 0x10
        };
        constexpr std::uintptr_t RoundAddress = 0x0233FA10;
        constexpr std::uintptr_t PointsAddress = 0x0234C068;
        constexpr std::uintptr_t KillsAddress = 0x0234C080;
        constexpr std::uintptr_t DownsAddress = 0x0234C084;
        constexpr std::uintptr_t ScriptStringPoolStart = 0x02C20000;
        constexpr std::uintptr_t ScriptStringPoolEnd = 0x02C80000;
        constexpr std::uint8_t JumpInstruction = 0xE9;
        constexpr std::size_t MaxObservedNotifyIds = 128;
        constexpr std::size_t MaxResolvedNameBytes = 64;

        using VmNotifyFunction = void(__cdecl*)(std::int32_t, unsigned int, unsigned int, void*);
        using SlGetStringOfSizeFunction = unsigned int(__cdecl*)(const char*, std::int32_t, unsigned int, std::int32_t);

        struct KnownNotifyName
        {
            const char* Name;
            GameEventType EventType;
        };

        struct KnownNotifyId
        {
            const char* Name;
            GameEventType EventType;
            unsigned int StringValue;
        };

        constexpr std::array<KnownNotifyName, 50> KnownNotifyNames =
        {
            KnownNotifyName{ "start_of_round", GameEventType::StartOfRound },
            KnownNotifyName{ "end_of_round", GameEventType::EndOfRound },
            KnownNotifyName{ "powerup_grabbed", GameEventType::PowerUpGrabbed },
            KnownNotifyName{ "dog_round_starting", GameEventType::DogRoundStarting },
            KnownNotifyName{ "power_on", GameEventType::PowerOn },
            KnownNotifyName{ "end_game", GameEventType::EndGame },
            KnownNotifyName{ "perk_bought", GameEventType::PerkBought },
            KnownNotifyName{ "zombie_death", GameEventType::NotifyObserved },
            KnownNotifyName{ "zombie_death_no_headshot", GameEventType::NotifyObserved },
            KnownNotifyName{ "zombie_death_headshot", GameEventType::NotifyObserved },
            KnownNotifyName{ "zombies_multikilled", GameEventType::NotifyObserved },
            KnownNotifyName{ "last_headshot_kill_time", GameEventType::NotifyObserved },
            KnownNotifyName{ "multikill_headshots", GameEventType::NotifyObserved },
            KnownNotifyName{ "zombie_powerup_insta_kill_ug_on", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_insta_kill_upgrade_hud_icon", GameEventType::NotifyObserved },
            KnownNotifyName{ "player_failed_revive", GameEventType::NotifyObserved },
            KnownNotifyName{ "death", GameEventType::NotifyObserved },
            KnownNotifyName{ "deaths", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_anim", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_normal", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_torso", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_neck", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_head", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_melee", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_out", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_in", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_fx", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_high", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_throe_zm", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_self_zm", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_crawl", GameEventType::NotifyObserved },
            KnownNotifyName{ "death_fall", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill", GameEventType::NotifyObserved },
            KnownNotifyName{ "killed", GameEventType::NotifyObserved },
            KnownNotifyName{ "kills", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_time", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_headshots", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_zombies", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_on", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_loop", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_ug", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_ug_on", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_ug_time", GameEventType::NotifyObserved },
            KnownNotifyName{ "kill_over", GameEventType::NotifyObserved },
            KnownNotifyName{ "killed_players", GameEventType::NotifyObserved },
            KnownNotifyName{ "zom_kill", GameEventType::NotifyObserved },
            KnownNotifyName{ "melee_kill", GameEventType::NotifyObserved },
            KnownNotifyName{ "pers_player_zombie_kill", GameEventType::NotifyObserved },
            KnownNotifyName{ "zombie_grenade_death", GameEventType::NotifyObserved },
            KnownNotifyName{ "killed", GameEventType::NotifyObserved }
        };

        SharedSnapshotWriter* activeSnapshotWriter = nullptr;
        VmNotifyFunction originalVmNotify = nullptr;
        unsigned int lastObservedStringValue = 0;
        DWORD lastObservedAt = 0;
        DWORD lastPublishedNotifyAt = 0;
        std::array<unsigned int, MaxObservedNotifyIds> observedStringValues{};
        std::size_t observedStringValueCount = 0;
        std::array<unsigned int, MaxObservedNotifyIds> resolverAttemptedStringValues{};
        std::size_t resolverAttemptedStringValueCount = 0;
        std::array<unsigned int, MaxObservedNotifyIds> resolvedStringValues{};
        std::size_t resolvedStringValueCount = 0;
        std::array<KnownNotifyId, KnownNotifyNames.size()> knownNotifyIds{};
        std::size_t knownNotifyIdCount = 0;

        bool BytesMatch(const std::uint8_t* address, const std::uint8_t* expected, std::size_t expectedLength);
        bool IsExecutableAddress(const void* address);

        bool HasValidatedVmNotifyAddress()
        {
            return VmNotifyAddress != 0;
        }

        bool IsVmNotifyHookEnabled()
        {
#ifdef BO2MONITOR_ENABLE_VM_NOTIFY_HOOK
            return true;
#else
            return false;
#endif
        }

        bool HasObservedStringValue(unsigned int stringValue)
        {
            for (std::size_t index = 0; index < observedStringValueCount; ++index)
            {
                if (observedStringValues[index] == stringValue)
                {
                    return true;
                }
            }

            return false;
        }

        bool RememberStringValue(unsigned int stringValue)
        {
            if (observedStringValueCount >= observedStringValues.size())
            {
                return false;
            }

            observedStringValues[observedStringValueCount] = stringValue;
            ++observedStringValueCount;
            return true;
        }

        bool ContainsValue(const std::array<unsigned int, MaxObservedNotifyIds>& values, std::size_t count, unsigned int value)
        {
            for (std::size_t index = 0; index < count; ++index)
            {
                if (values[index] == value)
                {
                    return true;
                }
            }

            return false;
        }

        bool TryRememberValue(std::array<unsigned int, MaxObservedNotifyIds>& values, std::size_t& count, unsigned int value)
        {
            if (ContainsValue(values, count, value))
            {
                return true;
            }

            if (count >= values.size())
            {
                return false;
            }

            values[count] = value;
            ++count;
            return true;
        }

        const KnownNotifyId* FindKnownNotifyById(unsigned int stringValue)
        {
            for (std::size_t index = 0; index < knownNotifyIdCount; ++index)
            {
                if (knownNotifyIds[index].StringValue == stringValue)
                {
                    return &knownNotifyIds[index];
                }
            }

            return nullptr;
        }

        bool IsScriptNameCharacter(char value)
        {
            return (value >= 'a' && value <= 'z')
                || (value >= '0' && value <= '9')
                || value == '_';
        }

        bool TryReadScriptName(const char* candidate, char (&nameBuffer)[MaxResolvedNameBytes])
        {
            std::size_t length = 0;
            while (length < MaxResolvedNameBytes - 1)
            {
                const char value = candidate[length];
                if (value == '\0')
                {
                    break;
                }

                if (!IsScriptNameCharacter(value))
                {
                    return false;
                }

                nameBuffer[length] = value;
                ++length;
            }

            if (length < 3 || length >= MaxResolvedNameBytes - 1 || candidate[length] != '\0')
            {
                return false;
            }

            nameBuffer[length] = '\0';
            return true;
        }

        bool SlGetStringOfSizePrologueMatches()
        {
            return IsExecutableAddress(reinterpret_cast<const void*>(SlGetStringOfSizeCandidate))
                && BytesMatch(
                    reinterpret_cast<const std::uint8_t*>(SlGetStringOfSizeCandidate),
                    ExpectedSlGetStringOfSizePrologue.data(),
                    ExpectedSlGetStringOfSizePrologue.size());
        }

        bool TryResolveStringId(const char* name, unsigned int& stringValue)
        {
            if (!SlGetStringOfSizePrologueMatches())
            {
                return false;
            }

            const auto resolver = reinterpret_cast<SlGetStringOfSizeFunction>(SlGetStringOfSizeCandidate);
            const auto length = static_cast<unsigned int>(std::strlen(name) + 1);
            unsigned int resolvedValue = 0;
            __try
            {
                resolvedValue = resolver(name, 0, length, 6);
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                return false;
            }

            if (resolvedValue == 0)
            {
                return false;
            }

            stringValue = resolvedValue;
            return true;
        }

        bool TryResolveObservedStringValue(unsigned int stringValue, char (&nameBuffer)[MaxResolvedNameBytes])
        {
            const char* poolStart = reinterpret_cast<const char*>(ScriptStringPoolStart);
            const char* poolEnd = reinterpret_cast<const char*>(ScriptStringPoolEnd);
            for (const char* cursor = poolStart; cursor < poolEnd; ++cursor)
            {
                if (!IsScriptNameCharacter(*cursor))
                {
                    continue;
                }

                if (cursor > poolStart && IsScriptNameCharacter(*(cursor - 1)))
                {
                    continue;
                }

                char candidateName[MaxResolvedNameBytes]{};
                if (!TryReadScriptName(cursor, candidateName))
                {
                    continue;
                }

                unsigned int resolvedValue = 0;
                if (TryResolveStringId(candidateName, resolvedValue) && resolvedValue == stringValue)
                {
                    std::memcpy(nameBuffer, candidateName, MaxResolvedNameBytes);
                    return true;
                }
            }

            return false;
        }

        void ResolveKnownNotifyIds(SharedSnapshotWriter& snapshotWriter)
        {
            knownNotifyIdCount = 0;
            for (const KnownNotifyName& knownName : KnownNotifyNames)
            {
                unsigned int stringValue = 0;
                if (!TryResolveStringId(knownName.Name, stringValue))
                {
                    continue;
                }

                knownNotifyIds[knownNotifyIdCount] = KnownNotifyId
                {
                    knownName.Name,
                    knownName.EventType,
                    stringValue
                };
                ++knownNotifyIdCount;

                snapshotWriter.PublishEvent(
                    knownName.EventType,
                    knownName.Name,
                    static_cast<std::int32_t>(stringValue));
            }
        }

        void __cdecl VmNotifyDetour(
            std::int32_t inst,
            unsigned int notifyListOwnerId,
            unsigned int stringValue,
            void* top)
        {
            originalVmNotify(inst, notifyListOwnerId, stringValue, top);

            if (activeSnapshotWriter != nullptr)
            {
                const DWORD now = GetTickCount();
                if (HasObservedStringValue(stringValue))
                {
                    return;
                }

                if (now - lastPublishedNotifyAt < 250)
                {
                    return;
                }

                if (stringValue == lastObservedStringValue && now - lastObservedAt < 1000)
                {
                    return;
                }

                if (!RememberStringValue(stringValue))
                {
                    return;
                }

                lastObservedStringValue = stringValue;
                lastObservedAt = now;
                lastPublishedNotifyAt = now;
                if (const KnownNotifyId* knownNotify = FindKnownNotifyById(stringValue))
                {
                    activeSnapshotWriter->PublishEvent(
                        knownNotify->EventType,
                        knownNotify->Name,
                        static_cast<std::int32_t>(stringValue));
                    return;
                }

                activeSnapshotWriter->PublishEvent(
                    GameEventType::NotifyObserved,
                    "vm_notify_observed",
                    static_cast<std::int32_t>(stringValue));
            }
        }

        bool BytesMatch(const std::uint8_t* address, const std::uint8_t* expected, std::size_t expectedLength)
        {
            for (std::size_t index = 0; index < expectedLength; ++index)
            {
                if (address[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        bool IsExecutableAddress(const void* address)
        {
            MEMORY_BASIC_INFORMATION memoryInfo{};
            if (VirtualQuery(address, &memoryInfo, sizeof(memoryInfo)) == 0)
            {
                return false;
            }

            const DWORD protection = memoryInfo.Protect & 0xff;
            return protection == PAGE_EXECUTE
                || protection == PAGE_EXECUTE_READ
                || protection == PAGE_EXECUTE_READWRITE
                || protection == PAGE_EXECUTE_WRITECOPY;
        }

        bool PrologueMatches(const std::uint8_t* address)
        {
            if constexpr (ExpectedVmNotifyPrologue.empty())
            {
                return false;
            }
            else
            {
                for (std::size_t index = 0; index < ExpectedVmNotifyPrologue.size(); ++index)
                {
                    if (address[index] != ExpectedVmNotifyPrologue[index])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        bool LocalVmNotifyEntryPrologueMatches()
        {
            return IsExecutableAddress(reinterpret_cast<const void*>(LocalVmNotifyEntryCandidate))
                && BytesMatch(
                    reinterpret_cast<const std::uint8_t*>(LocalVmNotifyEntryCandidate),
                    ExpectedLocalVmNotifyEntryPrologue.data(),
                    ExpectedLocalVmNotifyEntryPrologue.size());
        }

        bool WriteJump(void* source, const void* destination, std::size_t patchLength)
        {
            if (patchLength < 5)
            {
                return false;
            }

            DWORD oldProtect = 0;
            if (!VirtualProtect(source, patchLength, PAGE_EXECUTE_READWRITE, &oldProtect))
            {
                return false;
            }

            auto* patch = static_cast<std::uint8_t*>(source);
            const auto relativeOffset = reinterpret_cast<std::intptr_t>(destination)
                - reinterpret_cast<std::intptr_t>(source)
                - 5;

            patch[0] = JumpInstruction;
            *reinterpret_cast<std::int32_t*>(patch + 1) = static_cast<std::int32_t>(relativeOffset);
            for (std::size_t index = 5; index < patchLength; ++index)
            {
                patch[index] = 0x90;
            }

            FlushInstructionCache(GetCurrentProcess(), source, patchLength);
            DWORD unusedProtect = 0;
            VirtualProtect(source, patchLength, oldProtect, &unusedProtect);
            return true;
        }

        VmNotifyFunction CreateTrampoline(const std::uint8_t* targetAddress)
        {
            constexpr std::size_t TrampolineJumpSize = 5;
            const std::size_t trampolineSize = VmNotifyStolenByteCount + TrampolineJumpSize;
            auto* trampoline = static_cast<std::uint8_t*>(VirtualAlloc(
                nullptr,
                trampolineSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_EXECUTE_READWRITE));
            if (trampoline == nullptr)
            {
                return nullptr;
            }

            std::memcpy(trampoline, targetAddress, VmNotifyStolenByteCount);
            if (!WriteJump(
                trampoline + VmNotifyStolenByteCount,
                targetAddress + VmNotifyStolenByteCount,
                TrampolineJumpSize))
            {
                VirtualFree(trampoline, 0, MEM_RELEASE);
                return nullptr;
            }

            return reinterpret_cast<VmNotifyFunction>(trampoline);
        }

        bool InstallVmNotifyHook()
        {
            auto* targetAddress = reinterpret_cast<std::uint8_t*>(VmNotifyAddress);
            originalVmNotify = CreateTrampoline(targetAddress);
            if (originalVmNotify == nullptr)
            {
                return false;
            }

            if (!WriteJump(targetAddress, reinterpret_cast<const void*>(&VmNotifyDetour), VmNotifyStolenByteCount))
            {
                originalVmNotify = nullptr;
                return false;
            }

            return true;
        }

        bool CanReadAddress(const void* address)
        {
            MEMORY_BASIC_INFORMATION memoryInfo{};
            if (VirtualQuery(address, &memoryInfo, sizeof(memoryInfo)) == 0)
            {
                return false;
            }

            const DWORD protection = memoryInfo.Protect & 0xff;
            return protection == PAGE_READONLY
                || protection == PAGE_READWRITE
                || protection == PAGE_WRITECOPY
                || protection == PAGE_EXECUTE_READ
                || protection == PAGE_EXECUTE_READWRITE
                || protection == PAGE_EXECUTE_WRITECOPY;
        }

        bool ArePollingAddressesReadable()
        {
            return CanReadAddress(reinterpret_cast<const void*>(RoundAddress))
                && CanReadAddress(reinterpret_cast<const void*>(PointsAddress))
                && CanReadAddress(reinterpret_cast<const void*>(KillsAddress))
                && CanReadAddress(reinterpret_cast<const void*>(DownsAddress));
        }

        void PublishIfChanged(
            SharedSnapshotWriter& snapshotWriter,
            volatile std::int32_t* valueAddress,
            std::int32_t& previousValue,
            GameEventType eventType,
            const char* eventName,
            bool onlyIncreasing,
            std::int32_t minValue,
            std::int32_t maxValue)
        {
            const std::int32_t currentValue = *valueAddress;
            if (currentValue < minValue || currentValue > maxValue)
            {
                return;
            }

            if (onlyIncreasing && currentValue <= previousValue)
            {
                return;
            }

            if (!onlyIncreasing && currentValue == previousValue)
            {
                return;
            }

            previousValue = currentValue;
            snapshotWriter.PublishEvent(eventType, eventName, currentValue);
        }

        void PublishDiscoveryEvidence(SharedSnapshotWriter& snapshotWriter)
        {
            // Public T6 source hooks 0x008F3620, but this Steam build decodes that
            // address as the middle of a call immediate, so reject it locally.
            snapshotWriter.PublishEvent(
                GameEventType::NotifyCandidateRejected,
                "vm_notify_candidate_rejected",
                static_cast<std::int32_t>(PublicT6VmNotifyCandidate));

            if (LocalVmNotifyEntryPrologueMatches())
            {
                snapshotWriter.PublishEvent(
                    GameEventType::NotifyEntryCandidate,
                    "vm_notify_entry_candidate",
                    static_cast<std::int32_t>(LocalVmNotifyEntryCandidate));
            }

            if (IsExecutableAddress(reinterpret_cast<const void*>(PublicT6SlConvertCandidate)))
            {
                snapshotWriter.PublishEvent(
                    GameEventType::StringResolverCandidate,
                    "sl_convert_candidate",
                    static_cast<std::int32_t>(PublicT6SlConvertCandidate));
            }

            if (SlGetStringOfSizePrologueMatches())
            {
                snapshotWriter.PublishEvent(
                    GameEventType::StringResolverCandidate,
                    "sl_get_string_of_size_candidate",
                    static_cast<std::int32_t>(SlGetStringOfSizeCandidate));
            }
        }
    }

    GameEventType MapNotifyName(const char* notifyName)
    {
        if (notifyName == nullptr)
        {
            return GameEventType::Unknown;
        }

        if (std::strcmp(notifyName, "start_of_round") == 0)
        {
            return GameEventType::StartOfRound;
        }

        if (std::strcmp(notifyName, "end_of_round") == 0)
        {
            return GameEventType::EndOfRound;
        }

        if (std::strcmp(notifyName, "powerup_grabbed") == 0)
        {
            return GameEventType::PowerUpGrabbed;
        }

        if (std::strcmp(notifyName, "dog_round_starting") == 0)
        {
            return GameEventType::DogRoundStarting;
        }

        if (std::strcmp(notifyName, "power_on") == 0)
        {
            return GameEventType::PowerOn;
        }

        if (std::strcmp(notifyName, "end_game") == 0)
        {
            return GameEventType::EndGame;
        }

        if (std::strcmp(notifyName, "perk_bought") == 0)
        {
            return GameEventType::PerkBought;
        }

        return GameEventType::Unknown;
    }

    GameCompatibilityState TryInstallNotifyHook(SharedSnapshotWriter& snapshotWriter)
    {
        PublishDiscoveryEvidence(snapshotWriter);

        if (!HasValidatedVmNotifyAddress())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        const auto* targetAddress = reinterpret_cast<const std::uint8_t*>(VmNotifyAddress);
        if (!IsExecutableAddress(targetAddress) || !PrologueMatches(targetAddress))
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        if (!IsVmNotifyHookEnabled())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        // Resolve known names up front; unknown notifies still publish raw IDs
        // so discovery can continue without risking arbitrary string conversion.
        ResolveKnownNotifyIds(snapshotWriter);
        activeSnapshotWriter = &snapshotWriter;
        if (!InstallVmNotifyHook())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        snapshotWriter.SetCompatibility(GameCompatibilityState::Compatible);
        return GameCompatibilityState::Compatible;
    }

    void ResolveObservedNotifyNames(SharedSnapshotWriter& snapshotWriter)
    {
        if (!SlGetStringOfSizePrologueMatches())
        {
            return;
        }

        const std::size_t observedCount = observedStringValueCount;
        for (std::size_t index = 0; index < observedCount; ++index)
        {
            const unsigned int stringValue = observedStringValues[index];
            if (FindKnownNotifyById(stringValue) != nullptr
                || ContainsValue(resolvedStringValues, resolvedStringValueCount, stringValue)
                || ContainsValue(resolverAttemptedStringValues, resolverAttemptedStringValueCount, stringValue))
            {
                continue;
            }

            TryRememberValue(resolverAttemptedStringValues, resolverAttemptedStringValueCount, stringValue);

            char resolvedName[MaxResolvedNameBytes]{};
            if (!TryResolveObservedStringValue(stringValue, resolvedName))
            {
                continue;
            }

            TryRememberValue(resolvedStringValues, resolvedStringValueCount, stringValue);
            snapshotWriter.PublishEvent(
                MapNotifyName(resolvedName),
                resolvedName,
                static_cast<std::int32_t>(stringValue));
            return;
        }
    }

    void RunPollingFallback(SharedSnapshotWriter& snapshotWriter)
    {
        if (!ArePollingAddressesReadable())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return;
        }

        PublishDiscoveryEvidence(snapshotWriter);
        snapshotWriter.SetCompatibility(GameCompatibilityState::PollingFallback);
        auto* roundValue = reinterpret_cast<volatile std::int32_t*>(RoundAddress);
        auto* pointsValue = reinterpret_cast<volatile std::int32_t*>(PointsAddress);
        auto* killsValue = reinterpret_cast<volatile std::int32_t*>(KillsAddress);
        auto* downsValue = reinterpret_cast<volatile std::int32_t*>(DownsAddress);
        std::int32_t previousRound = *roundValue;
        std::int32_t previousPoints = *pointsValue;
        std::int32_t previousKills = *killsValue;
        std::int32_t previousDowns = *downsValue;

        while (true)
        {
            Sleep(250);
            PublishIfChanged(snapshotWriter, roundValue, previousRound, GameEventType::RoundChanged, "round_changed", true, 2, 255);
            PublishIfChanged(snapshotWriter, pointsValue, previousPoints, GameEventType::PointsChanged, "points_changed", false, 0, 2000000);
            PublishIfChanged(snapshotWriter, killsValue, previousKills, GameEventType::KillsChanged, "kills_changed", true, 0, 100000);
            PublishIfChanged(snapshotWriter, downsValue, previousDowns, GameEventType::DownsChanged, "downs_changed", true, 0, 1000);
        }
    }
}
