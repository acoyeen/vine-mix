// Memory-only verification. No Windows endpoint APIs, audio streams, device
// defaults, microphone content, configuration files or UI automation are used.
#include "am/spectrum.hpp"
#include "am/graph.hpp"
#include <chrono>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace allocation {
thread_local bool watching{};
thread_local std::uint64_t count{};
}
void* operator new(std::size_t size) {
    if (allocation::watching) ++allocation::count;
    if (void* value = std::malloc(size ? size : 1)) return value;
    throw std::bad_alloc();
}
void* operator new[](std::size_t size) { return ::operator new(size); }
void operator delete(void* value) noexcept { std::free(value); }
void operator delete[](void* value) noexcept { std::free(value); }
void operator delete(void* value, std::size_t) noexcept { std::free(value); }
void operator delete[](void* value, std::size_t) noexcept { std::free(value); }

namespace {
using namespace am;
int checks{};
void require(bool condition, const std::string& description) {
    ++checks; if (!condition) throw std::runtime_error(description);
}
void near(double actual, double expected, double tolerance, const std::string& description) {
    require(std::isfinite(actual) && std::abs(actual - expected) <= tolerance,
        description + " (" + std::to_string(actual) + " versus " + std::to_string(expected) + ")");
}
float highest(const LiveSpectrum::View& value) { return *std::max_element(value.db.begin(), value.db.end()); }
std::size_t peakIndex(const LiveSpectrum::View& value) {
    return static_cast<std::size_t>(std::max_element(value.db.begin(), value.db.end()) - value.db.begin());
}
void sine(std::array<Frame, quantumFrames>& block, std::uint64_t frame, double hz, float amplitude, float right = 1) {
    for (std::size_t n = 0; n < block.size(); ++n) {
        const float value = amplitude * static_cast<float>(std::sin(2 * std::numbers::pi * hz * (frame + n) / sampleRate));
        block[n] = {value, value * right};
    }
}
std::uint64_t feed(LiveSpectrum& analyzer, std::uint64_t start, int blocks, double hz, float amplitude, float right = 1,
    bool poll = true) {
    std::array<Frame, quantumFrames> samples{};
    for (int b = 0; b < blocks; ++b) {
        sine(samples, start, hz, amplitude, right); start += quantumFrames;
        analyzer.push(samples, start);
        if (poll) analyzer.read();
    }
    return start;
}
void calibration() {
    auto analyzer = std::make_unique<LiveSpectrum>(17);
    const auto& initial = analyzer->read();
    require(initial.frame == 0 && initial.revision == 17, "Unprimed analyzer cannot advertise old audio");
    require(std::all_of(initial.db.begin(), initial.db.end(), [](float value) { return value == -90; }), "Initial spectrum is silence");
    near(LiveSpectrum::frequencyHz(0), 20, 1e-6, "Log frequency starts at20Hz");
    near(LiveSpectrum::frequencyHz(127), 20000, 1e-6, "Log frequency ends at20kHz");
    constexpr double aligned = 85. * sampleRate / spectrumFftFrames;
    auto frame = feed(*analyzer, 0, 100, aligned, .5f);
    const auto reference = analyzer->read();
    near(highest(reference), -6.0206, .15, "Hann coherent-gain calibration: half-scale stereo sine is-6.02dBFS");
    require(std::abs(std::log(LiveSpectrum::frequencyHz(peakIndex(reference)) / aligned)) < .06, "Measured tone peak falls in its logarithmic frequency bin");
    auto antiphase = std::make_unique<LiveSpectrum>(); feed(*antiphase, 0, 100, aligned, .5f, -1);
    const auto inverted = antiphase->read();
    for (std::size_t n = 0; n < spectrumBinCount; ++n) near(inverted.db[n], reference.db[n], .001, "Opposite-polarity stereo does not cancel");
    auto oneSide = std::make_unique<LiveSpectrum>(); feed(*oneSide, 0, 100, aligned, .5f, 0);
    near(highest(oneSide->read()), highest(reference) - 3.0103, .02, "One active stereo channel carries half the mean power");
    auto offset = std::make_unique<LiveSpectrum>(); feed(*offset, 0, 100, aligned + sampleRate / spectrumFftFrames / 2., .5f);
    require(highest(offset->read()) > -7.6 && highest(offset->read()) <= -6., "Half-bin Hann scalloping is bounded and not misrepresented as precision metering");
    for (double hz : {50., 100., 250., 2000., 6000., 14000.}) {
        auto tone = std::make_unique<LiveSpectrum>(); feed(*tone, 0, 70, hz, .1f);
        const double located = LiveSpectrum::frequencyHz(peakIndex(tone->read()));
        require(std::abs(located - hz) < std::max(12., hz * .07), "Spectrum resolves independent low/mid/high test tones");
    }
    std::array<Frame, quantumFrames> silent{};
    for (int block = 0; block < 40; ++block) { frame += quantumFrames; analyzer->push(silent, frame); analyzer->read(); }
    require(highest(analyzer->read()) < -25 && highest(analyzer->read()) > -90, "Spectrum has a smooth release after sound stops");
    for (int block = 0; block < 260; ++block) { frame += quantumFrames; analyzer->push(silent, frame); analyzer->read(); }
    require(highest(analyzer->read()) < -89.5, "Silent input settles to the floor without stale peaks");
    silent.fill({std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity()});
    for (int block = 0; block < 60; ++block) { frame += quantumFrames; analyzer->push(silent, frame); analyzer->read(); }
    require(std::all_of(analyzer->read().db.begin(), analyzer->read().db.end(), [](float value) { return std::isfinite(value) && value <= 0 && value >= -90; }), "Non-finite samples cannot poison telemetry");
}
void cadenceAndFreshness() {
    auto analyzer = std::make_unique<LiveSpectrum>(9);
    std::array<Frame, quantumFrames> samples{};
    std::uint64_t frame{}, previous{};
    std::vector<std::uint64_t> seen;
    for (int b = 0; b < 200; ++b) {
        sine(samples, frame, 1000, .1f); frame += quantumFrames; analyzer->push(samples, frame);
        const auto value = analyzer->read();
        if (value.frame != previous) { seen.push_back(value.frame); previous = value.frame; }
        const auto duplicate = analyzer->read();
        require(duplicate.frame == value.frame && duplicate.db == value.db, "Duplicate polling never advances spectrum or smoothing");
    }
    require(seen.size() == 19 && seen.front() == 4320, "4096-frame prime then measured20Hz cadence");
    for (std::size_t i = 1; i < seen.size(); ++i) require(seen[i] - seen[i - 1] == 2400, "Spectrum frame stamps follow completed windows exactly");
    frame = feed(*analyzer, frame, 2000, 8000, .1f, 1, false);
    const auto latest = analyzer->read();
    require(latest.frame > frame - spectrumHopFrames && latest.frame <= frame, "A ten-second stalled UI gets the latest window rather than a queued backlog");
    require(std::abs(LiveSpectrum::frequencyHz(peakIndex(latest)) - 8000) < 600, "After a stalled UI, old frequencies are discarded");
    analyzer->reset(27);
    require(analyzer->read().frame == 0 && analyzer->read().revision == 27 && highest(analyzer->read()) == -90,
        "Suspend/recovery reset immediately publishes empty telemetry with new continuity token");
    frame = feed(*analyzer, frame, 17, 2000, .1f);
    require(analyzer->read().frame == 0, "Reset requires a complete fresh4096-frame window");
    frame = feed(*analyzer, frame, 1, 2000, .1f);
    require(analyzer->read().frame == frame && analyzer->read().revision == 27, "Fresh telemetry reappears with current frame identity");
    analyzer->push(samples, frame + 2 * quantumFrames);
    require(analyzer->read().frame == 0, "A missing quantum clears the display instead of splicing old and new audio");
}
void measuredPostEq() {
    AudioGraph graph;
    GraphNode input; input.id = "source"; input.kind = "microphone";
    GraphNode eq; eq.id = "eq"; eq.kind = "eq"; eq.eqBands = {{"peak", true, 1000, 0, .7}};
    graph.nodes = {input, eq}; graph.edges = {{"send", "source", "eq"}};
    auto plan = std::make_unique<GraphPlan>(compileGraph(graph));
    auto processor = std::make_unique<GraphProcessor>(); processor->prepare(*plan);
    auto analyzer = std::make_unique<LiveSpectrum>();
    std::array<Frame, quantumFrames> samples{}; GraphProcessor::Inputs inputs{}; inputs[0] = samples;
    std::uint64_t frame{};
    auto run = [&] {
        for (int b = 0; b < 200; ++b) {
            sine(samples, frame, 1000, .04f, -1); frame += quantumFrames;
            processor->process(*plan, inputs); analyzer->push(processor->output(1), frame); analyzer->read();
        }
        return highest(analyzer->read());
    };
    const float flat = run();
    graph.nodes[1].eqBands[0].gainDb = 9;
    auto updated = std::make_unique<GraphPlan>(compileGraph(graph));
    auto changed = std::make_unique<GraphProcessor>(); changed->prepare(*updated); changed->inherit(*processor, *plan, *updated);
    processor = std::move(changed); plan = std::move(updated);
    const float boosted = run();
    near(boosted - flat, 9, .12, "Actual post-EQ audio spectrum follows a9dB bell boost");
    graph.nodes[1].bypass = true;
    updated = std::make_unique<GraphPlan>(compileGraph(graph)); changed = std::make_unique<GraphProcessor>();
    changed->prepare(*updated); changed->inherit(*processor, *plan, *updated);
    processor = std::move(changed); plan = std::move(updated);
    const float bypassed = run();
    near(bypassed, flat, .4, "Bypass spectrum follows actual unprocessed audio rather than the stored EQ curve");
    graph.nodes[1].bypass = false; graph.nodes[1].eqBands[0].gainDb = -12;
    updated = std::make_unique<GraphPlan>(compileGraph(graph)); changed = std::make_unique<GraphProcessor>();
    changed->prepare(*updated); changed->inherit(*processor, *plan, *updated);
    processor = std::move(changed); plan = std::move(updated);
    const float cut = run();
    near(cut - flat, -12, .4, "Actual post-EQ spectrum follows attenuation, including its audible smoothing");
}
void mailboxOwnership() {
    auto mailbox = std::make_unique<SpectrumMailbox>();
    std::atomic<bool> done{}, consistent{true}; std::atomic<int> received{};
    constexpr std::uint64_t count = 10000;
    std::thread consumer([&] {
        std::uint64_t previous{};
        while (!done.load(std::memory_order_acquire)) {
            const auto* window = mailbox->consume(); if (!window) { std::this_thread::yield(); continue; }
            if (window->frame <= previous || window->revision != window->frame + 7) consistent.store(false);
            previous = window->frame;
            for (const auto sample : window->samples)
                if (sample.left != static_cast<float>(window->frame) || sample.right != -sample.left) consistent.store(false);
            ++received;
        }
    });
    for (std::uint64_t sequence = 1; sequence <= count; ++sequence) {
        auto& slot = mailbox->writable(); slot.samples.fill({static_cast<float>(sequence), -static_cast<float>(sequence)});
        slot.frame = sequence; slot.revision = sequence + 7; mailbox->publish();
        if (sequence % 50 == 0) std::this_thread::yield();
    }
    done.store(true, std::memory_order_release); consumer.join();
    require(consistent && received > 0, "Concurrent producer/consumer never observes torn samples or metadata");
    // Consumer ownership has ended; the main thread may now be the consumer.
    auto& slot = mailbox->writable(); slot.frame = count + 1; mailbox->publish();
    require(mailbox->consume()->frame == count + 1, "Latest-window publication remains usable after heavy contention");
}
void realtimeBudget() {
    std::vector<std::unique_ptr<LiveSpectrum>> analyzers;
    for (std::size_t n = 0; n < maximumGraphNodes; ++n) analyzers.push_back(std::make_unique<LiveSpectrum>(n + 1));
    std::array<Frame, quantumFrames> samples{}; sine(samples, 0, 1000, .1f);
    constexpr int blocks = 2000;
    std::uint64_t frame{}; double worstMicros{};
    allocation::watching = true; allocation::count = 0;
    const auto begin = std::chrono::steady_clock::now();
    for (int b = 0; b < blocks; ++b) {
        const auto blockStart = std::chrono::steady_clock::now(); frame += quantumFrames;
        for (auto& analyzer : analyzers) analyzer->push(samples, frame);
        const double micros = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - blockStart).count();
        worstMicros = std::max(worstMicros, micros);
    }
    const double totalMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
    allocation::watching = false;
    require(allocation::count == 0, "32 concurrent EQ analyzer push paths allocate no heap memory");
    require(sizeof(LiveSpectrum) < 400 * 1024, "Analyzer memory has a fixed upper bound below400KiB perEQ");
    require(totalMs < 10000, "Synthetic worst-graph tap processing stays below its10-second audio duration");
    const auto analysisStart = std::chrono::steady_clock::now();
    for (auto& analyzer : analyzers) require(analyzer->read().frame > 0, "All32 analyzers expose a current completed window");
    const double analysisMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - analysisStart).count();
    std::cout << "Spectrum budget: " << sizeof(LiveSpectrum) << " bytes/EQ;32-node tap average " << totalMs * 1000 / blocks
        << " us/block, max observed " << worstMicros << " us/block;32 FFT snapshots " << analysisMs << " ms.\n";
}
}
int main() {
    try {
        calibration(); cadenceAndFreshness(); measuredPostEq(); mailboxOwnership(); realtimeBudget();
        std::cout << "PASS: " << checks << " silent spectrum checks.\n"; return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL after " << checks << " checks: " << error.what() << '\n'; return 1; }
}
