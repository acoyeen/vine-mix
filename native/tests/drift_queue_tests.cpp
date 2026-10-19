#include "am/dsp.hpp"
#include <windows.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <chrono>
#include <cmath>
#include <deque>
#include <iomanip>
#include <iostream>
#include <limits>
#include <numbers>
#include <span>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

// Silent qualification: pure memory/DSP only. This executable does not link
// WASAPI, the audio engine, or configuration persistence.
namespace {
using am::Frame;
std::uint64_t checks{};
void expect(bool value, const std::string& message) {
    ++checks;
    if (!value) throw std::runtime_error(message);
}
void expectNear(double value, double expected, double tolerance, const std::string& message) {
    expect(std::isfinite(value) && std::abs(value - expected) <= tolerance,
        message + " actual=" + std::to_string(value) + " expected=" + std::to_string(expected));
}
template<class F> void rejects(F action, const char* message) {
    bool rejected = false;
    try { action(); } catch (const std::exception&) { rejected = true; }
    expect(rejected, message);
}
Frame sequence(std::size_t value) { return {static_cast<float>(value), -static_cast<float>(value)}; }
bool equal(Frame a, Frame b) { return a.left == b.left && a.right == b.right; }

void queueReference() {
    rejects([] { am::FrameQueue queue(0); }, "zero queue capacity rejected");
    for (const std::size_t capacity : {1, 3, 17, 257}) {
        am::FrameQueue queue(capacity);
        std::deque<Frame> reference;
        std::uint64_t dropped{}, missing{}, overruns{}, underruns{};
        std::size_t serial = 1;
        for (std::size_t step = 0; step < 4000; ++step) {
            const std::size_t amount = (step * 17 + step / 7) % (capacity + 11);
            if (step % 3 != 0) {
                std::vector<Frame> frames(amount);
                for (auto& frame : frames) frame = sequence(serial++);
                const auto accepted = std::min(amount, capacity - reference.size());
                expect(queue.push(frames) == accepted, "queue accepted count matches independent deque");
                for (std::size_t i = 0; i < accepted; ++i) reference.push_back(frames[i]);
                dropped += amount - accepted;
                overruns += accepted < amount;
            } else {
                const Frame sentinel{-123456, 654321};
                std::vector<Frame> frames(amount, sentinel);
                const auto returned = std::min(amount, reference.size());
                expect(queue.pop(frames) == returned, "queue returned count matches deque");
                bool order = true;
                for (std::size_t i = 0; i < returned; ++i) { order &= equal(frames[i], reference.front()); reference.pop_front(); }
                expect(order, "ring wrap preserves frame order and stereo pairing");
                expect(std::all_of(frames.begin() + returned, frames.end(), [&](Frame f) { return equal(f, sentinel); }),
                    "underrun leaves caller-owned remainder untouched");
                missing += amount - returned;
                underruns += returned < amount;
            }
            if (step % 191 == 0) { queue.clear(); reference.clear(); }
            if (step % 137 == 0) {
                queue.requestClear(); queue.requestClear();
                expect(queue.clearPending(), "repeated clear request remains pending until consumer acknowledges");
                expect(queue.pop({}) == 0, "empty consumer pop acknowledges deferred clear");
                reference.clear();
                expect(!queue.clearPending(), "consumer acknowledged latest request");
            }
            expect(queue.size() == reference.size(), "queue occupancy matches reference");
        }
        expect(queue.droppedFrames() == dropped && queue.overrunEvents() == overruns, "drop-new overflow telemetry exact");
        expect(queue.underrunFrames() == missing && queue.underrunEvents() == underruns, "underrun telemetry exact");
    }
    std::cout << "PASS queue reference: 16,000 operations, capacities 1/3/17/257, wrap/drop/clear/telemetry\n";
}

void queueConcurrent() {
    constexpr std::size_t total = 1000000;
    am::FrameQueue queue(257);
    std::atomic<bool> done{}, observerDone{};
    std::atomic<bool> sizeValid{true};
    std::vector<Frame> accepted, consumed;
    accepted.reserve(total); consumed.reserve(total);
    std::thread observer([&] { while (!observerDone.load(std::memory_order_acquire)) {
        if (queue.size() > queue.capacity()) sizeValid = false;
        std::this_thread::yield();
    }});
    std::thread producer([&] {
        std::array<Frame, 113> batch;
        std::size_t serial = 1;
        while (serial <= total) {
            const auto count = std::min<std::size_t>(1 + serial % batch.size(), total + 1 - serial);
            for (std::size_t i = 0; i < count; ++i) batch[i] = sequence(serial + i);
            const auto made = queue.push(std::span(batch).first(count));
            accepted.insert(accepted.end(), batch.begin(), batch.begin() + made);
            serial += count;
            if (serial % 11 < 5) std::this_thread::yield();
        }
        done.store(true, std::memory_order_release);
    });
    std::thread consumer([&] {
        std::array<Frame, 71> batch;
        while (!done.load(std::memory_order_acquire) || queue.size() != 0) {
            const auto read = queue.pop(batch);
            consumed.insert(consumed.end(), batch.begin(), batch.begin() + read);
            if (!read) std::this_thread::yield();
        }
    });
    producer.join(); consumer.join(); observerDone = true; observer.join();
    expect(sizeValid, "concurrent third-party occupancy never exceeds capacity");
    expect(!accepted.empty() && accepted.size() == consumed.size(), "all accepted concurrent samples consumed");
    expect(std::equal(accepted.begin(), accepted.end(), consumed.begin(), equal), "one million offered frames preserve exact accepted FIFO/stereo sequence under SPSC contention");
    expect(queue.droppedFrames() + consumed.size() == total, "concurrent drop-new accounting exact");
    expect(queue.size() == 0, "concurrent queue drains completely");

    // A third thread asks for flushes while producer and consumer are active.
    // Flushing intentionally loses samples, so monotonicity/pairing are the
    // independent oracle; exact delivery is tested in the preceding run.
    am::FrameQueue flushing(263);
    std::atomic<bool> finished{};
    std::atomic<bool> valid{true};
    std::size_t received{}, requests{};
    std::thread p([&] {
        std::array<Frame, 31> batch;
        for (std::size_t serial = 1; serial < total * 10; serial += batch.size()) {
            for (std::size_t i = 0; i < batch.size(); ++i) batch[i] = sequence(serial + i);
            flushing.push(batch);
            if (serial % 5 == 0) std::this_thread::yield();
        }
        finished = true;
    });
    std::thread c([&] {
        float previous{};
        std::array<Frame, 67> batch;
        while (!finished.load() || flushing.size()) {
            const auto read = flushing.pop(batch);
            for (std::size_t i = 0; i < read; ++i) {
                if (!(batch[i].left > previous && batch[i].right == -batch[i].left)) valid = false;
                previous = batch[i].left;
                ++received;
            }
            if (!read) std::this_thread::yield();
        }
        // A final request may race the exit condition, so acknowledge it after
        // the requesting thread has stopped below, on this same consumer.
    });
    while (!finished.load()) {
        flushing.requestClear(); ++requests;
        std::this_thread::sleep_for(std::chrono::microseconds(100));
    }
    p.join(); c.join();
    flushing.pop({}); // producer and consumer are joined; caller now owns consumer side.
    expect(valid && received != 0 && requests != 0, "concurrent requested flush never replays or tears samples (valid="
        + std::to_string(valid.load()) + ", received=" + std::to_string(received) + ", requests=" + std::to_string(requests) + ")");
    expect(!flushing.clearPending() && flushing.size() == 0, "all deferred flushes acknowledged after producer stops");
    std::cout << "PASS concurrent queues: 1,000,000 offered frames, " << consumed.size() << " delivered; "
        << requests << " flush requests, " << received << " frames observed under concurrent flush\n";
}

void controllerEdges() {
    am::DriftController drift;
    expectNear(drift.correction(), 1, 0, "initial correction neutral");
    double previous = 1;
    for (int step = 0; step < 10000; ++step) {
        const auto value = drift.update(2000, 720, .005);
        expect(value <= previous && value >= .998, "high occupancy reduces ratio within lower bound");
        expect(std::abs(value - previous) <= .0005 * .005 + 1e-12, "correction slews no faster than 500 ppm/s");
        previous = value;
    }
    expectNear(previous, .998, 1e-12, "sustained high occupancy reaches bounded correction");
    drift.reset(); previous = 1;
    for (int step = 0; step < 10000; ++step) {
        const auto value = drift.update(0, 720, .005);
        expect(value >= previous && value <= 1.002, "low occupancy raises ratio within upper bound");
        expect(std::abs(value - previous) <= .0005 * .005 + 1e-12, "positive correction slew bounded");
        previous = value;
    }
    expectNear(previous, 1.002, 1e-12, "sustained low occupancy reaches bounded correction");
    expectNear(drift.update(720, 720, 0), previous, 0, "zero elapsed time leaves correction unchanged");
    expectNear(drift.update(720, 720, -1), previous, 0, "negative elapsed time leaves correction unchanged");
    for (double bad : {std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity()}) {
        expectNear(drift.update(bad, 720, .005), 1, 0, "nonfinite queue resets controller");
        expectNear(drift.update(720, bad, .005), 1, 0, "nonfinite target resets controller");
        expectNear(drift.update(720, 720, bad), 1, 0, "nonfinite time resets controller");
    }
    expectNear(drift.update(720, 0, .005), 1, 0, "zero target resets controller");
    expectNear(drift.update(720, -1, .005), 1, 0, "negative target resets controller");
    drift.reset();
    const auto longDelay = drift.update(2000, 720, 1000);
    expect(std::abs(longDelay - 1) <= .0005 * .25 + 1e-12, "large time discontinuity clamps control elapsed interval");
    drift.reset(); expectNear(drift.correction(), 1, 0, "explicit reset neutral");
    std::cout << "PASS drift controller direction, +/-2,000 ppm limits, 500 ppm/s slew, invalid timing/reset\n";
}

void clockModels() {
    constexpr double dt = .005, duration = 3600, target = 720, capacity = 8192;
    constexpr int steps = static_cast<int>(duration / dt);
    double globalMin = capacity, globalMax{};
    for (bool render : {false, true}) for (double ppm : {-1500., -500., -100., 100., 500., 1500.}) {
        am::DriftController drift;
        double queue = target;
        for (int i = 0; i < steps; ++i) {
            const double clock = 1 + ppm * 1e-6;
            const double ratio = drift.update(queue, target, dt);
            // Independent conservation-of-frames model: capture changes samples
            // produced; render changes input samples needed for device demand.
            queue += render ? 48000 * dt - 48000 * clock * dt / ratio
                            : 48000 * clock * dt * ratio - 48000 * dt;
            globalMin = std::min(globalMin, queue); globalMax = std::max(globalMax, queue);
            if (!(queue > 0 && queue < capacity && std::isfinite(queue)))
                throw std::runtime_error("independent clock model escaped bounded buffer");
        }
        expect(true, "one-hour model maintained finite bounded queue at every 5ms step");
        // At -1,500 ppm capture drift the exact reciprocal exceeds the integral
        // limit very slightly, leaving a small bounded proportional residual.
        expectNear(queue, target, 3, "independent clock model returns within three frames of target occupancy");
        expectNear(drift.correction(), render ? 1 + ppm * 1e-6 : 1 / (1 + ppm * 1e-6), 2e-6,
             "converged correction cancels independently specified device clock");
        expect(std::abs(48000 * ppm * 1e-6 * duration) > capacity, "uncompensated reference would exhaust buffer");
    }
    std::cout << "PASS 12 independent clock models x 1 simulated hour (12 aggregate hours), capture/render +/-100/500/1500 ppm; queue range "
        << globalMin << ".." << globalMax << " frames (720 target, 8192 capacity)\n";
}

std::vector<Frame> resample(std::uint32_t inputRate, std::uint32_t outputRate, double seconds) {
    am::AdaptiveResampler adapter(inputRate, outputRate, 2048);
    const auto total = static_cast<std::size_t>(inputRate * seconds);
    std::vector<Frame> result;
    result.reserve(static_cast<std::size_t>(outputRate * seconds) + 500);
    std::array<Frame, 257> input;
    std::array<Frame, 617> output;
    for (std::size_t offset = 0; offset < total;) {
        const auto count = std::min(input.size(), total - offset);
        for (std::size_t i = 0; i < count; ++i) {
            const double time = static_cast<double>(offset + i) / inputRate;
            input[i] = {static_cast<float>(.25 * std::sin(2 * std::numbers::pi * 997 * time)),
                        static_cast<float>(.125 * std::sin(2 * std::numbers::pi * 2371 * time))};
        }
        expect(adapter.push(std::span(input).first(count)) == count, "streaming sample input accepted");
        for (;;) {
            const auto made = adapter.produce(output);
            result.insert(result.end(), output.begin(), output.begin() + made);
            if (!made) break;
        }
        expect(adapter.lastError() == 0 && adapter.pendingInputFrames() <= adapter.capacity(), "streaming adapter valid and bounded");
        offset += count;
    }
    expect(adapter.droppedInputFrames() == 0, "normal stream never drops accepted input");
    return result;
}

double magnitude(const std::vector<Frame>& samples, double rate, double frequency, bool right) {
    double cosine{}, sine{};
    const std::size_t start = 300;
    for (std::size_t i = start; i < samples.size(); ++i) {
        const double value = right ? samples[i].right : samples[i].left;
        const auto phase = 2 * std::numbers::pi * frequency * i / rate;
        cosine += value * std::cos(phase); sine += value * std::sin(phase);
    }
    return 2 * std::hypot(cosine, sine) / (samples.size() - start);
}

void resamplerQuality() {
    for (std::uint32_t input : {44100u, 48000u, 96000u}) for (std::uint32_t output : {44100u, 48000u, 96000u}) {
        const auto frames = resample(input, output, 1);
        expect(frames.size() >= output - 200 && frames.size() <= output + 1, "one second conversion has expected frame count with bounded filter lookahead");
        expect(std::all_of(frames.begin(), frames.end(), [](Frame f) { return std::isfinite(f.left) && std::isfinite(f.right); }), "resampled tone finite");
        expectNear(magnitude(frames, output, 997, false), .25, .002, "997 Hz left amplitude/frequency preserved");
        expectNear(magnitude(frames, output, 2371, true), .125, .002, "2371 Hz right amplitude/frequency preserved");
        expect(magnitude(frames, output, 2371, false) < .001 && magnitude(frames, output, 997, true) < .001, "stereo channels do not bleed");
        std::cout << "PASS resample " << input << " -> " << output << " Hz: " << frames.size() << " frames from 1 simulated second\n";
    }
    rejects([] { am::AdaptiveResampler value(0, 48000); }, "zero input rate rejected");
    rejects([] { am::AdaptiveResampler value(48000, 0); }, "zero output rate rejected");
    rejects([] { am::AdaptiveResampler value(48000, 48000, 0); }, "zero resampler capacity rejected");
    rejects([] { am::AdaptiveResampler value(1, 48000); }, "unsupported upsampling rejected");
    rejects([] { am::AdaptiveResampler value(48000, 1); }, "unsupported downsampling rejected");
    am::AdaptiveResampler adapter(44100, 48000, 1024);
    std::vector<Frame> input(1100, {.25f, -.5f}), output(4096);
    expect(adapter.push(input) == 1024 && adapter.droppedInputFrames() == 76, "resampler buffer overflow drops new samples exactly");
    expect(adapter.pendingInputFrames() == 1024, "full resampler occupancy exact");
    adapter.produce(output, 10);
    expectNear(adapter.ratio(), 48000. / 44100 * 1.002, 1e-12, "excess positive correction clamped");
    adapter.produce(output, -10);
    expectNear(adapter.ratio(), 48000. / 44100 * .998, 1e-12, "excess negative correction clamped");
    for (double bad : {std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity()}) {
        adapter.produce(output, bad);
        expectNear(adapter.ratio(), 48000. / 44100, 1e-12, "nonfinite ratio uses nominal rate");
    }
    adapter.reset();
    expect(adapter.pendingInputFrames() == 0 && adapter.lastError() == 0, "reset removes buffered input and errors");
    expectNear(adapter.ratio(), 48000. / 44100, 1e-12, "reset restores base ratio");
    expect(adapter.produce(output) == 0, "reset does not replay previous samples");
    input.resize(800);
    for (std::size_t i = 0; i < input.size(); ++i) input[i] = sequence(i % 7);
    am::AdaptiveResampler fresh(44100, 48000, 1024);
    adapter.push(input); fresh.push(input);
    std::vector<Frame> expected(4096);
    const auto a = adapter.produce(output), b = fresh.produce(expected);
    expect(a == b && std::equal(output.begin(), output.begin() + a, expected.begin(), equal), "reset filter history exactly equals fresh instance");
    adapter.reset();
    input.assign(800, {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity()});
    adapter.push(input); const auto silent = adapter.produce(output);
    expect(silent > 0 && std::all_of(output.begin(), output.begin() + silent, [](Frame f) { return f.left == 0 && f.right == 0; }), "invalid sample values become silence before filtering");
    std::cout << "PASS resampler limits, overflow telemetry, invalid samples/ratios, reset history\n";
}

void actualClockCapture() {
    constexpr double duration = 120, dt = .005;
    constexpr std::size_t target = 720, capacity = 8192;
    for (double ppm : {-750., 750.}) {
        am::FrameQueue queue(capacity);
        am::DriftController drift;
        am::AdaptiveResampler adapter(48000, 48000, 2048);
        std::vector<Frame> prime(target, {.1f, -.1f}); queue.push(prime);
        std::array<Frame, 256> input; input.fill({.1f, -.1f});
        std::array<Frame, 512> output;
        std::array<Frame, 240> consumed;
        double fractional{}, lastRatio = 1;
        std::size_t minimum = capacity, maximum{};
        for (int tick = 0; tick < static_cast<int>(duration / dt); ++tick) {
            fractional += 48000 * dt * (1 + ppm * 1e-6);
            const auto count = static_cast<std::size_t>(fractional); fractional -= count;
            lastRatio = drift.update(queue.size(), target, dt);
            if (adapter.push(std::span(input).first(count)) != count) throw std::runtime_error("actual SRC input dropped");
            for (;;) {
                const auto made = adapter.produce(output, lastRatio);
                if (!made) break;
                if (queue.push(std::span(output).first(made)) != made) throw std::runtime_error("actual SRC output overflow");
            }
            if (queue.pop(consumed) != consumed.size()) throw std::runtime_error("actual SRC queue underrun");
            minimum = std::min(minimum, queue.size()); maximum = std::max(maximum, queue.size());
            if (tick > 200 && !std::all_of(consumed.begin(), consumed.end(), [](Frame f) {
                    return std::isfinite(f.left) && std::isfinite(f.right) && std::abs(f.left - .1f) < .002 && std::abs(f.right + .1f) < .002;
                })) throw std::runtime_error("drift-corrected DC stream distorted or channels crossed");
        }
        expect(queue.droppedFrames() == 0 && queue.underrunFrames() == 0, "actual SRC independent-clock stream has no overflow/underflow");
        expectNear(queue.size(), target, 12, "actual SRC capture queue settles within 0.25ms of target");
        expectNear(lastRatio, 1 / (1 + ppm * 1e-6), 30e-6, "actual SRC capture correction cancels specified independent clock");
        expect(adapter.lastError() == 0 && adapter.droppedInputFrames() == 0, "actual adaptive stream retains all accepted input");
        std::cout << "PASS actual SRC capture " << ppm << " ppm x " << duration << " simulated seconds, queue " << minimum << ".." << maximum
            << ", final " << queue.size() << ", ratio " << lastRatio << ", no under/overruns\n";
    }
}

void actualClockRender() {
    constexpr double duration = 120, dt = .005;
    constexpr std::size_t target = 720, capacity = 8192;
    for (double ppm : {-750., 750.}) {
        am::FrameQueue queue(capacity);
        am::DriftController drift;
        am::AdaptiveResampler adapter(48000, 48000, 2048);
        std::vector<Frame> prime(target, {.1f, -.1f}); queue.push(prime);
        std::array<Frame, 240> mixed; mixed.fill({.1f, -.1f});
        std::array<Frame, 256> transfer, rendered;
        double fractional{}, lastRatio = 1, settledRatioSum{};
        std::size_t settledRatioCount{};
        std::size_t minimum = capacity, maximum{};
        for (int tick = 0; tick < static_cast<int>(duration / dt); ++tick) {
            lastRatio = drift.update(queue.size() + adapter.pendingInputFrames(), target, dt);
            if (queue.push(mixed) != mixed.size()) throw std::runtime_error("render input queue overflow");
            fractional += 48000 * dt * (1 + ppm * 1e-6);
            const auto wanted = static_cast<std::size_t>(fractional); fractional -= wanted;
            std::size_t filled{};
            while (filled < wanted) {
                filled += adapter.produce(std::span(rendered).subspan(filled, wanted - filled), lastRatio);
                if (filled == wanted) break;
                const auto count = queue.pop(std::span(transfer).first(std::min(queue.size(), transfer.size())));
                if (count == 0) throw std::runtime_error("actual render SRC queue exhausted");
                if (adapter.push(std::span(transfer).first(count)) != count) throw std::runtime_error("render SRC input dropped");
            }
            const auto occupancy = queue.size() + adapter.pendingInputFrames();
            minimum = std::min(minimum, occupancy); maximum = std::max(maximum, occupancy);
            if (tick >= static_cast<int>((duration - 20) / dt)) { settledRatioSum += lastRatio; ++settledRatioCount; }
            if (tick > 200 && !std::all_of(rendered.begin(), rendered.begin() + wanted, [](Frame f) {
                    return std::isfinite(f.left) && std::isfinite(f.right) && std::abs(f.left - .1f) < .002 && std::abs(f.right + .1f) < .002;
                })) throw std::runtime_error("render drift-corrected stream distorted or channels crossed");
        }
        expect(queue.droppedFrames() == 0 && queue.underrunFrames() == 0, "actual render SRC has no queue overflow/underflow");
        expectNear(queue.size() + adapter.pendingInputFrames(), target, 12, "actual render SRC queue settles within 0.25ms of target");
        // Queue occupancy has one transfer block of quantization as the SRC
        // retains filter input internally. Evaluate the sustained clock ratio,
        // rather than expecting one noisy feedback sample to equal its mean.
        expectNear(settledRatioSum / settledRatioCount, 1 + ppm * 1e-6, 30e-6, "actual render SRC mean correction cancels independent device clock");
        expect(adapter.lastError() == 0 && adapter.droppedInputFrames() == 0, "actual render SRC accepts all input");
        std::cout << "PASS actual SRC render " << ppm << " ppm x " << duration << " simulated seconds, queue " << minimum << ".." << maximum
            << ", final " << queue.size() + adapter.pendingInputFrames() << ", final-20s mean ratio " << settledRatioSum / settledRatioCount << ", no under/overruns\n";
    }
}
} // namespace

int main() {
    SetPriorityClass(GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS);
    DWORD_PTR processMask{}, systemMask{};
    if (GetProcessAffinityMask(GetCurrentProcess(), &processMask, &systemMask)) {
        // Cap tests to two available logical cores, preserving real contention.
        DWORD_PTR capped{};
        for (unsigned i = 0; i < sizeof(DWORD_PTR) * 8 && std::popcount(capped) < 2; ++i)
            if (processMask & (DWORD_PTR{1} << i)) capped |= DWORD_PTR{1} << i;
        if (capped) SetProcessAffinityMask(GetCurrentProcess(), capped);
    }
    const auto started = std::chrono::steady_clock::now();
    std::cout << std::setprecision(9);
    try {
        queueReference(); queueConcurrent(); controllerEdges(); clockModels(); resamplerQuality(); actualClockCapture(); actualClockRender();
        std::cout << "PASS " << checks << " assertions; elapsed wall seconds "
            << std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count()
            << "; simulated clock duration 12 hours analytical + 480 seconds real resampler; no hardware or settings accessed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL after " << checks << " assertions: " << error.what() << '\n';
        return 1;
    }
}
