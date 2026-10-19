#pragma once

#include <cstdint>

namespace am {

// Absolute mixer deadlines in QueryPerformanceCounter ticks. Small scheduler
// delays must not slow audio production by adding a fresh block-sized wait.
// Catch-up is limited so an overloaded or stalled mixer never replays an
// unbounded backlog of old blocks in a burst.
class MixClock final {
public:
    explicit constexpr MixClock(std::int64_t ticksPerBlock, std::int64_t firstBlock) noexcept
        : ticksPerBlock_(ticksPerBlock > 0 ? ticksPerBlock : 1), next_(firstBlock) {}

    constexpr void reset(std::int64_t firstBlock) noexcept {
        next_ = firstBlock;
        catchUpBlocks_ = 0;
    }

    // Call after processing one block. A returned deadline <= now means the
    // next block should run immediately, subject to the caller's stop check.
    [[nodiscard]] constexpr std::int64_t advance(std::int64_t now) noexcept {
        next_ += ticksPerBlock_;
        if (next_ > now) {
            catchUpBlocks_ = 0;
        } else if ((now - next_) / ticksPerBlock_ >= maximumCatchUpBlocks
            || catchUpBlocks_ == maximumCatchUpBlocks) {
            next_ = now + ticksPerBlock_;
            catchUpBlocks_ = 0;
        } else {
            ++catchUpBlocks_;
        }
        return next_;
    }

private:
    static constexpr unsigned maximumCatchUpBlocks = 2;
    const std::int64_t ticksPerBlock_;
    std::int64_t next_;
    unsigned catchUpBlocks_{};
};

} // namespace am
