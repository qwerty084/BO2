#include "CppUnitTest.h"

#include "PollingFallback.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        struct PublishedFallbackEvent
        {
            BO2Monitor::GameEventType EventType = BO2Monitor::GameEventType::Unknown;
            std::string EventName;
            std::int32_t LevelTime = 0;
            std::uint32_t OwnerId = 0;
            std::uint32_t StringValue = 0;
            std::uint32_t Tick = 0;
            bool WeaponNameWasNull = true;
        };

        std::size_t StatIndex(BO2Monitor::PollingFallbackStat stat)
        {
            return static_cast<std::size_t>(stat);
        }

        class FakePollingFallbackReader final : public BO2Monitor::IPollingFallbackReader
        {
        public:
            FakePollingFallbackReader()
            {
                Readable.fill(true);
            }

            bool TryReadStat(BO2Monitor::PollingFallbackStat stat, std::int32_t& value) override
            {
                const std::size_t index = StatIndex(stat);
                if (!Readable[index])
                {
                    return false;
                }

                value = Values[index];
                return true;
            }

            void Set(BO2Monitor::PollingFallbackStat stat, std::int32_t value)
            {
                Values[StatIndex(stat)] = value;
            }

            void SetReadable(BO2Monitor::PollingFallbackStat stat, bool readable)
            {
                Readable[StatIndex(stat)] = readable;
            }

        private:
            std::array<std::int32_t, 4> Values{};
            std::array<bool, 4> Readable{};
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
                PublishedFallbackEvent event{};
                event.EventType = eventType;
                event.EventName = eventName == nullptr ? "" : eventName;
                event.LevelTime = levelTime;
                event.OwnerId = ownerId;
                event.StringValue = stringValue;
                event.Tick = tick;
                event.WeaponNameWasNull = weaponName == nullptr;
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

            std::vector<PublishedFallbackEvent> Events;
            int CounterWriteCount = 0;
            std::uint32_t DroppedNotifyCount = 0;
            std::uint32_t PublishedNotifyCount = 0;
        };

        void SeedReadableBaseline(FakePollingFallbackReader& reader)
        {
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 1);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 100);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 10);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 1);
        }

        BO2Monitor::PollingFallbackState InitializePollingFallback(FakePollingFallbackReader& reader)
        {
            BO2Monitor::PollingFallbackState state;

            const BO2Monitor::GameCompatibilityState compatibilityState = state.Initialize(reader);

            Assert::IsTrue(compatibilityState == BO2Monitor::GameCompatibilityState::PollingFallback);
            Assert::IsTrue(state.IsInitialized());
            return state;
        }

        void AssertEvent(
            const PublishedFallbackEvent& event,
            BO2Monitor::GameEventType eventType,
            const char* eventName,
            std::int32_t levelTime)
        {
            Assert::IsTrue(event.EventType == eventType);
            Assert::AreEqual(eventName, event.EventName.c_str());
            Assert::AreEqual(levelTime, event.LevelTime);
            Assert::AreEqual(0u, event.OwnerId);
            Assert::AreEqual(0u, event.StringValue);
            Assert::AreEqual(0u, event.Tick);
            Assert::IsTrue(event.WeaponNameWasNull);
        }
    }

    TEST_CLASS(PollingFallbackTests)
    {
    public:
        TEST_METHOD(UnreadableRequiredInputReturnsUnsupportedCompatibility)
        {
            FakePollingFallbackReader reader;
            SeedReadableBaseline(reader);
            reader.SetReadable(BO2Monitor::PollingFallbackStat::Kills, false);
            BO2Monitor::PollingFallbackState state;

            const BO2Monitor::GameCompatibilityState compatibilityState = state.Initialize(reader);

            Assert::IsTrue(compatibilityState == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::IsFalse(state.IsInitialized());

            FakeNotifyPublicationWriter writer;
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(0), writer.Events.size());
            Assert::AreEqual(0, writer.CounterWriteCount);
        }

        TEST_METHOD(RoundChangesPublishOnlyIncreasingValuesWithinRange)
        {
            FakePollingFallbackReader reader;
            SeedReadableBaseline(reader);
            BO2Monitor::PollingFallbackState state = InitializePollingFallback(reader);
            FakeNotifyPublicationWriter writer;

            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 2);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 3);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 2);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 256);
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(2), writer.Events.size());
            AssertEvent(writer.Events[0], BO2Monitor::GameEventType::RoundChanged, "round_changed", 2);
            AssertEvent(writer.Events[1], BO2Monitor::GameEventType::RoundChanged, "round_changed", 3);
        }

        TEST_METHOD(PointsChangesPublishAnyChangedValueWithinRange)
        {
            FakePollingFallbackReader reader;
            SeedReadableBaseline(reader);
            BO2Monitor::PollingFallbackState state = InitializePollingFallback(reader);
            FakeNotifyPublicationWriter writer;

            reader.Set(BO2Monitor::PollingFallbackStat::Points, 90);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 90);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 110);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, -1);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 2000001);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 0);
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(3), writer.Events.size());
            AssertEvent(writer.Events[0], BO2Monitor::GameEventType::PointsChanged, "points_changed", 90);
            AssertEvent(writer.Events[1], BO2Monitor::GameEventType::PointsChanged, "points_changed", 110);
            AssertEvent(writer.Events[2], BO2Monitor::GameEventType::PointsChanged, "points_changed", 0);
        }

        TEST_METHOD(KillsAndDownsPublishOnlyIncreasingValuesWithinRanges)
        {
            FakePollingFallbackReader reader;
            SeedReadableBaseline(reader);
            BO2Monitor::PollingFallbackState state = InitializePollingFallback(reader);
            FakeNotifyPublicationWriter writer;

            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 9);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 0);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 11);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 2);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 100001);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 1001);
            state.PublishChanges(reader, writer);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 12);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 3);
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(4), writer.Events.size());
            AssertEvent(writer.Events[0], BO2Monitor::GameEventType::KillsChanged, "kills_changed", 11);
            AssertEvent(writer.Events[1], BO2Monitor::GameEventType::DownsChanged, "downs_changed", 2);
            AssertEvent(writer.Events[2], BO2Monitor::GameEventType::KillsChanged, "kills_changed", 12);
            AssertEvent(writer.Events[3], BO2Monitor::GameEventType::DownsChanged, "downs_changed", 3);
        }

        TEST_METHOD(InvalidValuesAreIgnoredWithoutMovingBaselines)
        {
            FakePollingFallbackReader reader;
            reader.Set(BO2Monitor::PollingFallbackStat::Round, 2);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 100);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 10);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 1);
            BO2Monitor::PollingFallbackState state = InitializePollingFallback(reader);
            FakeNotifyPublicationWriter writer;

            reader.Set(BO2Monitor::PollingFallbackStat::Round, 256);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, -1);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, -1);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 1001);
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(0), writer.Events.size());

            reader.Set(BO2Monitor::PollingFallbackStat::Round, 3);
            reader.Set(BO2Monitor::PollingFallbackStat::Points, 101);
            reader.Set(BO2Monitor::PollingFallbackStat::Kills, 11);
            reader.Set(BO2Monitor::PollingFallbackStat::Downs, 2);
            state.PublishChanges(reader, writer);

            Assert::AreEqual(static_cast<std::size_t>(4), writer.Events.size());
            AssertEvent(writer.Events[0], BO2Monitor::GameEventType::RoundChanged, "round_changed", 3);
            AssertEvent(writer.Events[1], BO2Monitor::GameEventType::PointsChanged, "points_changed", 101);
            AssertEvent(writer.Events[2], BO2Monitor::GameEventType::KillsChanged, "kills_changed", 11);
            AssertEvent(writer.Events[3], BO2Monitor::GameEventType::DownsChanged, "downs_changed", 2);
        }
    };
}
