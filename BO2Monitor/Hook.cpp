#include "Hook.h"
#include "NotifyLog.h"
#include "MinHook.h"

#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstring>
#include <limits>

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

        using VmNotifyFunction = void(__cdecl*)(std::int32_t, unsigned int, unsigned int, void*);
        using SlGetStringOfSizeFunction = unsigned int(__cdecl*)(const char*, std::int32_t, unsigned int, std::int32_t);

        struct ProductionNotifyTarget
        {
            const char* Name;
            GameEventType EventType;
            unsigned int StringValue;
            bool Resolved;
            bool ReadRoundValue;
        };

        std::array<ProductionNotifyTarget, 13> productionNotifyTargets =
        {
            ProductionNotifyTarget{ "start_of_round", GameEventType::StartOfRound, 0, false, true },
            ProductionNotifyTarget{ "end_of_round", GameEventType::EndOfRound, 0, false, true },
            ProductionNotifyTarget{ "end_game", GameEventType::EndGame, 0, false, false },
            ProductionNotifyTarget{ "randomization_done", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "user_grabbed_weapon", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "chest_accessed", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "box_moving", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "weapon_fly_away_start", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "weapon_fly_away_end", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "arrived", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "left", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "opened", GameEventType::BoxEvent, 0, false, false },
            ProductionNotifyTarget{ "closed", GameEventType::BoxEvent, 0, false, false }
        };

        VmNotifyFunction originalVmNotify = nullptr;
        bool minHookInitialized = false;
        bool vmNotifyHookCreated = false;

        bool BytesMatch(const std::uint8_t* address, const std::uint8_t* expected, std::size_t expectedLength);
        bool IsExecutableAddress(const void* address);
        bool CanReadAddress(const void* address);

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

        const ProductionNotifyTarget* FindProductionNotifyTarget(unsigned int stringValue)
        {
            for (const ProductionNotifyTarget& target : productionNotifyTargets)
            {
                if (target.Resolved && target.StringValue == stringValue)
                {
                    return &target;
                }
            }

            return nullptr;
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

        bool ResolveProductionNotifyTargets()
        {
            bool resolvedAnyTarget = false;
            for (ProductionNotifyTarget& target : productionNotifyTargets)
            {
                target.StringValue = 0;
                target.Resolved = false;

                unsigned int stringValue = 0;
                if (!TryResolveStringId(target.Name, stringValue))
                {
                    continue;
                }

                target.StringValue = stringValue;
                target.Resolved = true;
                resolvedAnyTarget = true;
            }

            return resolvedAnyTarget;
        }

        std::uint32_t SaturateCounter(std::uint64_t value)
        {
            constexpr std::uint32_t MaxCounterValue = std::numeric_limits<std::uint32_t>::max();
            return value > MaxCounterValue ? MaxCounterValue : static_cast<std::uint32_t>(value);
        }

        bool TryReadRoundValue(std::int32_t& roundValue)
        {
            auto* roundAddress = reinterpret_cast<volatile std::int32_t*>(RoundAddress);
            if (!CanReadAddress(reinterpret_cast<const void*>(RoundAddress)))
            {
                return false;
            }

            const std::int32_t currentValue = *roundAddress;
            if (currentValue < 1 || currentValue > 255)
            {
                return false;
            }

            roundValue = currentValue;
            return true;
        }

        void PublishMatchedNotify(
            SharedSnapshotWriter& snapshotWriter,
            const RawNotifyRecord& record)
        {
            std::int32_t eventValue = static_cast<std::int32_t>(record.StringValue);
            if (record.ReadRoundValue)
            {
                std::int32_t roundValue = 0;
                if (TryReadRoundValue(roundValue))
                {
                    eventValue = roundValue;
                }
            }

            snapshotWriter.PublishEvent(
                record.EventType,
                record.EventName,
                eventValue,
                record.OwnerId,
                record.StringValue);
        }

        void __cdecl VmNotifyDetour(
            std::int32_t inst,
            unsigned int notifyListOwnerId,
            unsigned int stringValue,
            void* top)
        {
            originalVmNotify(inst, notifyListOwnerId, stringValue, top);

            const ProductionNotifyTarget* target = FindProductionNotifyTarget(stringValue);
            if (target == nullptr)
            {
                return;
            }

            EnqueueMatchedNotify(
                inst,
                notifyListOwnerId,
                stringValue,
                top,
                target->EventType,
                target->Name,
                target->ReadRoundValue);
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

        bool InstallVmNotifyHook()
        {
            auto* targetAddress = reinterpret_cast<std::uint8_t*>(VmNotifyAddress);
            MH_STATUS initializeStatus = MH_Initialize();
            if (initializeStatus != MH_OK && initializeStatus != MH_ERROR_ALREADY_INITIALIZED)
            {
                return false;
            }

            minHookInitialized = true;
            MH_STATUS createStatus = MH_CreateHook(
                targetAddress,
                reinterpret_cast<LPVOID>(&VmNotifyDetour),
                reinterpret_cast<LPVOID*>(&originalVmNotify));
            if (createStatus != MH_OK)
            {
                return false;
            }

            vmNotifyHookCreated = true;
            MH_STATUS enableStatus = MH_EnableHook(targetAddress);
            if (enableStatus != MH_OK)
            {
                MH_RemoveHook(targetAddress);
                vmNotifyHookCreated = false;
                originalVmNotify = nullptr;
                return false;
            }

            return true;
        }

        [[maybe_unused]] void UninstallVmNotifyHook()
        {
            if (vmNotifyHookCreated)
            {
                auto* targetAddress = reinterpret_cast<std::uint8_t*>(VmNotifyAddress);
                MH_DisableHook(targetAddress);
                MH_RemoveHook(targetAddress);
                vmNotifyHookCreated = false;
                originalVmNotify = nullptr;
            }

            if (minHookInitialized)
            {
                MH_Uninitialize();
                minHookInitialized = false;
            }
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
            snapshotWriter.SetCompatibility(GameCompatibilityState::CaptureDisabled);
            return GameCompatibilityState::CaptureDisabled;
        }

        ResetNotifyEventQueue();
        if (!ResolveProductionNotifyTargets())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        if (!InstallVmNotifyHook())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            return GameCompatibilityState::UnsupportedVersion;
        }

        snapshotWriter.SetCompatibility(GameCompatibilityState::Compatible);
        return GameCompatibilityState::Compatible;
    }

    void RunNotifyEventWorker(SharedSnapshotWriter& snapshotWriter)
    {
        std::uint64_t publishedNotifyCount = 0;

        while (true)
        {
            std::uint32_t processedCount = 0;
            for (; processedCount < 64; ++processedCount)
            {
                RawNotifyRecord record{};
                std::uint64_t droppedSinceLastDrain = 0;
                if (!TryDequeueMatchedNotify(record, droppedSinceLastDrain))
                {
                    break;
                }

                PublishMatchedNotify(snapshotWriter, record);
                ++publishedNotifyCount;
            }

            snapshotWriter.SetNotifyEventCounters(
                SaturateCounter(GetDroppedNotifyEventCount()),
                SaturateCounter(publishedNotifyCount));

            if (processedCount == 0)
            {
                Sleep(100);
            }
            else
            {
                Sleep(0);
            }
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
