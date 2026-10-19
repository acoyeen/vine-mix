#pragma once
#include <cstdint>
#include <string>

namespace am {
// A PID is only a live hint. Persist the exact executable path to reconnect
// after an application exits; never identify an application by its title.
struct ProcessBinding {
    std::uint32_t processId{};
    std::string executablePath;
    std::string name;
};
struct ApplicationInfo {
    std::uint32_t processId{};
    std::string executablePath;
    std::string name;
    bool hasAudioSession{};
};
}
