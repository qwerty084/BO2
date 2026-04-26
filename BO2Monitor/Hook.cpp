#include "Hook.h"
#include "NotifyLog.h"
#include "MinHook.h"

#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstddef>
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
        constexpr std::uintptr_t SlGetStringOfSizeCandidate = 0x00418B40;
        constexpr std::uintptr_t ScrVarGlobCandidate = 0x02DEA400;
        constexpr std::uintptr_t ScriptStringDataPointer = 0x02BF83A4;
        constexpr std::size_t MaxScriptObjectVariableCount = 16384;
        constexpr std::size_t MaxScriptChildVariableCount = 0x20000;
        constexpr std::size_t MaxScriptFieldWalkCount = 1024;
        constexpr std::size_t MaxScriptStringCount = 0x40000;
        constexpr std::size_t ScriptStringDataStride = 0x18;
        constexpr std::size_t ScriptStringTextOffset = 4;
        constexpr std::uintptr_t ChildBucketsPointerSlotBase = 0x02DEFB00;
        constexpr std::uintptr_t ChildVariablesPointerSlotBase = 0x02DEFB80;
        constexpr std::uintptr_t ScriptInstancePointerStride = 0x200;
        constexpr std::uintptr_t MinimumUserModePointer = 0x00100000;
        constexpr unsigned int ScriptChildHashMask = 0x1FFFF;
        constexpr unsigned int EncodedScriptFieldBase = 0x10000;
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
            ScriptPointer = 1,
            ScriptString = 2,
            ScriptIString = 3,
            ScriptObject = 18,
            ScriptEntity = 20,
            ScriptArray = 21
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

        struct ScriptFieldValue
        {
            std::uint8_t Type;
            VariableUnion Value;
        };

        enum class ScriptFieldReadStatus
        {
            Unavailable,
            NotFound,
            Found
        };

        struct ProductionNotifyTarget
        {
            const char* Name;
            GameEventType EventType;
            unsigned int StringValue;
            bool Resolved;
            bool ReadRoundValue;
        };

        struct BoxScriptFieldIds
        {
            unsigned int WeaponString = 0;
            unsigned int GrabWeaponName = 0;
            unsigned int Zbarrier = 0;
            unsigned int TagBolt = 0;
            bool Resolved = false;
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
        BoxScriptFieldIds boxScriptFieldIds{};

        bool BytesMatch(const std::uint8_t* address, const std::uint8_t* expected, std::size_t expectedLength);
        bool IsExecutableAddress(const void* address);
        bool CanReadAddress(const void* address);
        bool ResolveBoxScriptFieldIds();
        void PublishBoxScriptFieldEvidence(SharedSnapshotWriter& snapshotWriter);
        ScriptFieldReadStatus TryReadBoxWeaponName(
            std::int32_t inst,
            const ProductionNotifyTarget& target,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes]);
        unsigned int CountScriptFieldMatches(unsigned int fieldName);
        void EnqueueBoxWeaponDiagnostic(
            std::int32_t inst,
            unsigned int ownerId,
            void* top,
            const char* eventName,
            unsigned int value);

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

        bool ResolveBoxScriptFieldIds()
        {
            boxScriptFieldIds = BoxScriptFieldIds{};

            unsigned int weaponString = 0;
            unsigned int grabWeaponName = 0;
            unsigned int zbarrier = 0;
            unsigned int tagBolt = 0;
            if (!TryResolveStringId("weapon_string", weaponString)
                || !TryResolveStringId("grab_weapon_name", grabWeaponName)
                || !TryResolveStringId("zbarrier", zbarrier)
                || !TryResolveStringId("tag_bolt", tagBolt))
            {
                return false;
            }

            boxScriptFieldIds.WeaponString = weaponString;
            boxScriptFieldIds.GrabWeaponName = grabWeaponName;
            boxScriptFieldIds.Zbarrier = zbarrier;
            boxScriptFieldIds.TagBolt = tagBolt;
            boxScriptFieldIds.Resolved = true;
            return true;
        }

        void PublishBoxScriptFieldEvidence(SharedSnapshotWriter& snapshotWriter)
        {
            if (!boxScriptFieldIds.Resolved)
            {
                snapshotWriter.PublishEvent(
                    GameEventType::StringResolverCandidate,
                    "box_field_resolve_failed",
                    0);
                return;
            }

            snapshotWriter.PublishEvent(
                GameEventType::StringResolverCandidate,
                "box_field_weapon_string_id",
                static_cast<std::int32_t>(boxScriptFieldIds.WeaponString));
            snapshotWriter.PublishEvent(
                GameEventType::StringResolverCandidate,
                "box_field_grab_weapon_name_id",
                static_cast<std::int32_t>(boxScriptFieldIds.GrabWeaponName));
            snapshotWriter.PublishEvent(
                GameEventType::StringResolverCandidate,
                "box_field_zbarrier_id",
                static_cast<std::int32_t>(boxScriptFieldIds.Zbarrier));
            snapshotWriter.PublishEvent(
                GameEventType::StringResolverCandidate,
                "box_field_tag_bolt_id",
                static_cast<std::int32_t>(boxScriptFieldIds.TagBolt));
        }

        bool AreScriptFieldHelpersAvailable()
        {
            return boxScriptFieldIds.Resolved
                && CanReadAddress(reinterpret_cast<const void*>(ChildBucketsPointerSlotBase))
                && CanReadAddress(reinterpret_cast<const void*>(ChildVariablesPointerSlotBase))
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

        std::uint32_t* ResolveChildBuckets(std::int32_t inst)
        {
            if (inst < 0 || inst >= MaxScriptInstanceCount)
            {
                return nullptr;
            }

            std::uint32_t* childBuckets = nullptr;
            const std::uintptr_t pointerSlot =
                ChildBucketsPointerSlotBase + (static_cast<std::uintptr_t>(inst) * ScriptInstancePointerStride);
            return TryReadPointer(pointerSlot, childBuckets) ? childBuckets : nullptr;
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

        bool TryReadChildVariable(std::int32_t inst, unsigned int childIndex, ChildVariableValue& child)
        {
            if (childIndex == 0 || childIndex >= MaxScriptChildVariableCount)
            {
                return false;
            }

            __try
            {
                auto* childVariables = ResolveChildVariables(inst);
                if (childVariables == nullptr)
                {
                    return false;
                }

                child = childVariables[childIndex];
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                return false;
            }

            return true;
        }

        unsigned int GetChildFieldName(const ChildVariableValue& child)
        {
            return static_cast<unsigned int>(child.NameLo)
                | (static_cast<unsigned int>(child.Key.Keys.NameHi) << 8);
        }

        unsigned int EncodeObjectFieldName(unsigned int scriptStringId)
        {
            return scriptStringId == 0 ? 0 : scriptStringId + EncodedScriptFieldBase;
        }

        ScriptFieldReadStatus TryReadScriptFieldValue(
            std::int32_t inst,
            unsigned int ownerId,
            unsigned int fieldName,
            ScriptFieldValue& fieldValue)
        {
            fieldValue = ScriptFieldValue{};
            if (!AreScriptFieldHelpersAvailable())
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            if (ownerId == 0 || fieldName == 0)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            if (ownerId >= MaxScriptObjectVariableCount)
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            auto* childBuckets = ResolveChildBuckets(inst);
            if (childBuckets == nullptr)
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            const unsigned int bucketIndex =
                ((ownerId * 0x65u) + fieldName) & ScriptChildHashMask;
            unsigned int childIndex = 0;
            __try
            {
                childIndex = childBuckets[bucketIndex];
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            const auto ownerKey = static_cast<std::uint16_t>(ownerId);
            const unsigned int expectedKey =
                (static_cast<unsigned int>(ownerKey) << 16)
                | ((fieldName >> 8) & 0xFFFFu);
            for (std::size_t walked = 0; childIndex != 0 && walked < MaxScriptFieldWalkCount; ++walked)
            {
                if (childIndex >= MaxScriptChildVariableCount)
                {
                    return ScriptFieldReadStatus::Unavailable;
                }

                ChildVariableValue child{};
                if (!TryReadChildVariable(inst, childIndex, child))
                {
                    return ScriptFieldReadStatus::Unavailable;
                }

                const unsigned int nextChildIndex = child.Next;
                if (child.Type != 0
                    && child.Key.Match == expectedKey
                    && GetChildFieldName(child) == fieldName)
                {
                    fieldValue.Type = child.Type & 0x7F;
                    fieldValue.Value = child.Value;
                    return ScriptFieldReadStatus::Found;
                }

                childIndex = nextChildIndex;
            }

            return ScriptFieldReadStatus::NotFound;
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

        ScriptFieldReadStatus TryReadScriptStringField(
            std::int32_t inst,
            unsigned int ownerId,
            unsigned int fieldName,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            ScriptFieldValue fieldValue{};
            ScriptFieldReadStatus status = TryReadScriptFieldValue(inst, ownerId, fieldName, fieldValue);
            if (status != ScriptFieldReadStatus::Found)
            {
                return status;
            }

            if (fieldValue.Type != ScriptString && fieldValue.Type != ScriptIString)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            return CopyScriptStringValue(fieldValue.Value.StringValue, weaponName)
                ? ScriptFieldReadStatus::Found
                : ScriptFieldReadStatus::NotFound;
        }

        ScriptFieldReadStatus TryReadOwnerWeaponAliasField(
            std::int32_t inst,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            std::memset(weaponName, 0, MaxWeaponNameBytes);
            if (!AreScriptFieldHelpersAvailable())
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

        ScriptFieldReadStatus TryReadAnyScriptStringField(
            std::int32_t inst,
            unsigned int fieldName,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            if (!AreScriptFieldHelpersAvailable())
            {
                return ScriptFieldReadStatus::Unavailable;
            }

            for (unsigned int childIndex = 1; childIndex < MaxScriptChildVariableCount; ++childIndex)
            {
                ChildVariableValue child{};
                if (!TryReadChildVariable(inst, childIndex, child))
                {
                    return ScriptFieldReadStatus::Unavailable;
                }

                const std::uint8_t childType = child.Type & 0x7F;
                if (child.Type == 0
                    || GetChildFieldName(child) != fieldName
                    || (childType != ScriptString && childType != ScriptIString))
                {
                    continue;
                }

                if (CopyScriptStringValue(child.Value.StringValue, weaponName))
                {
                    return ScriptFieldReadStatus::Found;
                }
            }

            return ScriptFieldReadStatus::NotFound;
        }

        unsigned int CountScriptFieldMatches(unsigned int fieldName)
        {
            if (!AreScriptFieldHelpersAvailable() || fieldName == 0)
            {
                return 0;
            }

            unsigned int count = 0;
            for (std::int32_t inst = 0; inst < MaxScriptInstanceCount; ++inst)
            {
                auto* childVariables = ResolveChildVariables(inst);
                if (childVariables == nullptr)
                {
                    continue;
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
                        return 0;
                    }

                    if (child.Type != 0 && GetChildFieldName(child) == fieldName)
                    {
                        ++count;
                    }
                }
            }

            return count;
        }

        ScriptFieldReadStatus TryReadScriptObjectField(
            std::int32_t inst,
            unsigned int ownerId,
            unsigned int fieldName,
            unsigned int& objectId)
        {
            objectId = 0;
            ScriptFieldValue fieldValue{};
            ScriptFieldReadStatus status = TryReadScriptFieldValue(inst, ownerId, fieldName, fieldValue);
            if (status != ScriptFieldReadStatus::Found)
            {
                return status;
            }

            if (fieldValue.Type != ScriptPointer
                && fieldValue.Type != ScriptObject
                && fieldValue.Type != ScriptEntity
                && fieldValue.Type != ScriptArray)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            if (fieldValue.Value.PointerValue == 0)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            objectId = fieldValue.Value.PointerValue;
            return ScriptFieldReadStatus::Found;
        }

        ScriptFieldReadStatus TryReadBoxWeaponName(
            std::int32_t inst,
            const ProductionNotifyTarget& target,
            unsigned int ownerId,
            char (&weaponName)[MaxWeaponNameBytes])
        {
            std::memset(weaponName, 0, MaxWeaponNameBytes);
            if (std::strcmp(target.Name, "randomization_done") == 0)
            {
                ScriptFieldReadStatus status = TryReadScriptStringField(
                    inst,
                    ownerId,
                    EncodeObjectFieldName(boxScriptFieldIds.WeaponString),
                    weaponName);
                if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
                {
                    return status;
                }

                status = TryReadScriptStringField(inst, ownerId, boxScriptFieldIds.WeaponString, weaponName);
                if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
                {
                    return status;
                }

                status = TryReadScriptStringField(inst, ownerId, boxScriptFieldIds.TagBolt, weaponName);
                if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
                {
                    return status;
                }

                return TryReadOwnerWeaponAliasField(inst, ownerId, weaponName);
            }

            if (std::strcmp(target.Name, "user_grabbed_weapon") != 0)
            {
                return ScriptFieldReadStatus::NotFound;
            }

            ScriptFieldReadStatus status = TryReadScriptStringField(
                inst,
                ownerId,
                EncodeObjectFieldName(boxScriptFieldIds.GrabWeaponName),
                weaponName);
            if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
            {
                return status;
            }

            status = TryReadScriptStringField(
                inst,
                ownerId,
                boxScriptFieldIds.GrabWeaponName,
                weaponName);
            if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
            {
                return status;
            }

            unsigned int zbarrierObjectId = 0;
            status = TryReadScriptObjectField(
                inst,
                ownerId,
                EncodeObjectFieldName(boxScriptFieldIds.Zbarrier),
                zbarrierObjectId);
            if (status == ScriptFieldReadStatus::NotFound)
            {
                status = TryReadScriptObjectField(inst, ownerId, boxScriptFieldIds.Zbarrier, zbarrierObjectId);
            }
            if (status == ScriptFieldReadStatus::Unavailable)
            {
                return status;
            }

            if (status == ScriptFieldReadStatus::Found)
            {
                status = TryReadScriptStringField(
                    inst,
                    zbarrierObjectId,
                    EncodeObjectFieldName(boxScriptFieldIds.WeaponString),
                    weaponName);
                if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
                {
                    return status;
                }

                status = TryReadScriptStringField(inst, zbarrierObjectId, boxScriptFieldIds.WeaponString, weaponName);
                if (status == ScriptFieldReadStatus::Found || status == ScriptFieldReadStatus::Unavailable)
                {
                    return status;
                }
            }

            return ScriptFieldReadStatus::NotFound;
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
                record.StringValue,
                record.Tick,
                record.WeaponName[0] == '\0' ? nullptr : record.WeaponName);
        }

        void EnqueueBoxWeaponDiagnostic(
            std::int32_t inst,
            unsigned int ownerId,
            void* top,
            const char* eventName,
            unsigned int value)
        {
            EnqueueMatchedNotify(
                inst,
                ownerId,
                value,
                top,
                GameEventType::StringResolverCandidate,
                eventName,
                nullptr,
                false);
        }

        void __cdecl VmNotifyDetour(
            std::int32_t inst,
            unsigned int notifyListOwnerId,
            unsigned int stringValue,
            void* top)
        {
            const ProductionNotifyTarget* target = FindProductionNotifyTarget(stringValue);
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
            if (weaponNameStatus != ScriptFieldReadStatus::Found)
            {
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    weaponNameStatus == ScriptFieldReadStatus::Unavailable
                        ? "box_weapon_read_unavailable"
                        : "box_weapon_read_not_found",
                    static_cast<unsigned int>(weaponNameStatus));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_weapon_string_field_matches",
                    CountScriptFieldMatches(boxScriptFieldIds.WeaponString));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_weapon_string_object_field_matches",
                    CountScriptFieldMatches(EncodeObjectFieldName(boxScriptFieldIds.WeaponString)));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_grab_weapon_name_field_matches",
                    CountScriptFieldMatches(boxScriptFieldIds.GrabWeaponName));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_grab_weapon_name_object_field_matches",
                    CountScriptFieldMatches(EncodeObjectFieldName(boxScriptFieldIds.GrabWeaponName)));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_zbarrier_field_matches",
                    CountScriptFieldMatches(boxScriptFieldIds.Zbarrier));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_zbarrier_object_field_matches",
                    CountScriptFieldMatches(EncodeObjectFieldName(boxScriptFieldIds.Zbarrier)));
                EnqueueBoxWeaponDiagnostic(
                    inst,
                    notifyListOwnerId,
                    top,
                    "box_tag_bolt_field_matches",
                    CountScriptFieldMatches(boxScriptFieldIds.TagBolt));
            }

            const bool shouldPublish = std::strcmp(target->Name, "user_grabbed_weapon") != 0
                || weaponNameStatus != ScriptFieldReadStatus::NotFound;

            if (!shouldPublish)
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

        ResolveBoxScriptFieldIds();
        PublishBoxScriptFieldEvidence(snapshotWriter);

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

                PublishMatchedNotify(snapshotWriter, record);
                ++publishedNotifyCount;
            }

            snapshotWriter.SetNotifyEventCounters(
                SaturateCounter(GetDroppedNotifyEventCount()),
                SaturateCounter(publishedNotifyCount));

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
        if (!ArePollingAddressesReadable())
        {
            snapshotWriter.SetCompatibility(GameCompatibilityState::UnsupportedVersion);
            snapshotWriter.WaitForStop(INFINITE);
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

        while (!snapshotWriter.WaitForStop(250))
        {
            PublishIfChanged(snapshotWriter, roundValue, previousRound, GameEventType::RoundChanged, "round_changed", true, 2, 255);
            PublishIfChanged(snapshotWriter, pointsValue, previousPoints, GameEventType::PointsChanged, "points_changed", false, 0, 2000000);
            PublishIfChanged(snapshotWriter, killsValue, previousKills, GameEventType::KillsChanged, "kills_changed", true, 0, 100000);
            PublishIfChanged(snapshotWriter, downsValue, previousDowns, GameEventType::DownsChanged, "downs_changed", true, 0, 1000);
        }
    }
}
