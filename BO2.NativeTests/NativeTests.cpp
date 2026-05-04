#include "CppUnitTest.h"

#include "HookPure.h"
#include "InjectorArguments.h"
#include "NotifyLog.h"
#include "SharedSnapshot.h"

#include <array>
#include <cstring>
#include <limits>
#include <string>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        std::string Repeat(char value, std::size_t count)
        {
            return std::string(count, value);
        }

        wchar_t* Mutable(std::wstring& value)
        {
            return value.empty() ? nullptr : &value[0];
        }
    }

    TEST_CLASS(SnapshotContractTests)
    {
    public:
        TEST_METHOD(ConstantsMatchManagedSnapshotReaderContract)
        {
            Assert::AreEqual(0x45324F42u, BO2Monitor::SnapshotMagic);
            Assert::AreEqual(6u, BO2Monitor::SnapshotVersion);
            Assert::AreEqual(static_cast<std::size_t>(128), BO2Monitor::MaxEventCount);
            Assert::AreEqual(static_cast<std::size_t>(64), BO2Monitor::MaxEventNameBytes);
            Assert::AreEqual(static_cast<std::size_t>(64), BO2Monitor::MaxWeaponNameBytes);
            Assert::AreEqual(static_cast<std::size_t>(148), sizeof(BO2Monitor::GameEventRecord));
            Assert::AreEqual(static_cast<std::size_t>(18980), sizeof(BO2Monitor::SharedSnapshot));
            Assert::AreEqual(L"BO2MonitorSharedMem-", BO2Monitor::SharedMemoryNamePrefix);
            Assert::AreEqual(L"BO2MonitorEvent-", BO2Monitor::EventHandleNamePrefix);
            Assert::AreEqual(L"BO2MonitorStopEvent-", BO2Monitor::StopEventHandleNamePrefix);
        }
    };

    TEST_CLASS(SnapshotMutationTests)
    {
    public:
        TEST_METHOD(InitializeSetsHeaderAndClearsSnapshot)
        {
            BO2Monitor::SharedSnapshot snapshot{};
            snapshot.EventCount = 99;

            BO2Monitor::InitializeSharedSnapshot(snapshot);

            Assert::AreEqual(BO2Monitor::SnapshotMagic, snapshot.Magic);
            Assert::AreEqual(BO2Monitor::SnapshotVersion, snapshot.Version);
            Assert::IsTrue(snapshot.CompatibilityState == BO2Monitor::GameCompatibilityState::WaitingForMonitor);
            Assert::AreEqual(0u, snapshot.EventWriteIndex);
            Assert::AreEqual(0u, snapshot.EventCount);
            Assert::AreEqual(0u, snapshot.WriteSequence);
        }

        TEST_METHOD(CompatibilityAndCounterWritesUseEvenSequences)
        {
            BO2Monitor::SharedSnapshot snapshot{};
            BO2Monitor::InitializeSharedSnapshot(snapshot);

            BO2Monitor::SetSharedSnapshotCompatibility(snapshot, BO2Monitor::GameCompatibilityState::Compatible);
            Assert::IsTrue(snapshot.CompatibilityState == BO2Monitor::GameCompatibilityState::Compatible);
            Assert::AreEqual(2u, snapshot.WriteSequence);

            BO2Monitor::SetSharedSnapshotNotifyEventCounters(snapshot, 3, 4);
            Assert::AreEqual(3u, snapshot.DroppedNotifyCount);
            Assert::AreEqual(4u, snapshot.PublishedNotifyCount);
            Assert::AreEqual(4u, snapshot.WriteSequence);
        }

        TEST_METHOD(AppendEventPreservesFieldsAndTruncatesNames)
        {
            BO2Monitor::SharedSnapshot snapshot{};
            BO2Monitor::InitializeSharedSnapshot(snapshot);
            const std::string eventName = Repeat('e', BO2Monitor::MaxEventNameBytes + 10);
            const std::string weaponName = Repeat('w', BO2Monitor::MaxWeaponNameBytes + 10);

            BO2Monitor::AppendSharedSnapshotEvent(
                snapshot,
                BO2Monitor::GameEventType::BoxEvent,
                eventName.c_str(),
                42,
                7,
                9,
                1234,
                weaponName.c_str());

            const BO2Monitor::GameEventRecord& record = snapshot.Events[0];
            Assert::IsTrue(record.EventType == BO2Monitor::GameEventType::BoxEvent);
            Assert::AreEqual(42, record.LevelTime);
            Assert::AreEqual(7u, record.OwnerId);
            Assert::AreEqual(9u, record.StringValue);
            Assert::AreEqual(1234u, record.Tick);
            Assert::AreEqual(0, std::strncmp(record.EventName, eventName.c_str(), BO2Monitor::MaxEventNameBytes - 1));
            Assert::AreEqual('\0', record.EventName[BO2Monitor::MaxEventNameBytes - 1]);
            Assert::AreEqual(0, std::strncmp(record.WeaponName, weaponName.c_str(), BO2Monitor::MaxWeaponNameBytes - 1));
            Assert::AreEqual('\0', record.WeaponName[BO2Monitor::MaxWeaponNameBytes - 1]);
            Assert::AreEqual(1u, snapshot.EventWriteIndex);
            Assert::AreEqual(1u, snapshot.EventCount);
            Assert::AreEqual(0u, snapshot.DroppedEventCount);
            Assert::AreEqual(2u, snapshot.WriteSequence);
        }

        TEST_METHOD(AppendEventAllowsNullWeaponAndRejectsNullEventName)
        {
            BO2Monitor::SharedSnapshot snapshot{};
            BO2Monitor::InitializeSharedSnapshot(snapshot);

            BO2Monitor::AppendSharedSnapshotEvent(
                snapshot,
                BO2Monitor::GameEventType::NotifyObserved,
                "notify_observed",
                11,
                0,
                0,
                1,
                nullptr);
            BO2Monitor::AppendSharedSnapshotEvent(
                snapshot,
                BO2Monitor::GameEventType::NotifyObserved,
                nullptr,
                12);

            Assert::AreEqual(1u, snapshot.EventCount);
            Assert::AreEqual('\0', snapshot.Events[0].WeaponName[0]);
            Assert::AreEqual(2u, snapshot.WriteSequence);
        }

        TEST_METHOD(AppendEventWrapsRingAndCountsDrops)
        {
            BO2Monitor::SharedSnapshot snapshot{};
            BO2Monitor::InitializeSharedSnapshot(snapshot);

            for (std::size_t index = 0; index < BO2Monitor::MaxEventCount + 2; ++index)
            {
                BO2Monitor::AppendSharedSnapshotEvent(
                    snapshot,
                    BO2Monitor::GameEventType::RoundChanged,
                    "round_changed",
                    static_cast<std::int32_t>(index),
                    0,
                    0,
                    static_cast<std::uint32_t>(index + 1));
            }

            Assert::AreEqual(static_cast<std::uint32_t>(BO2Monitor::MaxEventCount), snapshot.EventCount);
            Assert::AreEqual(2u, snapshot.DroppedEventCount);
            Assert::AreEqual(2u, snapshot.EventWriteIndex);
            Assert::AreEqual(static_cast<std::int32_t>(BO2Monitor::MaxEventCount), snapshot.Events[0].LevelTime);
            Assert::AreEqual(static_cast<std::int32_t>(BO2Monitor::MaxEventCount + 1), snapshot.Events[1].LevelTime);
            Assert::AreEqual(static_cast<std::uint32_t>((BO2Monitor::MaxEventCount + 2) * 2), snapshot.WriteSequence);
        }
    };

    TEST_CLASS(NotifyQueueTests)
    {
    public:
        TEST_METHOD(ResetLeavesQueueEmpty)
        {
            BO2Monitor::ResetNotifyEventQueue();

            BO2Monitor::RawNotifyRecord record{};
            std::uint64_t dropped = 99;
            Assert::IsFalse(BO2Monitor::TryDequeueMatchedNotify(record, dropped));
            Assert::AreEqual(0ull, dropped);
            Assert::AreEqual(0ull, BO2Monitor::GetDroppedNotifyEventCount());
        }

        TEST_METHOD(EnqueueDequeuePreservesOrderingAndFields)
        {
            BO2Monitor::ResetNotifyEventQueue();

            BO2Monitor::EnqueueMatchedNotify(1, 10, 100, reinterpret_cast<void*>(0x1234), BO2Monitor::GameEventType::BoxEvent, "first", "m1911_zm", true);
            BO2Monitor::EnqueueMatchedNotify(2, 20, 200, reinterpret_cast<void*>(0x5678), BO2Monitor::GameEventType::EndGame, "second", nullptr, false);

            BO2Monitor::RawNotifyRecord first{};
            std::uint64_t dropped = 0;
            Assert::IsTrue(BO2Monitor::TryDequeueMatchedNotify(first, dropped));
            Assert::AreEqual(0ull, dropped);
            Assert::AreEqual(0ull, first.Seq);
            Assert::AreEqual(1, first.Inst);
            Assert::AreEqual(10u, first.OwnerId);
            Assert::AreEqual(100u, first.StringValue);
            Assert::AreEqual(static_cast<std::uintptr_t>(0x1234), first.Top);
            Assert::IsTrue(first.EventType == BO2Monitor::GameEventType::BoxEvent);
            Assert::AreEqual("first", first.EventName);
            Assert::AreEqual(0, std::strcmp("m1911_zm", first.WeaponName));
            Assert::IsTrue(first.ReadRoundValue);

            BO2Monitor::RawNotifyRecord second{};
            Assert::IsTrue(BO2Monitor::TryDequeueMatchedNotify(second, dropped));
            Assert::AreEqual(1ull, second.Seq);
            Assert::AreEqual('\0', second.WeaponName[0]);
            Assert::IsFalse(second.ReadRoundValue);
        }

        TEST_METHOD(WeaponNameIsTruncated)
        {
            BO2Monitor::ResetNotifyEventQueue();
            const std::string weaponName = Repeat('w', BO2Monitor::MaxWeaponNameBytes + 10);

            BO2Monitor::EnqueueMatchedNotify(1, 2, 3, nullptr, BO2Monitor::GameEventType::BoxEvent, "box", weaponName.c_str(), false);

            BO2Monitor::RawNotifyRecord record{};
            std::uint64_t dropped = 0;
            Assert::IsTrue(BO2Monitor::TryDequeueMatchedNotify(record, dropped));
            Assert::AreEqual(0, std::strncmp(record.WeaponName, weaponName.c_str(), BO2Monitor::MaxWeaponNameBytes - 1));
            Assert::AreEqual('\0', record.WeaponName[BO2Monitor::MaxWeaponNameBytes - 1]);
        }

        TEST_METHOD(OverflowReportsDroppedRecords)
        {
            BO2Monitor::ResetNotifyEventQueue();

            for (std::uint64_t index = 0; index < 257; ++index)
            {
                BO2Monitor::EnqueueMatchedNotify(1, 2, static_cast<unsigned int>(index), nullptr, BO2Monitor::GameEventType::NotifyObserved, "notify", nullptr, false);
            }

            BO2Monitor::RawNotifyRecord record{};
            std::uint64_t dropped = 0;
            Assert::IsTrue(BO2Monitor::TryDequeueMatchedNotify(record, dropped));
            Assert::AreEqual(256ull, dropped);
            Assert::AreEqual(256ull, BO2Monitor::GetDroppedNotifyEventCount());
            Assert::AreEqual(256ull, record.Seq);
            Assert::AreEqual(256u, record.StringValue);
        }
    };

    TEST_CLASS(HookPureLogicTests)
    {
    public:
        TEST_METHOD(ZombieWeaponAliasValidation)
        {
            Assert::IsTrue(BO2Monitor::IsLikelyZombieWeaponAlias("m1911_zm"));
            Assert::IsTrue(BO2Monitor::IsLikelyZombieWeaponAlias("ray_gun_mark2_zm"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias(nullptr));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("_zm"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("m1911"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("M1911_zm"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("m1911-zm"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("m1911_\x1Fzm"));
            Assert::IsFalse(BO2Monitor::IsLikelyZombieWeaponAlias("m1911_\xC3\xA9_zm"));
        }

        TEST_METHOD(CounterSaturationCapsAtUInt32Max)
        {
            constexpr std::uint32_t maxValue = std::numeric_limits<std::uint32_t>::max();

            Assert::AreEqual(0u, BO2Monitor::SaturateCounter(0));
            Assert::AreEqual(42u, BO2Monitor::SaturateCounter(42));
            Assert::AreEqual(maxValue, BO2Monitor::SaturateCounter(maxValue));
            Assert::AreEqual(maxValue, BO2Monitor::SaturateCounter(static_cast<std::uint64_t>(maxValue) + 1));
        }
    };

    TEST_CLASS(InjectorArgumentParsingTests)
    {
    public:
        TEST_METHOD(WrongArityReturnsUsage)
        {
            std::array<std::wstring, 2> values{ L"BO2InjectorHelper.exe", L"123" };
            std::array<wchar_t*, 2> argv{ Mutable(values[0]), Mutable(values[1]) };
            BO2InjectorHelper::InjectorArguments arguments{};

            const BO2InjectorHelper::ParseArgumentsStatus status =
                BO2InjectorHelper::ParseInjectorArguments(static_cast<int>(argv.size()), argv.data(), arguments);

            Assert::IsTrue(status == BO2InjectorHelper::ParseArgumentsStatus::Usage);
        }

        TEST_METHOD(InvalidPidFormsAreRejected)
        {
            AssertInvalidPid(L"abc");
            AssertInvalidPid(L"12x");
            AssertInvalidPid(L"0");
            AssertInvalidPid(L"-1");
            AssertInvalidPid(L"4294967296");
            AssertInvalidPid(L"184467440737095516160");
        }

        TEST_METHOD(ValidPidAndPathAreParsed)
        {
            std::array<std::wstring, 3> values{ L"BO2InjectorHelper.exe", L"1234", L"C:\\Temp\\BO2Monitor.dll" };
            std::array<wchar_t*, 3> argv{ Mutable(values[0]), Mutable(values[1]), Mutable(values[2]) };
            BO2InjectorHelper::InjectorArguments arguments{};

            const BO2InjectorHelper::ParseArgumentsStatus status =
                BO2InjectorHelper::ParseInjectorArguments(static_cast<int>(argv.size()), argv.data(), arguments);

            Assert::IsTrue(status == BO2InjectorHelper::ParseArgumentsStatus::Success);
            Assert::AreEqual(static_cast<DWORD>(1234), arguments.ProcessId);
            Assert::AreEqual(values[2].c_str(), arguments.DllPath.c_str());
        }

    private:
        static void AssertInvalidPid(const wchar_t* pid)
        {
            std::array<std::wstring, 3> values{ L"BO2InjectorHelper.exe", pid, L"C:\\Temp\\BO2Monitor.dll" };
            std::array<wchar_t*, 3> argv{ Mutable(values[0]), Mutable(values[1]), Mutable(values[2]) };
            BO2InjectorHelper::InjectorArguments arguments{};

            const BO2InjectorHelper::ParseArgumentsStatus status =
                BO2InjectorHelper::ParseInjectorArguments(static_cast<int>(argv.size()), argv.data(), arguments);

            Assert::IsTrue(status == BO2InjectorHelper::ParseArgumentsStatus::InvalidProcessId);
        }
    };
}
