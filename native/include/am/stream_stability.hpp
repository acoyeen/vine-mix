#pragma once

#include <array>
#include <cmath>
#include <cstddef>

namespace am {

// Called only for genuine running-stream glitches, with monotonic seconds
// since this stream opened. Startup discontinuities do not judge a device's
// shortest period. Three glitches within ten seconds request a conservative
// reopen; the owner keeps that preference until an explicit retry or change.
class StreamStability final {
public:
    [[nodiscard]] bool observe(double secondsSinceStart) noexcept {
        if (latched_) return true;
        if (!std::isfinite(secondsSinceStart) || secondsSinceStart < warmupSeconds) return false;
        // A reset clock must not combine a new stream's incidents with older
        // timestamps if an owner reuses this otherwise per-open tracker.
        if (count_ && secondsSinceStart < glitches_[count_ - 1]) count_ = 0;
        std::size_t retained{};
        for (std::size_t i = 0; i < count_; ++i)
            if (secondsSinceStart - glitches_[i] <= windowSeconds) glitches_[retained++] = glitches_[i];
        count_ = retained;
        glitches_[count_++] = secondsSinceStart;
        latched_ = count_ == glitches_.size();
        return latched_;
    }

private:
    static constexpr double warmupSeconds = 1.0;
    static constexpr double windowSeconds = 10.0;
    std::array<double, 3> glitches_{};
    std::size_t count_{};
    bool latched_{};
};

} // namespace am
