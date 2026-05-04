#include "CppUnitTest.h"

#include "NotifyPublication.h"

#include <cstring>
#include <limits>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        struct PublishedEvent
        {
            BO2Monitor::GameEventType EventType = BO2Monitor::GameEventType::Unknown;
            std::string EventName;
            std::int32_t LevelTime = 0;
            std::uint32_t OwnerId = 0;
            std::uint32_t StringValue = 0;
            std::uint32_t Tick = 0;
            bool WeaponNameWasNull = true;
            std::string WeaponName;
        };

        class FakeNotifyPublicationWriter final : public BO2Monitor::INotifyPublicationWriter
        {
        public:
            void PublishEvent(
                BO2Monitor::GameEventType eventType,
                const char* eventName,
                std::int32_t levelTime,
                std::uint32_t ownerId,
                std::uint32_t stringValue,
                std::uint32_t tick,
                const char* weaponName) override
            {
                PublishedEvent event{};
                event.EventType = eventType;
                event.EventName = eventName == nullptr ? "" : eventName;
                event.LevelTime = levelTime;
                event.OwnerId = ownerId;
                event.StringValue = stringValue;
                event.Tick = tick;
                event.WeaponNameWasNull = weaponName == nullptr;
                event.WeaponName = weaponName == nullptr ? "" : weaponName;
                Events.push_back(event);
            }

            void SetNotifyEventCounters(
                std::uint32_t droppedNotifyCount,
                std::uint32_t publishedNotifyCount) override
            {
                ++CounterWriteCount;
                DroppedNotifyCount = droppedNotifyCount;
                PublishedNotifyCount = publishedNotifyCount;
            }

            std::vector<PublishedEvent> Events;
            int CounterWriteCount = 0;
            std::uint32_t DroppedNotifyCount = 0;
            std::uint32_t PublishedNotifyCount = 0;
        };

        class FakeRoundReader final : public BO2Monitor::INotifyRoundReader
        {
        public:
            bool TryReadRoundValue(std::int32_t& roundValue) override
            {
                ++ReadCount;
                if (!HasRoundValue)
                {
                    return false;
                }

                roundValue = RoundValue;
                return true;
            }

            bool HasRoundValue = false;
            std::int32_t RoundValue = 0;
            int ReadCount = 0;
        };

        BO2Monitor::RawNotifyRecord MakeRecord(
            BO2Monitor::GameEventType eventType,
            const char* eventName,
            std::uint32_t stringValue,
            bool readRoundValue = false,
            const char* weaponName = nullptr)
        {
            BO2Monitor::RawNotifyRecord record{};
            record.Tick = 123456u;
            record.Inst = 1;
            record.OwnerId = 77u;
            record.StringValue = stringValue;
            record.Top = 0x1234u;
            record.EventType = eventType;
            record.EventName = eventName;
            record.ReadRoundValue = readRoundValue;
            if (weaponName != nullptr)
            {
                const std::size_t copyLength = std::strlen(weaponName) < BO2Monitor::MaxWeaponNameBytes - 1
                    ? std::strlen(weaponName)
                    : BO2Monitor::MaxWeaponNameBytes - 1;
                std::memcpy(record.WeaponName, weaponName, copyLength);
            }

            return record;
        }
    }

    TEST_CLASS(NotifyPublicationTests)
    {
    public:
        TEST_METHOD(PublishesMatchedNotifyFields)
        {
            FakeNotifyPublicationWriter writer;
            FakeRoundReader roundReader;
            const BO2Monitor::RawNotifyRecord record = MakeRecord(
                BO2Monitor::GameEventType::BoxEvent,
                "randomization_done",
                456u,
                false,
                "ray_gun_mark2_zm");

            BO2Monitor::PublishMatchedNotify(writer, roundReader, record);

            Assert::AreEqual(static_cast<std::size_t>(1), writer.Events.size());
            const PublishedEvent& event = writer.Events[0];
            Assert::IsTrue(event.EventType == BO2Monitor::GameEventType::BoxEvent);
            Assert::AreEqual("randomization_done", event.EventName.c_str());
            Assert::AreEqual(456, event.LevelTime);
            Assert::AreEqual(77u, event.OwnerId);
            Assert::AreEqual(456u, event.StringValue);
            Assert::AreEqual(123456u, event.Tick);
            Assert::IsFalse(event.WeaponNameWasNull);
            Assert::AreEqual("ray_gun_mark2_zm", event.WeaponName.c_str());
            Assert::AreEqual(0, roundReader.ReadCount);
        }

        TEST_METHOD(RoundReadUsesCurrentRoundWhenAvailable)
        {
            FakeNotifyPublicationWriter writer;
            FakeRoundReader roundReader;
            roundReader.HasRoundValue = true;
            roundReader.RoundValue = 17;
            const BO2Monitor::RawNotifyRecord record = MakeRecord(
                BO2Monitor::GameEventType::StartOfRound,
                "start_of_round",
                900u,
                true);

            BO2Monitor::PublishMatchedNotify(writer, roundReader, record);

            Assert::AreEqual(static_cast<std::size_t>(1), writer.Events.size());
            Assert::AreEqual(17, writer.Events[0].LevelTime);
            Assert::AreEqual(1, roundReader.ReadCount);
        }

        TEST_METHOD(RoundReadFallsBackToNotifyStringWhenRoundUnavailable)
        {
            FakeNotifyPublicationWriter writer;
            FakeRoundReader roundReader;
            const BO2Monitor::RawNotifyRecord record = MakeRecord(
                BO2Monitor::GameEventType::EndOfRound,
                "end_of_round",
                901u,
                true);

            BO2Monitor::PublishMatchedNotify(writer, roundReader, record);

            Assert::AreEqual(static_cast<std::size_t>(1), writer.Events.size());
            Assert::AreEqual(901, writer.Events[0].LevelTime);
            Assert::AreEqual(1, roundReader.ReadCount);
        }

        TEST_METHOD(BoxWeaponAliasIsPublishedWhenPresentAndOmittedWhenAbsent)
        {
            FakeNotifyPublicationWriter writer;
            FakeRoundReader roundReader;
            BO2Monitor::PublishMatchedNotify(
                writer,
                roundReader,
                MakeRecord(
                    BO2Monitor::GameEventType::BoxEvent,
                    "user_grabbed_weapon",
                    100u,
                    false,
                    "m1911_zm"));
            BO2Monitor::PublishMatchedNotify(
                writer,
                roundReader,
                MakeRecord(
                    BO2Monitor::GameEventType::BoxEvent,
                    "box_moving",
                    101u));

            Assert::AreEqual(static_cast<std::size_t>(2), writer.Events.size());
            Assert::IsFalse(writer.Events[0].WeaponNameWasNull);
            Assert::AreEqual("m1911_zm", writer.Events[0].WeaponName.c_str());
            Assert::IsTrue(writer.Events[1].WeaponNameWasNull);
            Assert::AreEqual("", writer.Events[1].WeaponName.c_str());
        }

        TEST_METHOD(InvalidOrOverlongWeaponAliasIsOmittedWithoutCorruptingEvent)
        {
            FakeNotifyPublicationWriter writer;
            FakeRoundReader roundReader;
            BO2Monitor::PublishMatchedNotify(
                writer,
                roundReader,
                MakeRecord(
                    BO2Monitor::GameEventType::BoxEvent,
                    "randomization_done",
                    110u,
                    false,
                    "M1911_zm"));

            BO2Monitor::RawNotifyRecord overlong = MakeRecord(
                BO2Monitor::GameEventType::BoxEvent,
                "user_grabbed_weapon",
                111u);
            std::memset(overlong.WeaponName, 'a', sizeof(overlong.WeaponName));
            BO2Monitor::PublishMatchedNotify(writer, roundReader, overlong);

            Assert::AreEqual(static_cast<std::size_t>(2), writer.Events.size());
            Assert::IsTrue(writer.Events[0].WeaponNameWasNull);
            Assert::AreEqual("randomization_done", writer.Events[0].EventName.c_str());
            Assert::AreEqual(110, writer.Events[0].LevelTime);
            Assert::AreEqual(77u, writer.Events[0].OwnerId);
            Assert::AreEqual(110u, writer.Events[0].StringValue);
            Assert::AreEqual(123456u, writer.Events[0].Tick);

            Assert::IsTrue(writer.Events[1].WeaponNameWasNull);
            Assert::AreEqual("user_grabbed_weapon", writer.Events[1].EventName.c_str());
            Assert::AreEqual(111, writer.Events[1].LevelTime);
            Assert::AreEqual(77u, writer.Events[1].OwnerId);
            Assert::AreEqual(111u, writer.Events[1].StringValue);
            Assert::AreEqual(123456u, writer.Events[1].Tick);
        }

        TEST_METHOD(NotifyCountersAreSaturatedBeforePublishing)
        {
            FakeNotifyPublicationWriter writer;
            constexpr std::uint32_t maxValue = std::numeric_limits<std::uint32_t>::max();

            BO2Monitor::PublishNotifyEventCounters(
                writer,
                static_cast<std::uint64_t>(maxValue) + 1,
                static_cast<std::uint64_t>(maxValue) + 2);

            Assert::AreEqual(1, writer.CounterWriteCount);
            Assert::AreEqual(maxValue, writer.DroppedNotifyCount);
            Assert::AreEqual(maxValue, writer.PublishedNotifyCount);
        }
    };
}
