#pragma once
#include "am/types.hpp"
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <complex>
#include <numbers>
#include <span>

namespace am {
inline constexpr std::size_t spectrumFftFrames = 4096;
inline constexpr std::size_t spectrumBinCount = 128;
inline constexpr std::size_t spectrumHopFrames = 2400; // 20 Hz, independent of UI polling.
inline constexpr float spectrumFloorDb = -90;

// One producer (the mixer) and one consumer (snapshot(), serialized by the
// configuration mutex). Each side exclusively owns one slot. Exchanging the
// middle index transfers ownership, so the consumer never reads a slot while
// the producer writes it. The producer overwrites only its own/middle slots:
// a stalled UI loses intermediate views, never audio time or boundedness.
class SpectrumMailbox {
public:
    struct Window {
        std::array<Frame, spectrumFftFrames> samples{};
        std::uint64_t frame{}, revision{};
    };
    Window& writable() noexcept { return slots_[producer_]; }
    void publish() noexcept {
        producer_ = middle_.exchange(producer_ | dirty, std::memory_order_acq_rel) & indexMask;
    }
    const Window* consume() noexcept {
        if (!(middle_.load(std::memory_order_acquire) & dirty)) return nullptr;
        consumer_ = middle_.exchange(consumer_, std::memory_order_acq_rel) & indexMask;
        return &slots_[consumer_];
    }
private:
    static constexpr unsigned dirty = 4, indexMask = 3;
    static_assert(std::atomic<unsigned>::is_always_lock_free);
    std::array<Window, 3> slots_{};
    unsigned producer_ = 2, consumer_ = 0;
    std::atomic<unsigned> middle_{1};
};

// FFT and display smoothing run only on the control/snapshot thread. push()
// copies bounded audio into preallocated storage; it never allocates, locks,
// waits, performs FFT work, or retains a pointer to a GraphProcessor buffer.
class LiveSpectrum {
public:
    struct View {
        std::array<float, spectrumBinCount> db{};
        std::uint64_t frame{}, revision{};
    };
    explicit LiveSpectrum(std::uint64_t revision = 1) noexcept : producerRevision_(revision) {
        view_.db.fill(spectrumFloorDb);
        view_.revision = revision;
        for (std::size_t n = 0; n < spectrumFftFrames; ++n) {
            window_[n] = .5 - .5 * std::cos(2 * std::numbers::pi * n / spectrumFftFrames);
            windowSum_ += window_[n];
            std::size_t source = n, reverse{};
            for (unsigned bit = 0; bit < 12; ++bit) { reverse = (reverse << 1) | (source & 1); source >>= 1; }
            reversed_[n] = reverse;
        }
        for (std::size_t n = 0; n < spectrumFftFrames / 2; ++n) {
            const double angle = -2 * std::numbers::pi * n / spectrumFftFrames;
            twiddles_[n] = {std::cos(angle), std::sin(angle)};
        }
        const double halfStep = std::pow(1000., .5 / (spectrumBinCount - 1));
        for (std::size_t n = 0; n < spectrumBinCount; ++n) {
            const double frequency = frequencyHz(n);
            limits_[n] = {frequency / halfStep * spectrumFftFrames / sampleRate,
                frequency * halfStep * spectrumFftFrames / sampleRate};
        }
    }
    static double frequencyHz(std::size_t index) noexcept {
        return 20 * std::pow(1000., static_cast<double>(index) / (spectrumBinCount - 1));
    }
    // Only the audio thread calls reset/push. Call reset after suspend or a
    // discontinuity, with a new engine-wide token. Empty publication clears a
    // previous display immediately, before a fresh complete window is ready.
    void reset(std::uint64_t revision) noexcept {
        write_ = filled_ = sincePublish_ = 0;
        producerFrame_ = 0;
        producerRevision_ = revision;
        auto& slot = mailbox_.writable(); slot.frame = 0; slot.revision = revision;
        mailbox_.publish();
    }
    void push(std::span<const Frame> samples, std::uint64_t endFrame) noexcept {
        // A missing quantum cannot be bridged into an apparently continuous
        // FFT. Engine suspend explicitly supplies a new revision as well.
        if (producerFrame_ && (endFrame < samples.size() || producerFrame_ != endFrame - samples.size())) {
            write_ = filled_ = sincePublish_ = 0;
            auto& slot = mailbox_.writable(); slot.frame = 0; slot.revision = producerRevision_;
            mailbox_.publish();
        }
        for (const auto sample : samples) {
            ring_[write_] = {std::isfinite(sample.left) ? sample.left : 0.f,
                std::isfinite(sample.right) ? sample.right : 0.f};
            write_ = (write_ + 1) & (spectrumFftFrames - 1);
        }
        filled_ = std::min(spectrumFftFrames, filled_ + samples.size());
        sincePublish_ += samples.size();
        producerFrame_ = endFrame;
        if (filled_ != spectrumFftFrames || sincePublish_ < spectrumHopFrames) return;
        sincePublish_ = 0;
        auto& slot = mailbox_.writable();
        const auto tail = spectrumFftFrames - write_;
        std::copy_n(ring_.begin() + write_, tail, slot.samples.begin());
        std::copy_n(ring_.begin(), write_, slot.samples.begin() + tail);
        slot.frame = endFrame; slot.revision = producerRevision_;
        mailbox_.publish();
    }
    // Serialized control-thread consumer. A newer completed window always
    // wins; duplicate snapshot requests do no FFT work or smoothing.
    const View& read() noexcept {
        const auto* incoming = mailbox_.consume();
        if (!incoming) return view_;
        if (!incoming->frame) {
            view_.db.fill(spectrumFloorDb); view_.frame = 0; view_.revision = incoming->revision;
            return view_;
        }
        const bool fresh = !view_.frame || view_.revision != incoming->revision
            || incoming->frame <= view_.frame || incoming->frame - view_.frame > sampleRate / 4;
        for (std::size_t n = 0; n < spectrumFftFrames; ++n)
            fft_[reversed_[n]] = {incoming->samples[n].left * window_[n], incoming->samples[n].right * window_[n]};
        for (std::size_t width = 2; width <= spectrumFftFrames; width *= 2) {
            const auto half = width / 2, stride = spectrumFftFrames / width;
            for (std::size_t base = 0; base < spectrumFftFrames; base += width)
                for (std::size_t n = 0; n < half; ++n) {
                    const auto even = fft_[base + n];
                    const auto odd = fft_[base + n + half] * twiddles_[n * stride];
                    fft_[base + n] = even + odd; fft_[base + n + half] = even - odd;
                }
        }
        // Packing L+iR allows a single complex FFT. The expression below is
        // the mean of independently measured channel powers, corrected for
        // Hann coherent gain and the one-sided sinusoidal amplitude. Opposite
        // polarity stereo cannot cancel as it would with an L+R mono sum.
        for (std::size_t k = 1; k < spectrumFftFrames / 2; ++k)
            powers_[k] = (std::norm(fft_[k]) + std::norm(fft_[spectrumFftFrames - k])) / (windowSum_ * windowSum_);
        powers_[0] = powers_[1];
        powers_[spectrumFftFrames / 2] = powers_[spectrumFftFrames / 2 - 1];
        const double seconds = fresh ? 0 : static_cast<double>(incoming->frame - view_.frame) / sampleRate;
        const double attack = std::exp(-seconds / .035), release = std::exp(-seconds / .180);
        for (std::size_t n = 0; n < spectrumBinCount; ++n) {
            const auto [low, high] = limits_[n];
            double power = std::max(interpolatePower(low), interpolatePower(high));
            for (auto k = static_cast<std::size_t>(std::ceil(low)); k <= static_cast<std::size_t>(high); ++k)
                power = std::max(power, powers_[k]);
            const float measured = static_cast<float>(std::clamp(10 * std::log10(std::max(power, 1e-9)), -90., 0.));
            const double smoothing = measured > view_.db[n] ? attack : release;
            view_.db[n] = fresh ? measured : static_cast<float>(measured + smoothing * (view_.db[n] - measured));
        }
        view_.frame = incoming->frame; view_.revision = incoming->revision;
        return view_;
    }
private:
    double interpolatePower(double position) const noexcept {
        const auto lower = static_cast<std::size_t>(position);
        return powers_[lower] + (powers_[lower + 1] - powers_[lower]) * (position - lower);
    }
    struct Limits { double low{}, high{}; };
    SpectrumMailbox mailbox_;
    // Producer-owned state.
    std::array<Frame, spectrumFftFrames> ring_{};
    std::size_t write_{}, filled_{}, sincePublish_{};
    std::uint64_t producerFrame_{}, producerRevision_{};
    // Consumer-owned state; immutable tables are initialized off the mixer.
    View view_;
    std::array<double, spectrumFftFrames> window_{};
    std::array<std::size_t, spectrumFftFrames> reversed_{};
    std::array<std::complex<double>, spectrumFftFrames> fft_{};
    std::array<std::complex<double>, spectrumFftFrames / 2> twiddles_{};
    std::array<double, spectrumFftFrames / 2 + 1> powers_{};
    std::array<Limits, spectrumBinCount> limits_{};
    double windowSum_{};
};
}
