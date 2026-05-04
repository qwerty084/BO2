#include "CppUnitTest.h"

#include "HookCompatibility.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        constexpr std::array<std::uint8_t, 3> ExpectedPrologue = { 0x55, 0x8B, 0xEC };

        BO2Monitor::HookCompatibilityRequest MakeRequest(bool hookSupportEnabled = true)
        {
            return BO2Monitor::HookCompatibilityRequest
            {
                hookSupportEnabled,
                0x008F31D0,
                ExpectedPrologue.data(),
                ExpectedPrologue.size()
            };
        }

        class FakeHookCompatibilityProbe final : public BO2Monitor::IHookCompatibilityProbe
        {
        public:
            bool IsExecutableAddress(std::uintptr_t address) override
            {
                ++ExecutableAddressCallCount;
                LastExecutableAddress = address;
                return ExecutableAddressResult;
            }

            bool PrologueMatches(
                std::uintptr_t address,
                const std::uint8_t* expected,
                std::size_t expectedLength) override
            {
                ++PrologueCallCount;
                LastPrologueAddress = address;
                LastExpectedPrologueLength = expectedLength;
                ExpectedPrologueWasPassed = expected == ExpectedPrologue.data();
                return PrologueMatchesResult;
            }

            bool TryResolveStringId(const char* name, unsigned int& stringValue) override
            {
                ++ResolveCallCount;
                ResolvedNames.push_back(name == nullptr ? "" : name);
                if (!ResolveStrings || std::strcmp(name, FailingName.c_str()) == 0)
                {
                    stringValue = 0;
                    return false;
                }

                stringValue = NextStringValue++;
                return true;
            }

            bool TryInstallHook(std::uintptr_t address) override
            {
                ++InstallCallCount;
                InstalledAddress = address;
                return InstallHookResult;
            }

            bool ExecutableAddressResult = true;
            bool PrologueMatchesResult = true;
            bool ResolveStrings = true;
            bool InstallHookResult = true;
            std::string FailingName;
            unsigned int NextStringValue = 1000;

            int ExecutableAddressCallCount = 0;
            int PrologueCallCount = 0;
            int ResolveCallCount = 0;
            int InstallCallCount = 0;
            std::uintptr_t LastExecutableAddress = 0;
            std::uintptr_t LastPrologueAddress = 0;
            std::uintptr_t InstalledAddress = 0;
            std::size_t LastExpectedPrologueLength = 0;
            bool ExpectedPrologueWasPassed = false;
            std::vector<std::string> ResolvedNames;
        };

        BO2Monitor::GameCompatibilityState Determine(
            const BO2Monitor::HookCompatibilityRequest& request,
            FakeHookCompatibilityProbe& probe,
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount>& targets)
        {
            return BO2Monitor::DetermineHookCompatibility(request, probe, targets);
        }

        void AssertTargetsUnresolved(
            const std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount>& targets)
        {
            for (const BO2Monitor::ResolvedNotifyHookTarget& target : targets)
            {
                Assert::IsFalse(target.Resolved);
                Assert::AreEqual(0u, target.StringValue);
            }
        }
    }

    TEST_CLASS(HookCompatibilityTests)
    {
    public:
        TEST_METHOD(MissingValidatedTargetReturnsUnsupportedVersion)
        {
            FakeHookCompatibilityProbe probe;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            BO2Monitor::HookCompatibilityRequest request = MakeRequest();
            request.HookTargetAddress = 0;

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::AreEqual(0, probe.ExecutableAddressCallCount);
            Assert::AreEqual(0, probe.PrologueCallCount);
            Assert::AreEqual(0, probe.ResolveCallCount);
            Assert::AreEqual(0, probe.InstallCallCount);
            AssertTargetsUnresolved(targets);
        }

        TEST_METHOD(UnavailableExecutableTargetReturnsUnsupportedVersion)
        {
            FakeHookCompatibilityProbe probe;
            probe.ExecutableAddressResult = false;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest();

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::AreEqual(1, probe.ExecutableAddressCallCount);
            Assert::IsTrue(probe.LastExecutableAddress == request.HookTargetAddress);
            Assert::AreEqual(0, probe.PrologueCallCount);
            Assert::AreEqual(0, probe.ResolveCallCount);
            Assert::AreEqual(0, probe.InstallCallCount);
            AssertTargetsUnresolved(targets);
        }

        TEST_METHOD(MismatchedPrologueReturnsUnsupportedVersion)
        {
            FakeHookCompatibilityProbe probe;
            probe.PrologueMatchesResult = false;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest();

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::AreEqual(1, probe.ExecutableAddressCallCount);
            Assert::AreEqual(1, probe.PrologueCallCount);
            Assert::IsTrue(probe.LastPrologueAddress == request.HookTargetAddress);
            Assert::AreEqual(ExpectedPrologue.size(), probe.LastExpectedPrologueLength);
            Assert::IsTrue(probe.ExpectedPrologueWasPassed);
            Assert::AreEqual(0, probe.ResolveCallCount);
            Assert::AreEqual(0, probe.InstallCallCount);
            AssertTargetsUnresolved(targets);
        }

        TEST_METHOD(CaptureDisabledSkipsStringResolutionAndHookInstallation)
        {
            FakeHookCompatibilityProbe probe;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest(false);

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::CaptureDisabled);
            Assert::AreEqual(1, probe.ExecutableAddressCallCount);
            Assert::AreEqual(1, probe.PrologueCallCount);
            Assert::AreEqual(0, probe.ResolveCallCount);
            Assert::AreEqual(0, probe.InstallCallCount);
            AssertTargetsUnresolved(targets);
        }

        TEST_METHOD(CompatibleWhenTargetStringsAndHookInstallSucceed)
        {
            FakeHookCompatibilityProbe probe;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest();

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::Compatible);
            Assert::AreEqual(1, probe.ExecutableAddressCallCount);
            Assert::AreEqual(1, probe.PrologueCallCount);
            Assert::AreEqual(static_cast<int>(BO2Monitor::NotifyHookTargetCount), probe.ResolveCallCount);
            Assert::AreEqual(1, probe.InstallCallCount);
            Assert::IsTrue(probe.InstalledAddress == request.HookTargetAddress);
            for (const BO2Monitor::ResolvedNotifyHookTarget& target : targets)
            {
                Assert::IsTrue(target.Resolved);
                Assert::IsTrue(target.StringValue >= 1000u);
            }
        }

        TEST_METHOD(StringResolutionFailureReturnsUnsupportedVersion)
        {
            FakeHookCompatibilityProbe probe;
            probe.FailingName = "chest_accessed";
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest();

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::AreEqual(0, probe.InstallCallCount);
            AssertTargetsUnresolved(targets);
        }

        TEST_METHOD(HookInstallFailureReturnsUnsupportedVersion)
        {
            FakeHookCompatibilityProbe probe;
            probe.InstallHookResult = false;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};
            const BO2Monitor::HookCompatibilityRequest request = MakeRequest();

            const BO2Monitor::GameCompatibilityState state = Determine(request, probe, targets);

            Assert::IsTrue(state == BO2Monitor::GameCompatibilityState::UnsupportedVersion);
            Assert::AreEqual(1, probe.InstallCallCount);
            for (const BO2Monitor::ResolvedNotifyHookTarget& target : targets)
            {
                Assert::IsTrue(target.Resolved);
            }
        }

        TEST_METHOD(NotifyTargetResolutionMapsNamesTypesAndRoundReadFlags)
        {
            FakeHookCompatibilityProbe probe;
            std::array<BO2Monitor::ResolvedNotifyHookTarget, BO2Monitor::NotifyHookTargetCount> targets{};

            const bool resolved = BO2Monitor::TryResolveNotifyHookTargets(probe, targets);

            Assert::IsTrue(resolved);
            Assert::AreEqual(static_cast<std::size_t>(12), targets.size());
            AssertResolvedTarget(targets[0], "start_of_round", BO2Monitor::GameEventType::StartOfRound, true, 1000u);
            AssertResolvedTarget(targets[1], "end_of_round", BO2Monitor::GameEventType::EndOfRound, true, 1001u);
            AssertResolvedTarget(targets[2], "end_game", BO2Monitor::GameEventType::EndGame, false, 1002u);
            AssertResolvedTarget(targets[3], "randomization_done", BO2Monitor::GameEventType::BoxEvent, false, 1003u);
            AssertResolvedTarget(targets[4], "user_grabbed_weapon", BO2Monitor::GameEventType::BoxEvent, false, 1004u);
            AssertResolvedTarget(targets[5], "chest_accessed", BO2Monitor::GameEventType::BoxEvent, false, 1005u);
            AssertResolvedTarget(targets[6], "box_moving", BO2Monitor::GameEventType::BoxEvent, false, 1006u);
            AssertResolvedTarget(targets[7], "weapon_fly_away_start", BO2Monitor::GameEventType::BoxEvent, false, 1007u);
            AssertResolvedTarget(targets[8], "weapon_fly_away_end", BO2Monitor::GameEventType::BoxEvent, false, 1008u);
            AssertResolvedTarget(targets[9], "arrived", BO2Monitor::GameEventType::BoxEvent, false, 1009u);
            AssertResolvedTarget(targets[10], "left", BO2Monitor::GameEventType::BoxEvent, false, 1010u);
            AssertResolvedTarget(targets[11], "closed", BO2Monitor::GameEventType::BoxEvent, false, 1011u);

            for (std::size_t index = 0; index < targets.size(); ++index)
            {
                Assert::AreEqual(targets[index].Name, probe.ResolvedNames[index].c_str());
            }
        }

    private:
        static void AssertResolvedTarget(
            const BO2Monitor::ResolvedNotifyHookTarget& target,
            const char* name,
            BO2Monitor::GameEventType eventType,
            bool readRoundValue,
            unsigned int stringValue)
        {
            Assert::AreEqual(name, target.Name);
            Assert::IsTrue(target.EventType == eventType);
            Assert::AreEqual(stringValue, target.StringValue);
            Assert::IsTrue(target.Resolved);
            Assert::IsTrue(target.ReadRoundValue == readRoundValue);
        }
    };
}
