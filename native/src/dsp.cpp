#include "am/dsp.hpp"
#include <samplerate.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string>

namespace am {
namespace {
constexpr std::uint32_t rampFrames = sampleRate / 100;
constexpr double maximumDrift = 0.002;

float clean(float sample) noexcept {
    return std::isfinite(sample) ? sample : 0.0f;
}

float gain(float db) noexcept {
    if (!std::isfinite(db)) return 0.0f;
    if (db <= -96.0f) return 0.0f;
    return std::pow(10.0f, std::clamp(db, -96.0f, 12.0f) / 20.0f);
}

float safePeak(double value) noexcept {
    return static_cast<float>(std::min(std::abs(value), 1.0));
}

float limit(double value) noexcept {
    // Accumulation uses double so even extreme finite float input cannot
    // overflow into NaN/Inf before final saturation.
    return static_cast<float>(std::clamp(value, -1.0, 1.0));
}
} // namespace

FrameQueue::FrameQueue(std::size_t capacityFrames) : frames_(capacityFrames) {
    if (capacityFrames == 0) throw std::invalid_argument("Audio queue capacity must be positive");
}

std::size_t FrameQueue::push(std::span<const Frame> input) noexcept {
    // Reading read_ with acquire synchronizes with completion of consumer reads
    // before any reused ring slots are written.
    const auto write = write_.load(std::memory_order_relaxed);
    const auto read = read_.load(std::memory_order_acquire);
    const auto occupied = static_cast<std::size_t>(write - read);
    const auto available = frames_.size() - std::min(occupied, frames_.size());
    const auto count = std::min(input.size(), available);
    const auto offset = static_cast<std::size_t>(write % frames_.size());
    const auto first = std::min(count, frames_.size() - offset);
    std::copy_n(input.begin(), first, frames_.begin() + offset);
    if (count > first) std::copy_n(input.begin() + first, count - first, frames_.begin());
    write_.store(write + count, std::memory_order_release);
    if (count < input.size()) {
        dropped_.fetch_add(input.size() - count, std::memory_order_relaxed);
        overruns_.fetch_add(1, std::memory_order_relaxed);
    }
    return count;
}

void FrameQueue::acknowledgeClear() noexcept {
    const auto requested = clearRequested_.load(std::memory_order_acquire);
    if (requested != clearAcknowledged_.load(std::memory_order_relaxed)) {
        read_.store(write_.load(std::memory_order_acquire), std::memory_order_release);
        clearAcknowledged_.store(requested, std::memory_order_release);
    }
}

std::size_t FrameQueue::pop(std::span<Frame> output) noexcept {
    acknowledgeClear();
    const auto read = read_.load(std::memory_order_relaxed);
    const auto write = write_.load(std::memory_order_acquire);
    const auto count = std::min(output.size(), static_cast<std::size_t>(write - read));
    const auto offset = static_cast<std::size_t>(read % frames_.size());
    const auto first = std::min(count, frames_.size() - offset);
    std::copy_n(frames_.begin() + offset, first, output.begin());
    if (count > first) std::copy_n(frames_.begin(), count - first, output.begin() + first);
    read_.store(read + count, std::memory_order_release);
    if (count < output.size()) {
        missing_.fetch_add(output.size() - count, std::memory_order_relaxed);
        underruns_.fetch_add(1, std::memory_order_relaxed);
    }
    return count;
}

std::size_t FrameQueue::size() const noexcept {
    // Read first: a simultaneous consumer clear can otherwise make an older
    // write snapshot smaller than read. An observer may still see an approximate
    // size, but never an underflowed count.
    const auto read = read_.load(std::memory_order_acquire);
    const auto write = write_.load(std::memory_order_acquire);
    return static_cast<std::size_t>(std::min<std::uint64_t>(write - read, frames_.size()));
}

void FrameQueue::clear() noexcept {
    const auto requested = clearRequested_.load(std::memory_order_acquire);
    read_.store(write_.load(std::memory_order_acquire), std::memory_order_release);
    clearAcknowledged_.store(requested, std::memory_order_release);
}

void FrameQueue::requestClear() noexcept {
    clearRequested_.fetch_add(1, std::memory_order_release);
}

bool FrameQueue::clearPending() const noexcept {
    return clearRequested_.load(std::memory_order_acquire) !=
           clearAcknowledged_.load(std::memory_order_acquire);
}

std::uint64_t FrameQueue::droppedFrames() const noexcept { return dropped_.load(std::memory_order_relaxed); }
std::uint64_t FrameQueue::underrunFrames() const noexcept { return missing_.load(std::memory_order_relaxed); }
std::uint64_t FrameQueue::overrunEvents() const noexcept { return overruns_.load(std::memory_order_relaxed); }
std::uint64_t FrameQueue::underrunEvents() const noexcept { return underruns_.load(std::memory_order_relaxed); }

double DriftController::update(double queueFrames, double targetFrames, double elapsedSeconds) noexcept {
    if (!std::isfinite(queueFrames) || !std::isfinite(targetFrames) ||
        !std::isfinite(elapsedSeconds) || targetFrames <= 0.0) {
        reset();
        return correction_;
    }
    if (elapsedSeconds <= 0.0) return correction_;
    const double dt = std::min(elapsedSeconds, 0.25);
    const double error = std::clamp((queueFrames - targetFrames) / targetFrames, -4.0, 4.0);
    const double smoothing = 1.0 - std::exp(-dt / 0.4);
    filteredError_ += smoothing * (error - filteredError_);
    integral_ = std::clamp(integral_ + filteredError_ * dt * 0.0006, -0.0015, 0.0015);
    const double desired = std::clamp(1.0 - filteredError_ * 0.001 - integral_,
                                      1.0 - maximumDrift, 1.0 + maximumDrift);
    const double maximumStep = 0.0005 * dt;
    correction_ += std::clamp(desired - correction_, -maximumStep, maximumStep);
    return correction_;
}

void DriftController::reset() noexcept {
    filteredError_ = 0.0;
    integral_ = 0.0;
    correction_ = 1.0;
}

struct AdaptiveResampler::Impl {
    SRC_STATE* state{};
    std::size_t frameCapacity{};
    std::vector<float> input;
    std::vector<float> output;
    std::size_t pending{};
    double baseRatio{};
    double activeRatio{};
    int error{};
    std::uint64_t dropped{};

