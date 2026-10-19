#pragma once

#include "am/types.hpp"
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>
#include <vector>

namespace am {

// Fixed storage, single producer and single consumer. Neither operation waits.
// An overflowing producer drops NEW samples; it never advances the read cursor
// or overwrites storage that the consumer could currently be reading.
class FrameQueue final {
public:
    explicit FrameQueue(std::size_t capacityFrames);
    FrameQueue(const FrameQueue&) = delete;
    FrameQueue& operator=(const FrameQueue&) = delete;

    std::size_t push(std::span<const Frame> frames) noexcept;
    std::size_t pop(std::span<Frame> frames) noexcept;
    [[nodiscard]] std::size_t size() const noexcept;
    [[nodiscard]] std::size_t capacity() const noexcept { return frames_.size(); }

    // clear() MUST run on the consumer, outside a concurrent pop(). For other
    // threads use requestClear(): the consumer flushes at its next pop().
    void clear() noexcept;
    void requestClear() noexcept;
    [[nodiscard]] bool clearPending() const noexcept;
    [[nodiscard]] std::uint64_t droppedFrames() const noexcept;
    [[nodiscard]] std::uint64_t underrunFrames() const noexcept;
    [[nodiscard]] std::uint64_t overrunEvents() const noexcept;
    [[nodiscard]] std::uint64_t underrunEvents() const noexcept;

private:
    void acknowledgeClear() noexcept;
    std::vector<Frame> frames_;
    alignas(64) std::atomic<std::uint64_t> write_{0};
    alignas(64) std::atomic<std::uint64_t> read_{0};
    alignas(64) std::atomic<std::uint64_t> clearRequested_{0};
    std::atomic<std::uint64_t> clearAcknowledged_{0};
    std::atomic<std::uint64_t> dropped_{0};
    std::atomic<std::uint64_t> missing_{0};
    std::atomic<std::uint64_t> overruns_{0};
    std::atomic<std::uint64_t> underruns_{0};
};

// Run on the stream-owning thread. The queue may be a capture stream's produced
// queue or a renderer's input queue: in both cases, high occupancy must lower
// output/input ratio. Correction is bounded to +/- 2,000 ppm and slewed.
class DriftController final {
public:
    double update(double queueFrames, double targetFrames, double elapsedSeconds) noexcept;
    void reset() noexcept;
    [[nodiscard]] double correction() const noexcept { return correction_; }
private:
    double filteredError_{};
    double integral_{};
    double correction_{1.0};
};

// Streaming, stereo libsamplerate adapter. push() retains input until consumed;
// produce() retains filter history and unconsumed input. No allocation occurs
// after construction. Calls and reset() belong to one stream-owning thread.
class AdaptiveResampler final {
public:
    AdaptiveResampler(std::uint32_t inputRate, std::uint32_t outputRate,
                      std::size_t maximumBufferedFrames = 32768);
    ~AdaptiveResampler();
    AdaptiveResampler(const AdaptiveResampler&) = delete;
    AdaptiveResampler& operator=(const AdaptiveResampler&) = delete;

    std::size_t push(std::span<const Frame> input) noexcept;
    std::size_t produce(std::span<Frame> output, double correction = 1.0) noexcept;
    void reset() noexcept;
    [[nodiscard]] std::size_t pendingInputFrames() const noexcept;
    [[nodiscard]] std::size_t capacity() const noexcept;
    [[nodiscard]] double ratio() const noexcept;
    [[nodiscard]] int lastError() const noexcept;
    [[nodiscard]] std::uint64_t droppedInputFrames() const noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

struct MixMeters {
    std::array<float, sourceCount> sourcePeaks{};
    float peakA{};
    float peakB{};
    bool clipA{};
    bool clipB{};
};

// Stateful 48 kHz stereo mixer. Every route and master transition takes 480
// frames (10 ms), including startup. process() never allocates or takes a lock.
class Mixer final {
public:
    MixMeters process(const std::array<std::span<const Frame>, sourceCount>& inputs,
                      std::span<Frame> outputA, std::span<Frame> outputB,
                      const Configuration& config) noexcept;
    void reset() noexcept;
private:
    struct Ramp {
        float current{};
        float target{};
        float step{};
        std::uint32_t remaining{};
        void set(float value) noexcept;
        float next() noexcept;
    };
    std::array<Ramp, sourceCount> sendsA_{};
    std::array<Ramp, sourceCount> sendsB_{};
    Ramp masterA_{};
    Ramp masterB_{};
};

} // namespace am
