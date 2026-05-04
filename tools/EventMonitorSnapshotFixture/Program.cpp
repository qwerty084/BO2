#include "SharedSnapshot.h"

#include <Windows.h>

#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>

namespace
{
    constexpr std::uint32_t FirstTick = 50'100;
    constexpr std::uint32_t TickStep = 100;

    std::uint32_t TickForEvent(std::uint32_t eventNumber)
    {
        return FirstTick + ((eventNumber - 1) * TickStep);
    }

    void AppendFixtureEvent(
        BO2Monitor::SharedSnapshot& snapshot,
        std::uint32_t eventNumber,
        BO2Monitor::GameEventType eventType,
        const char* eventName,
        const char* weaponName = nullptr)
    {
        BO2Monitor::AppendSharedSnapshotEvent(
            snapshot,
            eventType,
            eventName,
            10'000 + static_cast<std::int32_t>(eventNumber),
            700 + eventNumber,
            1'100 + eventNumber,
            TickForEvent(eventNumber),
            weaponName);
    }

    bool WriteSnapshot(const std::filesystem::path& outputPath, const BO2Monitor::SharedSnapshot& snapshot)
    {
        std::filesystem::create_directories(outputPath.parent_path());
        std::ofstream output(outputPath, std::ios::binary | std::ios::trunc);
        if (!output)
        {
            return false;
        }

        output.write(reinterpret_cast<const char*>(&snapshot), sizeof(snapshot));
        return output.good();
    }
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc != 2)
    {
        std::fputws(L"Usage: EventMonitorSnapshotFixture.exe <output snapshot path>\n", stderr);
        return 2;
    }

    BO2Monitor::SharedSnapshot snapshot{};
    BO2Monitor::InitializeSharedSnapshot(snapshot);
    BO2Monitor::SetSharedSnapshotCompatibility(snapshot, BO2Monitor::GameCompatibilityState::Compatible);
    BO2Monitor::SetSharedSnapshotNotifyEventCounters(
        snapshot,
        7,
        9);

    for (std::uint32_t eventNumber = 1; eventNumber <= 125; ++eventNumber)
    {
        std::string name = "fixture_filler_" + std::to_string(eventNumber);
        AppendFixtureEvent(
            snapshot,
            eventNumber,
            BO2Monitor::GameEventType::NotifyObserved,
            name.c_str());
    }

    AppendFixtureEvent(
        snapshot,
        126,
        BO2Monitor::GameEventType::StartOfRound,
        "start_of_round");
    AppendFixtureEvent(
        snapshot,
        127,
        BO2Monitor::GameEventType::BoxEvent,
        "randomization_done",
        "ray_gun_zm");
    AppendFixtureEvent(
        snapshot,
        128,
        BO2Monitor::GameEventType::EndGame,
        "end_game");
    AppendFixtureEvent(
        snapshot,
        129,
        BO2Monitor::GameEventType::NotifyObserved,
        "fixture_wrap_129");
    AppendFixtureEvent(
        snapshot,
        130,
        BO2Monitor::GameEventType::NotifyObserved,
        "fixture_wrap_130");

    const std::filesystem::path outputPath(argv[1]);
    if (!WriteSnapshot(outputPath, snapshot))
    {
        std::fwprintf(stderr, L"Failed to write snapshot fixture: %ls\n", outputPath.c_str());
        return 1;
    }

    std::wprintf(L"Wrote %zu bytes to %ls\n", sizeof(snapshot), outputPath.c_str());
    return 0;
}
