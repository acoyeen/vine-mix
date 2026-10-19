#include "am/audio_timing.hpp"
#include "am/dsp.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

// Entirely synthetic scheduling and the production sinc resampler. No Windows
// audio clients, endpoints, configuration files or playback are accessed.
namespace {
using am::Frame;
std::uint64_t checks{};
void require(bool value, const std::string& message) {
    ++checks;
    if (!value) throw std::runtime_error(message);
}

void policyBudgets() {
    bool rejected{};
    try { am::StreamBufferPolicy invalid(0, 128, false); }
    catch (const std::invalid_argument&) { rejected = true; }
    require(rejected, "A zero device rate is rejected");
    for (const auto rate : {44100u, 48000u, 96000u}) {
        for (const auto period : {rate / 1000, rate / 200, rate / 100, rate / 50}) {
            for (const bool capture : {false, true}) {
                am::StreamBufferPolicy policy(rate, period, capture);
                const auto converted = static_cast<std::size_t>(std::ceil(double(period) * am::sampleRate / rate));
                require(policy.deviceFramesToEngineFrames(period) == converted,
                    "Periods round upward in graph frame units");
                const auto initial = policy.targetFrames();
                require(initial == (capture ? am::StreamBufferPolicy::captureReserveFrames
                    : std::max(converted, am::quantumFrames) + am::quantumFrames + 64),
                    "Initial queue budget follows graph and device periods");
                if (!capture) require(policy.primeFrames(1200) >= 1504,
                    "A large available callback overrides normal priming budget");
                for (int i = 0; i < 100; ++i) {
                    const auto before = policy.targetFrames();
                    policy.observeUnderrun();
                    require(policy.targetFrames() >= before && policy.targetFrames() - before <= am::quantumFrames,
                        "Recovery grows by no more than one graph block per shortfall");
                    require(policy.targetFrames() <= policy.maximumTargetFrames(), "Recovery remains bounded");
                }
                require(policy.targetFrames() == policy.maximumTargetFrames() && !policy.observeUnderrun(),
                    "Repeated shortfalls saturate the budget");
            }
        }
    }
    am::StreamBufferPolicy fallback(44100, 0, false);
    require(fallback.targetFrames() == 480 + am::quantumFrames + 64,
        "Unknown legacy periods retain a 10 ms device assumption");
    am::StreamBufferPolicy large(48000, 4800, false);
    require(large.targetFrames() >= large.deviceFramesToEngineFrames(4800),
        "Long device periods are never capped below their mandatory callback budget");
    am::StreamBufferPolicy lowCapture(48000, 128, true), lowRender(48000, 128, false);
    require(lowCapture.targetFrames() + lowRender.targetFrames() < 2 * 720,
        "Short-period capture/render targets materially improve on two legacy 15 ms targets");
    require(lowRender.primeFrames(std::numeric_limits<std::size_t>::max())
            == std::numeric_limits<std::size_t>::max(), "Extreme callback demand does not wrap the priming budget");
    std::cout << "PASS period conversion, low-latency budgets, demand-aware priming and bounded recovery\n";
}

void capturePriming() {
    am::FrameQueue queue(8192);
    am::CaptureBufferReader reader;
    std::array<Frame, am::quantumFrames> output, input;
    input.fill({.1f, -.1f});
    const auto silent = [&] {
        return std::all_of(output.begin(), output.end(), [](Frame f) { return f.left == 0 && f.right == 0; });
    };
    output.fill({1, 1});
    require(reader.read(queue, output, true) == 0 && silent() && !reader.primed(),
        "Empty capture startup produces silence and stays unprimed");
    queue.push(input);
    require(reader.read(queue, output, true) == 0 && silent() && queue.size() == input.size(),
        "A single capture block waits for one block of reserve");
    require(queue.underrunEvents() == 0, "Waiting for capture pre-roll does not record an underrun");
    queue.push(input);
    require(reader.read(queue, output, true) == 0 && !reader.primed(),
        "Capture waits for the full 7.5 ms reserve");
    queue.push(input);
    require(reader.read(queue, output, true) == output.size() && reader.primed()
            && queue.size() == 2 * am::quantumFrames && output.front().left == .1f,
        "Capture starts with at least 7.5 ms of remaining reserve");
    require(reader.read(queue, output, true) == output.size() && reader.primed(),
        "A primed capture path continues without adding repeated pre-roll");
    reader.read(queue, output, true);
    queue.push(std::span(input).first(31));
    require(reader.read(queue, output, true) == 31 && !reader.primed()
            && output[30].left == .1f && output[31].left == 0 && output.back().right == 0,
        "A real partial capture block preserves available audio and zero fills its tail");
    require(queue.underrunEvents() == 1, "A real running capture shortfall retains queue telemetry");
    queue.push(input); queue.push(input); queue.push(input);
    require(reader.read(queue, output, true) == output.size() && reader.primed(),
        "Capture resumes after rebuilding one block of reserve");
    queue.requestClear(); queue.push(input);
    require(reader.read(queue, output, true) == 0 && silent() && !reader.primed()
            && queue.size() == 0 && !queue.clearPending(),
        "Producer discontinuity flushes old audio and restarts capture pre-roll");
    queue.push(input); queue.push(input); queue.push(input);
    reader.read(queue, output, true);
    require(reader.read(queue, output, false) == 0 && silent() && !reader.primed() && queue.size() == 0,
        "A disconnected capture path clears audio and priming state");
    std::vector<Frame> stale(am::sampleRate / 10 + 1, {.1f, -.1f}); queue.push(stale);
    require(reader.read(queue, output, true) == 0 && silent() && queue.size() == 0,
        "A capture backlog over 100 ms is discarded instead of replaying old speech");
    const std::vector<Frame> largePacket(960, {.1f, -.1f});
    queue.push(largePacket);
    require(reader.read(queue, output, true, 960) == 0 && queue.size() == 960,
        "A 20 ms capture device waits for a full packet period plus reserve");
    queue.push(input); queue.push(input);
    require(reader.read(queue, output, true, 960) == output.size() && reader.primed()
            && queue.size() >= 960 + am::StreamBufferPolicy::captureReserveFrames - output.size(),
        "Large packet capture starts with enough reserve to bridge its next callback");
    queue.clear(); reader.reset();
    queue.push(input); queue.push(input); queue.push(input);
    require(reader.read(queue, output, true, 240, 720) == 0 && queue.size() == 720,
        "Recovery waits for an adaptively increased capture reserve");
    queue.push(input);
    require(reader.read(queue, output, true, 240, 720) == output.size() && queue.size() == 720,
        "Re-priming establishes the larger reserve without waiting for slow drift feedback");
    std::cout << "PASS capture pre-roll, partial-read silence, readiness, discontinuity and stale-backlog recovery\n";
}

void cleanSignal(std::span<const Frame> frames) {
    require(std::all_of(frames.begin(), frames.end(), [](const Frame& frame) {
        return std::isfinite(frame.left) && std::isfinite(frame.right)
            && std::abs(frame.left - .1f) < .002f && std::abs(frame.right + .1f) < .002f;
    }), "The asynchronous sinc stream preserves stereo DC amplitude");
}

struct Result {
    std::size_t underruns{}, adaptations{}, maximum{}, finalTarget{};
    double meanOccupancy{};
};

Result renderSimulation(std::uint32_t rate, std::uint32_t period, double phase,
                        double ppm, bool injectStall, std::size_t targetOverride = 0,
                        bool batchedCallbacks = false) {
    constexpr double duration = 24, mixerPeriod = double(am::quantumFrames) / am::sampleRate;
    am::StreamBufferPolicy policy(rate, period, false);
    am::FrameQueue queue(16384);
    am::AdaptiveResampler resampler(am::sampleRate, rate, 8192);
    am::DriftController drift;
    std::array<Frame, am::quantumFrames> mixed; mixed.fill({.1f, -.1f});
    std::array<Frame, 8192> transfer, output;
    double nextMix = 0, nextDevice = phase, lastDevice = phase;
    double clockMultiplier = 1, nextClockEstimate = phase + .5;
    const double devicePeriod = double(period) / rate / (1 + ppm * 1e-6);
    bool primed{}, stalled{};
    std::uint32_t callbackIndex{}, callbackDemand = batchedCallbacks ? period * 3 : period;
    Result result;
    std::size_t occupancySamples{};
    while (std::min(nextMix, nextDevice) < duration) {
        if (nextMix <= nextDevice) {
            require(queue.push(mixed) == mixed.size(), "Async render producer never overflows");
            nextMix += mixerPeriod;
            // One deliberately missed 20 ms producer interval, then normal
            // cadence resumes. This must adapt once and recover, not spin.
            if (injectStall && !stalled && nextMix > 8) { nextMix += .020; stalled = true; }
            continue;
        }
        const double now = nextDevice;
        const double elapsed = std::max(.0001, now - lastDevice);
        lastDevice = now;
        const auto demand = callbackDemand;
        ++callbackIndex;
        const auto nextFactor = batchedCallbacks && callbackIndex % 89 == 0 ? 3u
            : batchedCallbacks && callbackIndex % 47 == 0 ? 2u : 1u;
        callbackDemand = period * nextFactor;
        nextDevice += devicePeriod * nextFactor;
        if (now >= nextClockEstimate) {
            clockMultiplier += ((1 + ppm * 1e-6) - clockMultiplier) * .08;
            nextClockEstimate = now + .5;
        }
        const auto occupancy = queue.size() + resampler.pendingInputFrames();
        const auto target = targetOverride ? targetOverride : policy.targetFrames();
        const auto prime = targetOverride ? std::max<std::size_t>(480, policy.deviceFramesToEngineFrames(demand))
                                         : policy.primeFrames(policy.deviceFramesToEngineFrames(demand));
        if (!primed && occupancy >= prime) primed = true;
        const double correction = drift.update(double(occupancy), double(target), elapsed) * clockMultiplier;
        std::size_t filled{};
        if (primed) while (filled < demand) {
            filled += resampler.produce(std::span(output).subspan(filled, demand - filled), correction);
            require(resampler.lastError() == 0, "Render SRC remains healthy");
            if (filled == demand) break;
            const auto count = std::min(queue.size(), transfer.size());
            if (!count) break;
            const auto read = queue.pop(std::span(transfer).first(count));
            require(resampler.push(std::span(transfer).first(read)) == read, "Render SRC retains all input");
        }
        if (primed && filled < demand) {
            ++result.underruns;
            result.adaptations += policy.observeUnderrun();
            primed = false;
        }
        if (now > 1 && filled == demand) cleanSignal(std::span(output).first(filled));
        const auto after = queue.size() + resampler.pendingInputFrames();
        result.maximum = std::max(result.maximum, after);
        if (now > duration - 4) { result.meanOccupancy += after; ++occupancySamples; }
    }
    require(queue.droppedFrames() == 0 && resampler.droppedInputFrames() == 0,
        "Asynchronous rendering drops no samples");
    result.finalTarget = targetOverride ? targetOverride : policy.targetFrames();
    result.meanOccupancy /= occupancySamples;
    return result;
}

Result captureSimulation(std::uint32_t rate, std::uint32_t period, double phase, double ppm,
                         std::size_t targetOverride = 0) {
    constexpr double duration = 24, mixerPeriod = double(am::quantumFrames) / am::sampleRate;
    am::StreamBufferPolicy policy(rate, period, true);
    am::FrameQueue queue(16384);
    am::AdaptiveResampler resampler(rate, am::sampleRate, 8192);
    am::DriftController drift;
    am::CaptureBufferReader reader;
    std::array<Frame, 8192> packet, converted;
    packet.fill({.1f, -.1f});
    std::array<Frame, am::quantumFrames> consumed;
    double nextMix = 0, nextDevice = phase, lastDevice = phase;
    double clockMultiplier = 1, nextClockEstimate = phase + .5;
    const double devicePeriod = double(period) / rate / (1 + ppm * 1e-6);
    Result result;
    std::size_t occupancySamples{};
    while (std::min(nextMix, nextDevice) < duration) {
        if (nextMix <= nextDevice) {
            const auto now = nextMix;
            const bool wasPrimed = reader.primed();
            const auto read = reader.read(queue, consumed, true, policy.deviceFramesToEngineFrames(period),
                targetOverride ? targetOverride : policy.targetFrames());
            if (wasPrimed && read != consumed.size()) {
                ++result.underruns;
                result.adaptations += policy.observeUnderrun();
            }
            if (now > 1 && read == consumed.size()) cleanSignal(consumed);
            nextMix += mixerPeriod;
            continue;
        }
        const double now = nextDevice;
        const double elapsed = std::max(.0001, now - lastDevice);
        lastDevice = now;
        nextDevice += devicePeriod;
        if (now >= nextClockEstimate) {
            clockMultiplier += ((1 + ppm * 1e-6) - clockMultiplier) * .08;
            nextClockEstimate = now + .5;
        }
        const double correction = drift.update(double(queue.size()), double(targetOverride ? targetOverride : policy.targetFrames()), elapsed) / clockMultiplier;
        require(resampler.push(std::span(packet).first(period)) == period, "Capture SRC retains every device packet");
        for (;;) {
            const auto made = resampler.produce(converted, correction);
            require(resampler.lastError() == 0, "Capture SRC remains healthy");
            if (!made) break;
            require(queue.push(std::span(converted).first(made)) == made, "Async capture queue never overflows");
        }
        result.maximum = std::max(result.maximum, queue.size());
        if (now > duration - 4) { result.meanOccupancy += queue.size(); ++occupancySamples; }
    }
    require(queue.droppedFrames() == 0 && resampler.droppedInputFrames() == 0,
        "Asynchronous capture drops no input");
    result.finalTarget = targetOverride ? targetOverride : policy.targetFrames();
    result.meanOccupancy /= occupancySamples;
    return result;
}

void asynchronousStreams() {
    std::size_t scenarios{};
    for (const auto rate : {44100u, 48000u, 96000u}) {
        for (const auto period : {rate / 1000, rate / 200, rate / 100, rate / 50}) {
            for (const auto phase : {.00013, .00371}) for (const auto ppm : {-750., 750.}) {
                const std::string context = " rate=" + std::to_string(rate) + " period=" + std::to_string(period)
                    + " phase=" + std::to_string(phase) + " ppm=" + std::to_string(ppm);
                const auto render = renderSimulation(rate, period, phase, ppm, false);
                require(render.underruns == 0, "Steady async render has no shortfall" + context);
                const auto capture = captureSimulation(rate, period, phase, ppm);
                require(capture.underruns == 0, "Primed async capture has no shortfall" + context);
                require(render.maximum < 4096 && capture.maximum < 4096, "Async queues remain bounded" + context);
                ++scenarios;
            }
        }
    }
    const auto recovery = renderSimulation(48000, 128, .00371, 750, true);
    require(recovery.underruns >= 1 && recovery.underruns <= 2,
        "A 20 ms producer stall gives one bounded underrun/re-prime episode");
    require(recovery.adaptations >= 1 && recovery.finalTarget > 544 && recovery.maximum < 4096,
        "A real shortfall increases headroom and streaming recovers");
    for (const auto rate : {44100u, 48000u, 96000u}) {
        const auto batched = renderSimulation(rate, rate / 200, .00371, -750, false, 0, true);
        require(batched.underruns == 0, "First-buffer and recurring 2x/3x render demands preserve streaming");
    }
    const auto capture = captureSimulation(48000, 128, .00371, 750);
    const auto render = renderSimulation(48000, 128, .00371, 750, false);
    const auto oldCapture = captureSimulation(48000, 128, .00371, 750, 720);
    const auto oldRender = renderSimulation(48000, 128, .00371, 750, false, 720);
    const double meanFrames = capture.meanOccupancy + render.meanOccupancy;
    const double oldMeanFrames = oldCapture.meanOccupancy + oldRender.meanOccupancy;
    require(meanFrames + 240 < oldMeanFrames,
        "Actual asynchronous SRC reduces representative mean application buffering by at least 5 ms");
    std::cout << "Synthetic 48k/128-frame app queue evidence: capture target=" << capture.finalTarget
        << ", mean=" << capture.meanOccupancy << "; render target=" << render.finalTarget
        << ", mean=" << render.meanOccupancy << "; total mean=" << meanFrames / 48
        << "ms versus legacy720-frame targets=" << oldMeanFrames / 48
        << "ms. Device/effect/acoustic latency is excluded.\n";
    std::cout << "PASS " << scenarios << " asynchronous capture+render cases at 44.1/48/96 kHz, 1/5/10/20 ms periods, "
        "independent phases and +/-750 ppm; producer-stall recovery\n";
}
} // namespace

int main() {
    try {
        policyBudgets(); capturePriming(); asynchronousStreams();
        std::cout << "PASS " << checks << " assertions; synthetic only, no audio devices or settings accessed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL after " << checks << " assertions: " << error.what() << '\n';
        return 1;
    }
}
