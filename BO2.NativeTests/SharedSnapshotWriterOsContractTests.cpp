#include "CppUnitTest.h"

#include "SharedSnapshot.h"

#include <Windows.h>
#include <cstring>
#include <string>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        class UniqueHandle
        {
        public:
            explicit UniqueHandle(HANDLE handle = nullptr) noexcept
                : handle_(handle)
            {
            }

            UniqueHandle(const UniqueHandle&) = delete;
            UniqueHandle& operator=(const UniqueHandle&) = delete;

            UniqueHandle(UniqueHandle&& other) noexcept
                : handle_(other.handle_)
            {
                other.handle_ = nullptr;
            }

            UniqueHandle& operator=(UniqueHandle&& other) noexcept
            {
                if (this != &other)
                {
                    Reset();
                    handle_ = other.handle_;
                    other.handle_ = nullptr;
                }

                return *this;
            }

            ~UniqueHandle()
            {
                Reset();
            }

            HANDLE Get() const noexcept
            {
                return handle_;
            }

            void Reset(HANDLE handle = nullptr) noexcept
            {
                if (handle_ != nullptr)
                {
                    CloseHandle(handle_);
                }

                handle_ = handle;
            }

        private:
            HANDLE handle_;
        };

        class MappedSnapshotView
        {
        public:
            explicit MappedSnapshotView(HANDLE mappingHandle)
                : snapshot_(static_cast<const BO2Monitor::SharedSnapshot*>(MapViewOfFile(
                    mappingHandle,
                    FILE_MAP_READ,
                    0,
                    0,
                    sizeof(BO2Monitor::SharedSnapshot))))
            {
                Assert::IsTrue(snapshot_ != nullptr);
            }

            MappedSnapshotView(const MappedSnapshotView&) = delete;
            MappedSnapshotView& operator=(const MappedSnapshotView&) = delete;

            ~MappedSnapshotView()
            {
                if (snapshot_ != nullptr)
                {
                    UnmapViewOfFile(snapshot_);
                }
            }

            const BO2Monitor::SharedSnapshot& Snapshot() const noexcept
            {
                return *snapshot_;
            }

        private:
            const BO2Monitor::SharedSnapshot* snapshot_;
        };

        std::wstring CurrentProcessSharedMemoryName()
        {
            return BO2Monitor::BuildSharedMemoryName(GetCurrentProcessId());
        }

        std::wstring CurrentProcessEventName()
        {
            return BO2Monitor::BuildEventHandleName(GetCurrentProcessId());
        }

        std::wstring CurrentProcessStopEventName()
        {
            return BO2Monitor::BuildStopEventHandleName(GetCurrentProcessId());
        }

        UniqueHandle OpenReadableSharedMemory()
        {
            UniqueHandle mapping(OpenFileMappingW(FILE_MAP_READ, FALSE, CurrentProcessSharedMemoryName().c_str()));
            Assert::IsTrue(mapping.Get() != nullptr);
            return mapping;
        }

        UniqueHandle OpenUpdateEvent()
        {
            UniqueHandle updateEvent(OpenEventW(
                SYNCHRONIZE | EVENT_MODIFY_STATE,
                FALSE,
                CurrentProcessEventName().c_str()));
            Assert::IsTrue(updateEvent.Get() != nullptr);
            Assert::IsTrue(ResetEvent(updateEvent.Get()) != FALSE);
            return updateEvent;
        }

        UniqueHandle OpenStopEvent()
        {
            UniqueHandle stopEvent(OpenEventW(
                SYNCHRONIZE | EVENT_MODIFY_STATE,
                FALSE,
                CurrentProcessStopEventName().c_str()));
            Assert::IsTrue(stopEvent.Get() != nullptr);
            Assert::IsTrue(ResetEvent(stopEvent.Get()) != FALSE);
            return stopEvent;
        }

        void AssertNoCurrentProcessWriterObjects()
        {
            UniqueHandle mapping(OpenFileMappingW(FILE_MAP_READ, FALSE, CurrentProcessSharedMemoryName().c_str()));
            Assert::IsTrue(mapping.Get() == nullptr);

            UniqueHandle updateEvent(OpenEventW(SYNCHRONIZE, FALSE, CurrentProcessEventName().c_str()));
            Assert::IsTrue(updateEvent.Get() == nullptr);

            UniqueHandle stopEvent(OpenEventW(SYNCHRONIZE, FALSE, CurrentProcessStopEventName().c_str()));
            Assert::IsTrue(stopEvent.Get() == nullptr);
        }
    }

    TEST_CLASS(SharedSnapshotWriterOsContractTests)
    {
    public:
        TEST_METHOD(InitializeCreatesReadableSnapshotWithEmptyEventState)
        {
            BO2Monitor::SharedSnapshotWriter writer;

            Assert::IsTrue(writer.Initialize());

            UniqueHandle mapping = OpenReadableSharedMemory();
            MappedSnapshotView view(mapping.Get());
            const BO2Monitor::SharedSnapshot& snapshot = view.Snapshot();

            Assert::AreEqual(BO2Monitor::SnapshotMagic, snapshot.Magic);
            Assert::AreEqual(BO2Monitor::SnapshotVersion, snapshot.Version);
            Assert::IsTrue(snapshot.CompatibilityState == BO2Monitor::GameCompatibilityState::WaitingForMonitor);
            Assert::AreEqual(0u, snapshot.EventWriteIndex);
            Assert::AreEqual(0u, snapshot.DroppedEventCount);
            Assert::AreEqual(0u, snapshot.EventCount);
            Assert::AreEqual(0u, snapshot.DroppedNotifyCount);
            Assert::AreEqual(0u, snapshot.PublishedNotifyCount);
            Assert::AreEqual(0u, snapshot.WriteSequence);
            Assert::IsTrue(snapshot.Events[0].EventType == BO2Monitor::GameEventType::Unknown);
            Assert::AreEqual('\0', snapshot.Events[0].EventName[0]);
            Assert::AreEqual('\0', snapshot.Events[0].WeaponName[0]);
        }

        TEST_METHOD(InitializeCreatesProcessScopedObjectsWithManagedContractNames)
        {
            const DWORD processId = GetCurrentProcessId();
            const std::wstring processIdText = std::to_wstring(processId);

            Assert::AreEqual((std::wstring(BO2Monitor::SharedMemoryNamePrefix) + processIdText).c_str(),
                BO2Monitor::BuildSharedMemoryName(processId).c_str());
            Assert::AreEqual((std::wstring(BO2Monitor::EventHandleNamePrefix) + processIdText).c_str(),
                BO2Monitor::BuildEventHandleName(processId).c_str());
            Assert::AreEqual((std::wstring(BO2Monitor::StopEventHandleNamePrefix) + processIdText).c_str(),
                BO2Monitor::BuildStopEventHandleName(processId).c_str());

            BO2Monitor::SharedSnapshotWriter writer;

            Assert::IsTrue(writer.Initialize());

            UniqueHandle mapping = OpenReadableSharedMemory();
            UniqueHandle updateEvent = OpenUpdateEvent();
            UniqueHandle stopEvent = OpenStopEvent();

            Assert::IsTrue(mapping.Get() != nullptr);
            Assert::IsTrue(updateEvent.Get() != nullptr);
            Assert::IsTrue(stopEvent.Get() != nullptr);
        }

        TEST_METHOD(SetCompatibilitySignalsUpdateEventAndWritesState)
        {
            BO2Monitor::SharedSnapshotWriter writer;
            Assert::IsTrue(writer.Initialize());
            UniqueHandle updateEvent = OpenUpdateEvent();
            UniqueHandle mapping = OpenReadableSharedMemory();
            MappedSnapshotView view(mapping.Get());

            Assert::AreEqual(static_cast<DWORD>(WAIT_TIMEOUT), WaitForSingleObject(updateEvent.Get(), 0));

            writer.SetCompatibility(BO2Monitor::GameCompatibilityState::Compatible);

            Assert::AreEqual(static_cast<DWORD>(WAIT_OBJECT_0), WaitForSingleObject(updateEvent.Get(), 1000));
            Assert::IsTrue(view.Snapshot().CompatibilityState == BO2Monitor::GameCompatibilityState::Compatible);
            Assert::AreEqual(2u, view.Snapshot().WriteSequence);
        }

        TEST_METHOD(PublishEventSignalsUpdateEventAndPreservesFields)
        {
            BO2Monitor::SharedSnapshotWriter writer;
            Assert::IsTrue(writer.Initialize());
            UniqueHandle updateEvent = OpenUpdateEvent();
            UniqueHandle mapping = OpenReadableSharedMemory();
            MappedSnapshotView view(mapping.Get());

            writer.PublishEvent(
                BO2Monitor::GameEventType::BoxEvent,
                "randomization_done",
                123,
                456u,
                789u,
                3456u,
                "ray_gun_zm");

            Assert::AreEqual(static_cast<DWORD>(WAIT_OBJECT_0), WaitForSingleObject(updateEvent.Get(), 1000));

            const BO2Monitor::SharedSnapshot& snapshot = view.Snapshot();
            const BO2Monitor::GameEventRecord& record = snapshot.Events[0];
            Assert::AreEqual(1u, snapshot.EventWriteIndex);
            Assert::AreEqual(1u, snapshot.EventCount);
            Assert::AreEqual(0u, snapshot.DroppedEventCount);
            Assert::AreEqual(2u, snapshot.WriteSequence);
            Assert::IsTrue(record.EventType == BO2Monitor::GameEventType::BoxEvent);
            Assert::AreEqual(123, record.LevelTime);
            Assert::AreEqual(456u, record.OwnerId);
            Assert::AreEqual(789u, record.StringValue);
            Assert::AreEqual(3456u, record.Tick);
            Assert::AreEqual(0, std::strcmp("randomization_done", record.EventName));
            Assert::AreEqual(0, std::strcmp("ray_gun_zm", record.WeaponName));
        }

        TEST_METHOD(WaitForStopObservesProcessScopedStopEvent)
        {
            BO2Monitor::SharedSnapshotWriter writer;
            Assert::IsTrue(writer.Initialize());
            UniqueHandle stopEvent = OpenStopEvent();

            Assert::IsFalse(writer.WaitForStop(0));

            Assert::IsTrue(SetEvent(stopEvent.Get()) != FALSE);

            Assert::IsTrue(writer.WaitForStop(1000));
        }

        TEST_METHOD(PublicCallsAreSafeBeforeInitialization)
        {
            AssertNoCurrentProcessWriterObjects();

            BO2Monitor::SharedSnapshotWriter writer;

            writer.SetCompatibility(BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            writer.SetNotifyEventCounters(3u, 4u);
            writer.PublishEvent(BO2Monitor::GameEventType::NotifyObserved, "vm_notify_observed", 12);
            writer.PublishEvent(BO2Monitor::GameEventType::NotifyObserved, nullptr, 13);

            Assert::IsFalse(writer.WaitForStop(0));
            AssertNoCurrentProcessWriterObjects();
        }

        TEST_METHOD(PublicCallsAreSafeAfterInitializationFailureBeforeMapping)
        {
            UniqueHandle sharedMemoryNameBlocker(CreateEventW(
                nullptr,
                TRUE,
                FALSE,
                CurrentProcessSharedMemoryName().c_str()));
            Assert::IsTrue(sharedMemoryNameBlocker.Get() != nullptr);

            BO2Monitor::SharedSnapshotWriter writer;

            Assert::IsFalse(writer.Initialize());

            writer.SetCompatibility(BO2Monitor::GameCompatibilityState::CaptureDisabled);
            writer.SetNotifyEventCounters(5u, 6u);
            writer.PublishEvent(BO2Monitor::GameEventType::RoundChanged, "round_changed", 4);

            Assert::IsFalse(writer.WaitForStop(0));

            UniqueHandle mapping(OpenFileMappingW(FILE_MAP_READ, FALSE, CurrentProcessSharedMemoryName().c_str()));
            Assert::IsTrue(mapping.Get() == nullptr);

            UniqueHandle updateEvent(OpenEventW(SYNCHRONIZE, FALSE, CurrentProcessEventName().c_str()));
            Assert::IsTrue(updateEvent.Get() == nullptr);

            UniqueHandle stopEvent(OpenEventW(SYNCHRONIZE, FALSE, CurrentProcessStopEventName().c_str()));
            Assert::IsTrue(stopEvent.Get() == nullptr);
        }

        TEST_METHOD(DestructorClosesProcessScopedObjects)
        {
            {
                BO2Monitor::SharedSnapshotWriter writer;

                Assert::IsTrue(writer.Initialize());
            }

            AssertNoCurrentProcessWriterObjects();
        }
    };
}