    static std::size_t validateCapacity(std::size_t capacity) {
        if (capacity == 0 ||
            capacity > static_cast<std::size_t>(std::numeric_limits<long>::max()) / 2 ||
            capacity > std::numeric_limits<std::size_t>::max() / (2 * sizeof(float)))
            throw std::invalid_argument("Invalid audio resampler dimensions");
        return capacity;
    }

    Impl(std::uint32_t inputRate, std::uint32_t outputRate, std::size_t capacity)
        : frameCapacity(validateCapacity(capacity)), input(frameCapacity * 2), output(frameCapacity * 2) {
        if (inputRate == 0 || outputRate == 0)
            throw std::invalid_argument("Audio sample rates must be positive");
        baseRatio = static_cast<double>(outputRate) / inputRate;
        activeRatio = baseRatio;
        if (!src_is_valid_ratio(baseRatio * (1.0 - maximumDrift)) ||
            !src_is_valid_ratio(baseRatio * (1.0 + maximumDrift)))
            throw std::invalid_argument("Unsupported audio sample-rate ratio");
        state = src_new(SRC_SINC_FASTEST, 2, &error);
        if (!state) throw std::runtime_error(std::string("Cannot create resampler: ") + src_strerror(error));
        error = src_set_ratio(state, baseRatio);
        if (error != 0) {
            src_delete(state);
            state = nullptr;
            throw std::runtime_error(std::string("Cannot configure resampler: ") + src_strerror(error));
        }
    }

