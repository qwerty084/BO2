#include "Hook.h"
#include "HookCompatibility.h"
#include "HookPure.h"
#include "NotifyLog.h"
#include "NotifyPublication.h"
#include "PollingFallback.h"
#include "MinHook.h"

#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstddef>
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
        constexpr std::uintptr_t SlGetStringOfSizeCandidate = 0x00418B40;
        constexpr std::uintptr_t ScriptStringDataPointer = 0x02BF83A4;
        constexpr std::size_t MaxScriptObjectVariableCount = 16384;
        constexpr std::size_t MaxScriptChildVariableCount = 0x20000;
        constexpr std::size_t MaxScriptStringCount = 0x40000;
        constexpr std::size_t ScriptStringDataStride = 0x18;
        constexpr std::size_t ScriptStringTextOffset = 4;
        constexpr std::uintptr_t ChildVariablesPointerSlotBase = 0x02DEFB80;
        constexpr std::uintptr_t ScriptInstancePointerStride = 0x200;
        constexpr std::uintptr_t MinimumUserModePointer = 0x00100000;
        constexpr int MaxScriptInstanceCount = 2;
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

        enum ScriptValueType : std::uint8_t
        {
            ScriptString = 2,
            ScriptIString = 3
        };

        union VariableUnion
        {
            std::int32_t IntValue;
            std::uint32_t UIntValue;
            float FloatValue;
            std::uint32_t StringValue;
            const float* VectorValue;
            const char* CodePosValue;
            std::uint32_t PointerValue;
            void* StackValue;
            std::uint32_t EntityOffset;
        };

        union ChildBucketMatchKeys
        {
            struct
            {
                std::uint16_t NameHi;
                std::uint16_t ParentId;
            } Keys;
            std::uint32_t Match;
        };

        struct ChildVariableValue
        {
            VariableUnion Value;
            std::uint32_t SiblingOrHash;
            std::uint32_t Next;
            std::uint8_t Type;
            std::uint8_t NameLo;
            std::uint16_t Flags;
            ChildBucketMatchKeys Key;
            std::uint32_t NextSibling;
            std::uint32_t PrevSibling;
        };

        static_assert(sizeof(VariableUnion) == 4);
        static_assert(sizeof(ChildVariableValue) == 0x1C);

        enum class ScriptFieldReadStatus
        {
            Unavailable,
            NotFound,
            Found
        };

        std::array<ResolvedNotifyHookTarget, NotifyHookTargetCount> productionNotifyTargets =
            CreateUnresolvedNotifyHookTargets();

        VmNotifyFunction originalVmNotify = nullptr;
        bool minHookInitialized = false;
        bool vmNotifyHookCreated = false;
        std::uintptr_t installedVmNotifyHookAddress = 0;

        bool BytesMatch(const std::uint8_t* address, const std::uint8_t* expected, std::size_t expectedLength);
        bool IsExecutableMemoryAddress(const void* address);
        bool CanReadAddress(const void* address);
        ScriptFieldReadStatus TryReadBoxWeaponName(
            std::int32_t inst,
            const ResolvedNotifyHookTarget& target,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes]);

        bool IsVmNotifyHookEnabled()
        {
#ifdef BO2MONITOR_ENABLE_VM_NOTIFY_HOOK
            return true;
#else
            return false;
#endif
        }

        const ResolvedNotifyHookTarget* FindProductionNotifyTarget(unsigned int stringValue)
        {
            for (const ResolvedNotifyHookTarget& target : productionNotifyTargets)
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
            return IsExecutableMemoryAddress(reinterpret_cast<const void*>(SlGetStringOfSizeCandidate))
                && BytesMatch(
                    reinterpret_cast<const std::uint8_t*>(SlGetStringOfSizeCandidate),
                    ExpectedSlGetStringOfSizePrologue.data(),
                    ExpectedSlGetStringOfSizePrologue.size());
        }

        bool TryResolveLiveStringId(const char* name, unsigned int& stringValue)
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

        bool AreScriptAliasTablesAvailable()
        {
            return CanReadAddress(reinterpret_cast<const void*>(ChildVariablesPointerSlotBase))
                && CanReadAddress(reinterpret_cast<const void*>(ScriptStringDataPointer));
        }

        template <typename T>
        bool TryReadPointer(std::uintptr_t address, T*& pointer)
        {
            pointer = nullptr;
            __try
            {
                pointer = *reinterpret_cast<T**>(address);
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                return false;
            }

            return reinterpret_cast<std::uintptr_t>(pointer) >= MinimumUserModePointer
                && CanReadAddress(pointer);
        }

        ChildVariableValue* ResolveChildVariables(std::int32_t inst)
        {
            if (inst < 0 || inst >= MaxScriptInstanceCount)
            {
                return nullptr;
            }

            ChildVariableValue* childVariables = nullptr;
            const std::uintptr_t pointerSlot =
                ChildVariablesPointerSlotBase + (static_cast<std::uintptr_t>(inst) * ScriptInstancePointerStride);
            return TryReadPointer(pointerSlot, childVariables) ? childVariables : nullptr;
        }

        bool CopyScriptStringValue(unsigned int stringValue, char (&destination)[MaxWeaponNameBytes])
        {
            std::memset(destination, 0, MaxWeaponNameBytes);
            if (stringValue == 0 || stringValue >= MaxScriptStringCount)
            {
                return false;
            }

            const char* source = nullptr;
            __try
            {
                const auto stringData = *reinterpret_cast<const std::uint8_t* const*>(ScriptStringDataPointer);
                if (stringData == nullptr)
                {
                    return false;
                }

                const std::uintptr_t sourceAddress =
                    reinterpret_cast<std::uintptr_t>(stringData)
                    + (static_cast<std::uintptr_t>(stringValue) * ScriptStringDataStride)
                    + ScriptStringTextOffset;
                source = reinterpret_cast<const char*>(sourceAddress);
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                return false;
            }

            if (source == nullptr || !CanReadAddress(source))
            {
                return false;
            }

            std::size_t index = 0;
            __try
            {
                for (; index < MaxWeaponNameBytes - 1; ++index)
                {
                    const char character = source[index];
                    if (character == '\0')
                    {
                        break;
                    }

                    if (character < 0x20 || character > 0x7E)
                    {
                        return false;
                    }

                    destination[index] = character;
                }
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                std::memset(destination, 0, MaxWeaponNameBytes);
                return false;
            }

            destination[index] = '\0';
            return index > 0;
        }

        ScriptFieldReadStatus TryReadOwnerWeaponAliasField(
            std::int32_t inst,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            std::memset(weaponName, 0, MaxWeaponNameBytes);
            if (!AreScriptAliasTablesAvailable())
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            if (ownerId == 0 || ownerId >= MaxScriptObjectVariableCount)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            auto* childVariables = ResolveChildVariables(inst);
            if (childVariables == nullptr)
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            for (unsigned int childIndex = 1; childIndex < MaxScriptChildVariableCount; ++childIndex)
            {
                ChildVariableValue child{};
                __try
                {
                    child = childVariables[childIndex];
                }
                __except (EXCEPTION_EXECUTE_HANDLER)
                {
                    return ScriptFieldReadStatus::Unavailable;
                }

                const std::uint8_t childType = child.Type & 0x7F;
                if (child.Type == 0
                    || child.Key.Keys.ParentId != static_cast<std::uint16_t>(ownerId)
                    || (childType != ScriptString && childType != ScriptIString))
                {
                    continue;
                }

                char candidate[MaxWeaponNameBytes]{};
                if (CopyScriptStringValue(child.Value.StringValue, candidate)
                    && IsLikelyZombieWeaponAlias(candidate))
                {
                    std::memcpy(weaponName, candidate, MaxWeaponNameBytes);
                    return ScriptFieldReadStatus::Found;
                }
            }

            return ScriptFieldReadStatus::NotFound;
        }

        ScriptFieldReadStatus TryReadBoxWeaponName(
            std::int32_t inst,
            const ResolvedNotifyHookTarget& target,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            std::memset(weaponName, 0, MaxWeaponNameBytes);
            if (std::strcmp(target.Name, "randomization_done") == 0
                || std::strcmp(target.Name, "user_grabbed_weapon") == 0)
            {
                return TryReadOwnerWeaponAliasField(inst, ownerId, weaponName);
            }

            return ScriptFieldReadStatus::NotFound;
        }

        bool TryReadLiveRoundValue(std::int32_t& roundValue)
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

        class SharedSnapshotNotifyPublicationWriter final : public INotifyPublicationWriter
        {
        public:
            explicit SharedSnapshotNotifyPublicationWriter(SharedSnapshotWriter& snapshotWriter) :
                snapshotWriter_(snapshotWriter)
            {
            }

            void PublishEvent(
                GameEventType eventType,
                const char* eventName,
                std::int32_t levelTime,
                std::uint32_t ownerId,
                std::uint32_t stringValue,
                std::uint32_t tick,
                const char* weaponName) override
            {
                snapshotWriter_.PublishEvent(
                    eventType,
                    eventName,
                    levelTime,
                    ownerId,
                    stringValue,
                    tick,
                    weaponName);
            }

            void SetNotifyEventCounters(
                std::uint32_t droppedNotifyCount,
                std::uint32_t publishedNotifyCount) override
            {
                snapshotWriter_.SetNotifyEventCounters(droppedNotifyCount, publishedNotifyCount);
            }

        private:
            SharedSnapshotWriter& snapshotWriter_;
        };

        class LiveNotifyRoundReader final : public INotifyRoundReader
        {
        public:
            bool TryReadRoundValue(std::int32_t& roundValue) override
            {
                return TryReadLiveRoundValue(roundValue);
            }
        };

        std::uintptr_t PollingAddressFor(PollingFallbackStat stat)
        {
            switch (stat)
            {
            case PollingFallbackStat::Round:
                return RoundAddress;
            case PollingFallbackStat::Points:
                return PointsAddress;
            case PollingFallbackStat::Kills:
                return KillsAddress;
            case PollingFallbackStat::Downs:
                return DownsAddress;
            }

            return 0;
        }

        class LivePollingFallbackReader final : public IPollingFallbackReader
        {
        public:
            bool TryReadStat(PollingFallbackStat stat, std::int32_t& value) override
            {
                const std::uintptr_t address = PollingAddressFor(stat);
                if (address == 0 || !CanReadAddress(reinterpret_cast<const void*>(address)))
                {
                    return false;
                }

                __try
                {
                    value = *reinterpret_cast<volatile const std::int32_t*>(address);
                }
                __except (EXCEPTION_EXECUTE_HANDLER)
                {
                    return false;
                }

                return true;
            }
        };

        void __cdecl VmNotifyDetour(
            std::int32_t inst,
            unsigned int notifyListOwnerId,
            unsigned int stringValue,
            void* top)
        {
            const ResolvedNotifyHookTarget* target = FindProductionNotifyTarget(stringValue);
            if (target == nullptr)
            {
                originalVmNotify(inst, notifyListOwnerId, stringValue, top);
                return;
            }

            const bool isBoxWeaponEvent = std::strcmp(target->Name, "randomization_done") == 0
                || std::strcmp(target->Name, "user_grabbed_weapon") == 0;
            if (!isBoxWeaponEvent)
            {
                originalVmNotify(inst, notifyListOwnerId, stringValue, top);
                EnqueueMatchedNotify(
                    inst,
                    notifyListOwnerId,
                    stringValue,
                    top,
                    target->EventType,
                    target->Name,
                    nullptr,
                    target->ReadRoundValue);
                return;
            }

            originalVmNotify(inst, notifyListOwnerId, stringValue, top);

            char weaponName[MaxWeaponNameBytes]{};
            const ScriptFieldReadStatus weaponNameStatus = TryReadBoxWeaponName(
                inst,
                *target,
                notifyListOwnerId,
                weaponName);

            EnqueueMatchedNotify(
                inst,
                notifyListOwnerId,
                stringValue,
                top,
                target->EventType,
                target->Name,
                weaponNameStatus == ScriptFieldReadStatus::Found ? weaponName : nullptr,
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

        bool IsExecutableMemoryAddress(const void* address)
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

        bool LocalVmNotifyEntryPrologueMatches()
        {
            return IsExecutableMemoryAddress(reinterpret_cast<const void*>(LocalVmNotifyEntryCandidate))
                && BytesMatch(
                    reinterpret_cast<const std::uint8_t*>(LocalVmNotifyEntryCandidate),
                    ExpectedLocalVmNotifyEntryPrologue.data(),
                    ExpectedLocalVmNotifyEntryPrologue.size());
        }

        bool InstallVmNotifyHook(std::uintptr_t hookTargetAddress)
        {
            auto* targetAddress = reinterpret_cast<std::uint8_t*>(hookTargetAddress);
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
                installedVmNotifyHookAddress = 0;
                return false;
            }

            vmNotifyHookCreated = true;
            installedVmNotifyHookAddress = hookTargetAddress;
            MH_STATUS enableStatus = MH_EnableHook(targetAddress);
            if (enableStatus != MH_OK)
            {
                MH_RemoveHook(targetAddress);
                vmNotifyHookCreated = false;
                installedVmNotifyHookAddress = 0;
                originalVmNotify = nullptr;
                return false;
            }

            return true;
        }

        [[maybe_unused]] void UninstallVmNotifyHook()
        {
            if (vmNotifyHookCreated)
            {
                auto* targetAddress = reinterpret_cast<std::uint8_t*>(installedVmNotifyHookAddress);
                MH_DisableHook(targetAddress);
                MH_RemoveHook(targetAddress);
                vmNotifyHookCreated = false;
                installedVmNotifyHookAddress = 0;
                originalVmNotify = nullptr;
            }

            if (minHookInitialized)
            {
                MH_Uninitialize();
                minHookInitialized = false;
            }
        }

        class LiveHookCompatibilityProbe final : public IHookCompatibilityProbe
        {
        public:
            bool IsExecutableAddress(std::uintptr_t address) override
            {
                return IsExecutableMemoryAddress(reinterpret_cast<const void*>(address));
            }

            bool PrologueMatches(
                std::uintptr_t address,
                const std::uint8_t* expected,
                std::size_t expectedLength) override
            {
                return BytesMatch(
                    reinterpret_cast<const std::uint8_t*>(address),
                    expected,
                    expectedLength);
            }

            bool TryResolveStringId(const char* name, unsigned int& stringValue) override
            {
                return TryResolveLiveStringId(name, stringValue);
            }

            bool TryInstallHook(std::uintptr_t address) override
            {
                ResetNotifyEventQueue();
                return InstallVmNotifyHook(address);
            }
        };

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

            if (CanReadAddress(reinterpret_cast<const void*>(ScriptStringDataPointer)))
            {
                snapshotWriter.PublishEvent(
                    GameEventType::StringResolverCandidate,
                    "sl_string_data_candidate",
                    static_cast<std::int32_t>(ScriptStringDataPointer));
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

        LiveHookCompatibilityProbe probe;
        const HookCompatibilityRequest request
        {
            IsVmNotifyHookEnabled(),
            VmNotifyAddress,
            ExpectedVmNotifyPrologue.data(),
            ExpectedVmNotifyPrologue.size()
        };

        const GameCompatibilityState compatibilityState =
            DetermineHookCompatibility(request, probe, productionNotifyTargets);
        snapshotWriter.SetCompatibility(compatibilityState);
        return compatibilityState;
    }

    void RunNotifyEventWorker(SharedSnapshotWriter& snapshotWriter)
    {
        std::uint64_t publishedNotifyCount = 0;
        SharedSnapshotNotifyPublicationWriter publicationWriter(snapshotWriter);
        LiveNotifyRoundReader roundReader;

        while (!snapshotWriter.WaitForStop(0))
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

                PublishMatchedNotify(publicationWriter, roundReader, record);
                ++publishedNotifyCount;
            }

            PublishNotifyEventCounters(
                publicationWriter,
                GetDroppedNotifyEventCount(),
                publishedNotifyCount);

            if (processedCount == 0)
            {
                snapshotWriter.WaitForStop(100);
            }
            else
            {
                if (snapshotWriter.WaitForStop(0))
                {
                    break;
                }

                Sleep(0);
            }
        }

        UninstallVmNotifyHook();
    }

    void RunPollingFallback(SharedSnapshotWriter& snapshotWriter)
    {
        LivePollingFallbackReader reader;
        PollingFallbackState pollingState;
        const GameCompatibilityState compatibilityState = pollingState.Initialize(reader);
        if (compatibilityState != GameCompatibilityState::PollingFallback)
        {
            snapshotWriter.SetCompatibility(compatibilityState);
            snapshotWriter.WaitForStop(INFINITE);
            return;
        }

        PublishDiscoveryEvidence(snapshotWriter);
        snapshotWriter.SetCompatibility(compatibilityState);
        SharedSnapshotNotifyPublicationWriter publicationWriter(snapshotWriter);

        while (!snapshotWriter.WaitForStop(250))
        {
            pollingState.PublishChanges(reader, publicationWriter);
        }
    }
}
