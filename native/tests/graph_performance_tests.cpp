// Memory-only benchmark of compiled graph processing. No endpoint APIs, device
// workers, settings store or audio output are linked into this executable.
#include "am/graph.hpp"
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <iostream>
#include <memory>
#include <numbers>
#include <stdexcept>
#include <vector>

namespace {
using namespace am;
void measure(const char* label, const char* kind, int bands) {
    AudioGraph graph;
    GraphNode source; source.id = "source"; source.kind = "virtualInput";
    graph.nodes.push_back(source);
    std::string previous = source.id;
    for (int i = 0; i < 30; ++i) {
        GraphNode effect; effect.id = "effect-" + std::to_string(i); effect.kind = kind;
        effect.panPosition = .35; effect.crossfadePosition = .5;
        effect.eqBands.clear();
        if (effect.kind == "highPass") effect.eqBands.push_back({"highPass", true, 80, 0, .70710678});
        else for (int band = 0; band < bands; ++band)
            effect.eqBands.push_back({"peak", true, 100. * (band + 1), bands == 1 ? 0. : -.2, 1});
        graph.edges.push_back({"edge-" + std::to_string(i), previous, effect.id});
        if (effect.kind == "crossfade") graph.edges.back().toPort = "a";
        previous = effect.id; graph.nodes.push_back(std::move(effect));
    }
    GraphNode sink; sink.id = "sink"; sink.kind = "output";
    graph.nodes.push_back(sink); graph.edges.push_back({"last", previous, sink.id});
    auto plan = std::make_unique<GraphPlan>(compileGraph(graph));
    auto processor = std::make_unique<GraphProcessor>(); processor->prepare(*plan);
    std::array<Frame, quantumFrames> input{};
    GraphProcessor::Inputs views{}; views[0] = input;
    std::vector<double> times; times.reserve(3000);
    double checksum{};
    for (int block = 0; block < 3100; ++block) {
        for (std::size_t i = 0; i < input.size(); ++i) {
            const auto value = static_cast<float>(.05 * std::sin(2 * std::numbers::pi * 997 * (block * quantumFrames + i) / sampleRate));
            input[i] = {value, -value * .5f};
        }
        const auto start = std::chrono::steady_clock::now();
        processor->process(*plan, views);
        const double elapsed = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - start).count();
        if (block >= 100) times.push_back(elapsed);
        const auto value = processor->output(graph.nodes.size() - 1).back();
        if (!std::isfinite(value.left) || !std::isfinite(value.right)) throw std::runtime_error("Non-finite graph output");
        checksum += value.left;
    }
    std::sort(times.begin(), times.end());
    std::cout << label << ": median=" << times[times.size()/2] << " us; p99=" << times[times.size()*99/100]
        << " us; checksum=" << checksum << "; 5000 us quantum.\n";
    // A benchmark is machine/load dependent; numerical regression tests are
    // separate. Report timings rather than rejecting a busy user's computer.
}
}
int main() {
    try {
        measure("30 empty EQ nodes", "eq", 0);
        measure("30 flat-band EQ nodes", "eq", 1);
        measure("30 high-pass nodes", "highPass", 1);
        measure("30 fully occupied EQ nodes", "eq", 8);
        measure("30 Pan nodes", "stereoPan", 0);
        measure("30 Stereo Width nodes", "stereoWidth", 0);
        measure("30 equal-power Crossfade nodes", "crossfade", 0);
        std::cout << "PASS: offline graph processing benchmark; no acoustic or device latency claim.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