    ~Impl() { if (state) src_delete(state); }
};

AdaptiveResampler::AdaptiveResampler(std::uint32_t inputRate, std::uint32_t outputRate,
                                   std::size_t maximumBufferedFrames)
    : impl_(std::make_unique<Impl>(inputRate, outputRate, maximumBufferedFrames)) {}
AdaptiveResampler::~AdaptiveResampler() = default;

std::size_t AdaptiveResampler::push(std::span<const Frame> input) noexcept {
    const auto accepted = std::min(input.size(), impl_->frameCapacity - impl_->pending);
    for (std::size_t i = 0; i < accepted; ++i) {
        impl_->input[(impl_->pending + i) * 2] = clean(input[i].left);
        impl_->input[(impl_->pending + i) * 2 + 1] = clean(input[i].right);
    }
    impl_->pending += accepted;
    impl_->dropped += input.size() - accepted;
    return accepted;
}

std::size_t AdaptiveResampler::produce(std::span<Frame> output, double correction) noexcept {
    if (!std::isfinite(correction)) correction = 1.0;
    const auto requestedRatio = impl_->baseRatio *
        std::clamp(correction, 1.0 - maximumDrift, 1.0 + maximumDrift);
    std::size_t generated{};
    while (generated < output.size()) {
        const auto requested = std::min(output.size() - generated, impl_->frameCapacity);
        SRC_DATA data{};
        data.data_in = impl_->input.data();
        data.data_out = impl_->output.data();
        data.input_frames = static_cast<long>(impl_->pending);
        data.output_frames = static_cast<long>(requested);
        data.src_ratio = requestedRatio;
        data.end_of_input = 0;
        impl_->error = src_process(impl_->state, &data);
        if (impl_->error != 0) break;
        impl_->activeRatio = requestedRatio;
        const auto used = static_cast<std::size_t>(data.input_frames_used);
        const auto made = static_cast<std::size_t>(data.output_frames_gen);
        for (std::size_t i = 0; i < made; ++i) {
            output[generated + i] = {clean(impl_->output[i * 2]), clean(impl_->output[i * 2 + 1])};
        }
        impl_->pending -= used;
        if (used != 0 && impl_->pending != 0)
            std::memmove(impl_->input.data(), impl_->input.data() + used * 2,
                         impl_->pending * 2 * sizeof(float));
        generated += made;
        if (made == 0 && used == 0) break;
    }
    return generated;
}

void AdaptiveResampler::reset() noexcept {
    src_reset(impl_->state);
    impl_->pending = 0;
    impl_->activeRatio = impl_->baseRatio;
    impl_->error = src_set_ratio(impl_->state, impl_->baseRatio);
}

std::size_t AdaptiveResampler::pendingInputFrames() const noexcept { return impl_->pending; }
std::size_t AdaptiveResampler::capacity() const noexcept { return impl_->frameCapacity; }
double AdaptiveResampler::ratio() const noexcept { return impl_->activeRatio; }
int AdaptiveResampler::lastError() const noexcept { return impl_->error; }
std::uint64_t AdaptiveResampler::droppedInputFrames() const noexcept { return impl_->dropped; }

void Mixer::Ramp::set(float value) noexcept {
    if (value == target) return;
    target = value;
    step = (target - current) / static_cast<float>(rampFrames);
    remaining = rampFrames;
}

float Mixer::Ramp::next() noexcept {
    if (remaining != 0) {
        current += step;
        if (--remaining == 0) current = target;
    }
    return current;
}

MixMeters Mixer::process(const std::array<std::span<const Frame>, sourceCount>& inputs,
                        std::span<Frame> outputA, std::span<Frame> outputB,
                        const Configuration& config) noexcept {
    for (std::size_t source = 0; source < sourceCount; ++source) {
        const auto& control = config.sources[source];
        sendsA_[source].set(!control.muted && control.a.enabled ? gain(control.a.gainDb) : 0.0f);
        sendsB_[source].set(!control.muted && control.b.enabled ? gain(control.b.gainDb) : 0.0f);
    }
    masterA_.set(config.busA.muted ? 0.0f : gain(config.busA.gainDb));
    masterB_.set(config.busB.muted ? 0.0f : gain(config.busB.gainDb));

    MixMeters meters{};
    const auto frames = std::max(outputA.size(), outputB.size());
    for (std::size_t frame = 0; frame < frames; ++frame) {
        double leftA{}, rightA{}, leftB{}, rightB{};
        for (std::size_t source = 0; source < sourceCount; ++source) {
            const auto sample = frame < inputs[source].size() ? inputs[source][frame] : Frame{};
            const float left = clean(sample.left);
            const float right = clean(sample.right);
            meters.sourcePeaks[source] = std::max(meters.sourcePeaks[source],
                                                 std::min(1.0f, std::max(std::abs(left), std::abs(right))));
            const auto sendA = sendsA_[source].next();
            const auto sendB = sendsB_[source].next();
            leftA += static_cast<double>(left) * sendA;
            rightA += static_cast<double>(right) * sendA;
            leftB += static_cast<double>(left) * sendB;
            rightB += static_cast<double>(right) * sendB;
        }
        const auto masterA = masterA_.next();
        const auto masterB = masterB_.next();
        leftA *= masterA;
        rightA *= masterA;
        leftB *= masterB;
        rightB *= masterB;
        meters.peakA = std::max(meters.peakA, std::max(safePeak(leftA), safePeak(rightA)));
        meters.peakB = std::max(meters.peakB, std::max(safePeak(leftB), safePeak(rightB)));
        meters.clipA = meters.clipA || std::abs(leftA) > 1.0 || std::abs(rightA) > 1.0;
        meters.clipB = meters.clipB || std::abs(leftB) > 1.0 || std::abs(rightB) > 1.0;
        if (frame < outputA.size()) outputA[frame] = {limit(leftA), limit(rightA)};
        if (frame < outputB.size()) outputB[frame] = {limit(leftB), limit(rightB)};
    }
    return meters;
}

void Mixer::reset() noexcept {
    sendsA_ = {};
    sendsB_ = {};
    masterA_ = {};
    masterB_ = {};
}

} // namespace am
