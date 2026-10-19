#pragma once

#include "am/dsp.hpp"
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace am {

// Queue budgets use the graph's 48 kHz frames, including on non-48 kHz devices.
// This is application buffering only; it does not estimate end-to-end latency.
// Retain graph scheduling headroom and the existing sinc converter quality.
// Growth belongs to a real, running stream shortfall, never silent priming.
class StreamBufferPolicy final {
public:
    static constexpr std::size_t resamplerReserveFrames = 64;
    static constexpr std::size_t captureReserveFrames = quantumFrames + quantumFrames / 2;

    StreamBufferPolicy(std::uint32_t deviceRate, std::uint32_t periodDeviceFrames,
                       bool capture)
        : deviceRate_(deviceRate), capture_(capture) {
        if (!deviceRate_) throw std::invalid_argument("Audio device rate must be positive");
        // Older WASAPI clients may not expose a negotiated period. Preserve a
        // conservative 10 ms assumption until the caller has a measured demand.
        periodFrames_ = periodDeviceFrames ? deviceFramesToEngineFrames(periodDeviceFrames)
                                          : sampleRate / 100;
        targetFrames_ = capture_ ? captureReserveFrames
            : std::max(periodFrames_, quantumFrames) + quantumFrames + resamplerReserveFrames;
        // Step back toward legacy buffering after actual shortfalls, with a
        // finite ceiling. Very long device periods still need their base budget.
        maximumTargetFrames_ = std::max(targetFrames_,
            std::min<std::size_t>(sampleRate / 20, sampleRate * 15 / 1000 + periodFrames_));
    }

    [[nodiscard]] std::size_t deviceFramesToEngineFrames(std::uint32_t frames) const noexcept {
        return static_cast<std::size_t>((std::uint64_t{frames} * sampleRate + deviceRate_ - 1) / deviceRate_);
    }

    [[nodiscard]] std::size_t targetFrames() const noexcept { return targetFrames_; }
    [[nodiscard]] std::size_t maximumTargetFrames() const noexcept { return maximumTargetFrames_; }

    // Demand is the current callback size converted to graph frames. Readiness
    // must count both the external queue and pending input inside the converter.
    [[nodiscard]] std::size_t primeFrames(std::size_t demandEngineFrames = 0) const noexcept {
        if (capture_) return targetFrames_;
        const auto reserve = std::min(quantumFrames + resamplerReserveFrames,
            std::numeric_limits<std::size_t>::max() - demandEngineFrames);
        return std::max(targetFrames_, demandEngineFrames + reserve);
    }

    // Returns whether the next target changed. The owner decides which events
    // are genuine shortfalls, and resets the policy when reopening a stream.
    bool observeUnderrun() noexcept {
        const auto increase = std::min(quantumFrames, maximumTargetFrames_ - targetFrames_);
        targetFrames_ += increase;
        return increase != 0;
    }

private:
    std::uint32_t deviceRate_;
    bool capture_;
    std::size_t periodFrames_{};
    std::size_t targetFrames_{};
    std::size_t maximumTargetFrames_{};
};

// Consumer-owned capture startup/recovery. Waiting for the requested block plus
// the capture reserve avoids building the initial queue through tiny drift
// corrections over several seconds. Priming silence is not an underrun.
class CaptureBufferReader final {
public:
    std::size_t read(FrameQueue& queue, std::span<Frame> output, bool ready,
                     std::size_t periodEngineFrames = 0,
                     std::size_t reserveEngineFrames = StreamBufferPolicy::captureReserveFrames) noexcept {
        std::fill(output.begin(), output.end(), Frame{});
        if (!ready || queue.clearPending() || queue.size() > sampleRate / 10) {
            queue.clear();
            reset();
            return 0;
        }
        if (output.empty()) return 0;
        if (!primed_) {
            const auto block = std::max(output.size(), periodEngineFrames);
            const auto reserve = std::min(std::max(StreamBufferPolicy::captureReserveFrames, reserveEngineFrames),
                std::numeric_limits<std::size_t>::max() - block);
            if (queue.size() < block + reserve) return 0;
            primed_ = true;
        }
        const auto count = queue.pop(output);
        if (count != output.size()) reset();
        return count;
    }

    void reset() noexcept { primed_ = false; }
    [[nodiscard]] bool primed() const noexcept { return primed_; }

private:
    bool primed_{};
};

} // namespace am
