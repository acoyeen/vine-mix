#pragma once

#include "am/types.hpp"
#include <cstdint>
#include <memory>
#include <vector>

namespace am {
class FrameQueue;

// Enumeration is cached on a dedicated COM thread. Device notifications only
// advance a generation and wake that thread; they never touch an audio client.
class DeviceCatalog {
public:
    DeviceCatalog();
    ~DeviceCatalog();
    DeviceCatalog(const DeviceCatalog&) = delete;
    DeviceCatalog& operator=(const DeviceCatalog&) = delete;
    std::vector<Endpoint> enumerate() const;
    std::uint64_t generation() const noexcept;
    void refresh() noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

// Capture workers produce stereo 48 kHz frames; render workers consume them.
// Every output gets its own queue so one disconnected output cannot block or
// consume frames belonging to another output. No default-device substitution.
class AudioWorker {
public:
    AudioWorker(bool capture, bool microphone, std::shared_ptr<FrameQueue> queue,
                DeviceCatalog& catalog, std::string requiredTransport = {});
    ~AudioWorker();
    AudioWorker(const AudioWorker&) = delete;
    AudioWorker& operator=(const AudioWorker&) = delete;
    void configure(DeviceBinding binding);
    void retry() noexcept;
    bool ready() const noexcept;
    // Negotiated device callback period expressed in the graph's 48 kHz frames.
    std::uint32_t periodFrames() const noexcept;
    std::uint32_t targetQueueFrames() const noexcept;
    PathState status() const;
    void stop() noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
