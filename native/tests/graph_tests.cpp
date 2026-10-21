#include "am/configuration.hpp"
#include "am/dsp.hpp"
#include "am/graph.hpp"
#include <windows.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <memory>
#include <numbers>
#include <sstream>
#include <stdexcept>

namespace {
using namespace am;
int assertions{};
void expect(bool condition, const std::string& message) {
    ++assertions; if (!condition) throw std::runtime_error(message);
}
void expectNear(double actual, double expected, double tolerance, const std::string& message) {
    expect(std::isfinite(actual) && std::abs(actual - expected) <= tolerance,
        message + " (actual=" + std::to_string(actual) + ", expected=" + std::to_string(expected) + ")");
}
template<class Action> void rejects(Action action, const char* message) {
    bool rejected = false; try { action(); } catch (const std::exception&) { rejected = true; }
    expect(rejected, message);
}
GraphNode node(const char* id, const char* kind) {
    GraphNode result; result.id = id; result.kind = kind; result.name = id;
    if (result.kind == "tone") result.eqBands = toneDefaults();
    if (result.kind == "highPass" || result.kind == "lowPass")
        result.eqBands = {{result.kind, true, result.kind == "highPass" ? 80. : 12000., 0, .70710678}};
    return result;
}
AudioGraph chain(const char* kind = "gain") {
    AudioGraph graph; graph.nodes = {node("source", "microphone"), node("effect", kind), node("out", "output")};
    graph.edges = {{"in", "source", "effect"}, {"out", "effect", "out"}}; return graph;
}
std::size_t index(const GraphPlan& plan, const char* id) {
    for (std::size_t i = 0; i < plan.nodeCount; ++i) if (plan.nodes[i].id == id) return i;
    throw std::runtime_error("Missing test node");
}
Frame constant(GraphProcessor& processor, const GraphPlan& plan, Frame value, int blocks = 6) {
    std::array<Frame, quantumFrames> input; input.fill(value);
    GraphProcessor::Inputs views{}; views[index(plan, "source")] = input;
    for (int i = 0; i < blocks; ++i) processor.process(plan, views);
    return processor.output(index(plan, "out")).back();
}
double tone(const AudioGraph& graph, double frequency, double amplitude = .05) {
    const auto plan = compileGraph(graph); auto processor = std::make_unique<GraphProcessor>();
    std::array<Frame, quantumFrames> input; GraphProcessor::Inputs views{}; views[index(plan, "source")] = input;
    double energy{}; std::size_t count{};
    for (std::size_t block = 0; block < 120; ++block) {
        for (std::size_t frame = 0; frame < quantumFrames; ++frame) {
            const auto value = static_cast<float>(amplitude * std::sin(2 * std::numbers::pi * frequency * (block * quantumFrames + frame) / sampleRate));
            input[frame] = {value, value};
        }
        processor->process(plan, views);
        if (block >= 40) for (const auto& value : processor->output(index(plan, "out"))) { energy += value.left * value.left; ++count; }
    }
    return std::sqrt(energy / count);
}
// Keep large fixed-plan test fixtures in separate stack frames. Aggressive
// inlining into main otherwise retains every fixture under Windows' 1 MiB
// default stack; production DSP plans/processors are owned by engine runtimes.
__declspec(noinline) void migration() {
    std::array<std::array<Frame, quantumFrames>, 3> source;
    source[0].fill({.03f, -.04f}); source[1].fill({.07f, -.02f}); source[2].fill({.11f, .13f});
    for (unsigned mask = 0; mask < 64; ++mask) {
        Configuration legacy;
        for (std::size_t s = 0; s < 3; ++s) {
            legacy.sources[s] = {false, {(mask & (1u << (2 * s))) != 0, -static_cast<float>(s + 1) * 3},
                {(mask & (1u << (2 * s + 1))) != 0, -static_cast<float>(s + 1) * 6}};
        }
        legacy.busA.gainDb = -4; legacy.busB.gainDb = -2;
        auto document = Json(legacy); document["version"] = 1; document.erase("graph");
        auto migrated = document.get<Configuration>(); validateConfiguration(migrated);
        expect(migrated.version == 2 && migrated.graph.nodes.size() == 8 && migrated.graph.edges.size() == 9, "Legacy schema migration");
        auto plan = compileGraph(migrated.graph); auto graph = std::make_unique<GraphProcessor>(); Mixer reference;
        std::array<Frame, quantumFrames> a{}, b{};
        std::array<std::span<const Frame>, 3> inputs{source[0], source[1], source[2]};
        GraphProcessor::Inputs views{}; views[0] = source[0]; views[1] = source[1]; views[2] = source[2];
        for (int block = 0; block < 6; ++block) { reference.process(inputs, a, b, legacy); graph->process(plan, views); }
        const auto first = graph->output(index(plan, "main-output-1"));
        const auto second = graph->output(index(plan, "main-output-2"));
        const auto virtualOut = graph->output(index(plan, "virtual-output"));
        expectNear(first.back().left, a.back().left, 1e-6, "A route migration " + std::to_string(mask));
        expectNear(first.back().right, a.back().right, 1e-6, "A stereo migration");
        expectNear(second.back().left, first.back().left, 0, "Independent output fanout");
        expectNear(virtualOut.back().left, b.back().left, 1e-6, "B route migration " + std::to_string(mask));
        expectNear(virtualOut.back().right, b.back().right, 1e-6, "B stereo migration");
    }
    Configuration config; migrateLegacyGraph(config); config.busA = {true, -24}; config.revision = 3;
    config.microphones[0] = {"saved-mic", "stable-mic", "Saved microphone", 1};
    config.mainOutputs[0] = {"missing-headphones", "stable-headphones", "Missing headphones", 0};
    config.sources[0].b.enabled = true;
    auto json = Json(config); json["version"] = 1; json.erase("graph");
    const auto upgraded = json.get<Configuration>();
    expect(upgraded.revision == 3 && upgraded.graph.nodes[3].muted && upgraded.graph.nodes[3].gainDb == -24, "Saved quiet A state is exact");
    expect(upgraded.graph.nodes[0].device.channel == 1 && upgraded.graph.nodes[5].device.id == "missing-headphones", "Missing device identity and microphone channel preserved");
    expect(!upgraded.graph.edges[1].muted && upgraded.graph.edges[0].muted, "Explicit B microphone route preserved");
    auto arranged = upgraded;
    arranged.graph.panX = -137; arranged.graph.panY = 253; arranged.graph.zoom = .75;
    for (std::size_t i = 0; i < arranged.graph.nodes.size(); ++i) {
        arranged.graph.nodes[i].x = 301 - static_cast<double>(i) * 97;
        arranged.graph.nodes[i].y = 19 + static_cast<double>(i) * 43;
    }
    const auto savedLayout = Json(arranged);
    expect(Json(savedLayout.get<Configuration>().graph) == savedLayout["graph"],
        "Existing graph positions, pan and zoom survive loading without automatic rearrangement");
}
__declspec(noinline) void controls() {
    auto graph = chain(); graph.nodes.push_back(node("out2", "output"));
    graph.edges.push_back({"second", "source", "out2", "audio", -6, false});
    graph.nodes.push_back(node("control", "control")); graph.nodes.back().gainDb = -6;
    graph.edges.push_back({"shared", "control", "effect", "control", 0, false});
    auto plan = compileGraph(graph); auto processor = std::make_unique<GraphProcessor>();
    const auto a = constant(*processor, plan, {.4f, .2f});
    expectNear(a.left, .4 * std::pow(10., -6 / 20.), 1e-6, "Control adds gain to target");
    expectNear(processor->output(index(plan, "out2")).back().left, a.left, 1e-6, "Independent cable gain");
    auto changed = graph; changed.nodes[1].gainDb = -12; auto after = compileGraph(changed);
    auto next = std::make_unique<GraphProcessor>(); next->inherit(*processor, plan, after);
    const auto reduced = constant(*next, after, {.4f, .2f});
    expectNear(reduced.left, .4 * std::pow(10., -18 / 20.), 1e-6, "Control and target gains combine");
    expectNear(next->output(index(after, "out2")).back().left, a.left, 1e-6, "One route level never changes its sibling");
    changed.nodes.back().muted = true; after = compileGraph(changed);
    expectNear(constant(*next, after, {.4f, .2f}).left, 0, 1e-6, "Shared control mute");
    expect(next->output(index(after, "out2")).back().left > .19f, "Uncontrolled sibling stays audible");
    changed.edges.back().muted = true; after = compileGraph(changed);
    expectNear(constant(*next, after, {.4f, .2f}).left, .4 * std::pow(10., -12. / 20.), 1e-6,
        "Disabled control cable ignores its source mute and gain, restoring local control");
    changed.nodes[0].muted = true; after = compileGraph(changed);
    constant(*next, after, {.4f, .2f});
    expectNear(next->output(index(after, "out2")).back().left, 0, 1e-6, "Source mute silences all sends");
    GraphProcessor::Inputs absent{}; next->process(after, absent);
    expectNear(next->output(index(after, "out2")).back().left, 0, 0, "Absent input is silence");
}
__declspec(noinline) void physicalAndVirtualOutputMuteIsolation() {
    // A microphone can feed listening hardware and a virtual microphone at
    // once. Muting either sink must silence its samples without changing the
    // sibling send; only a source mute silences both branches.
    auto graph = chain("mix");
    graph.nodes.push_back(node("virtual", "virtualOutput"));
    graph.edges.push_back({"virtual-in", "effect", "virtual"});
    graph.nodes[2].gainDb = -6;
    graph.nodes[3].gainDb = -12;
    auto plan = compileGraph(graph);
    auto dsp = std::make_unique<GraphProcessor>(); dsp->prepare(plan);
    std::array<Frame, quantumFrames> input; input.fill({.4f, -.2f});
    GraphProcessor::Inputs views{}; views[index(plan, "source")] = input;
    const float physicalGain = std::pow(10.f, -6.f / 20.f);
    const float virtualGain = std::pow(10.f, -12.f / 20.f);
    const auto verify = [&](bool sourceMuted, bool physicalMuted, bool virtualMuted) {
        for (const auto sink : {std::size_t{2}, std::size_t{3}}) {
            const bool silent = sourceMuted || (sink == 2 ? physicalMuted : virtualMuted);
            const float gain = silent ? 0.f : sink == 2 ? physicalGain : virtualGain;
            for (const auto sample : dsp->output(sink)) {
                expectNear(sample.left, .4f * gain, silent ? 0 : 1e-7,
                    "Settled output mute preserves exact silence or independent left send level");
                expectNear(sample.right, -.2f * gain, silent ? 0 : 1e-7,
                    "Settled output mute preserves exact silence or independent right send level");
            }
        }
        expectNear(dsp->meter(0).peakLeft, sourceMuted ? 0 : .4f, 0,
            "Output mute leaves the microphone live; source mute silences its meter");
    };
    for (int block = 0; block < 6; ++block) dsp->process(plan, views);
    verify(false, false, false);
    const auto adopt = [&](bool sourceMuted, bool physicalMuted, bool virtualMuted) {
        graph.nodes[0].muted = sourceMuted;
        graph.nodes[2].muted = physicalMuted;
        graph.nodes[3].muted = virtualMuted;
        auto after = compileGraph(graph);
        auto next = std::make_unique<GraphProcessor>(); next->prepare(after);
        next->inherit(*dsp, plan, after);
        plan = std::move(after); dsp = std::move(next);
    };
    adopt(false, true, false);
    dsp->process(plan, views);
    expect(dsp->output(2).front().left > dsp->output(2).back().left
        && dsp->output(2).back().left > 0, "Live physical mute inherits its existing 10 ms fade");
    for (const auto sample : dsp->output(3)) {
        expectNear(sample.left, .4f * virtualGain, 1e-7, "Physical mute adoption leaves virtual left send unchanged");
        expectNear(sample.right, -.2f * virtualGain, 1e-7, "Physical mute adoption leaves virtual right send unchanged");
    }
    dsp->process(plan, views);
    expectNear(dsp->output(2).back().left, 0, 0, "Physical mute reaches exact silence after its 10 ms fade");
    dsp->process(plan, views); verify(false, true, false);
    for (const auto state : {std::array{false, false, true}, std::array{false, true, true},
                            std::array{false, false, false}, std::array{true, false, false},
                            std::array{false, true, false}}) {
        adopt(state[0], state[1], state[2]);
        for (int block = 0; block < 3; ++block) dsp->process(plan, views);
        verify(state[0], state[1], state[2]);
    }
}
__declspec(noinline) void transparentBypass() {
    const auto gain = [](double db) { return std::pow(10., db / 20.); };
    for (const char* kind : {"mix", "gain", "eq", "tone", "highPass", "lowPass", "compressor", "limiter", "ducking", "pan", "mono", "reroute"}) {
        auto graph = chain(kind);
        graph.nodes[0].gainDb = -3; graph.nodes[0].bypass = true;
        graph.nodes[1].gainDb = -9; graph.nodes[1].balance = .65;
        graph.nodes[1].compressor = {-30, 12, .1, 5, 6};
        if (graph.nodes[1].kind == "eq" || graph.nodes[1].kind == "tone") graph.nodes[1].eqBands[0].gainDb = 9;
        graph.nodes[2].gainDb = -4; graph.nodes[2].bypass = true;
        graph.nodes.push_back(node("control", "control")); graph.nodes.back().gainDb = -12;
        graph.edges[0].gainDb = -6; graph.edges[1].gainDb = -2;
        graph.edges.push_back({"control", "control", "effect", "control", -3, false});
        const auto before = compileGraph(graph); auto old = std::make_unique<GraphProcessor>();
        constant(*old, before, {.4f, -.15f}, 200);
        graph.nodes[1].bypass = true;
        const auto saved = Json(graph); const auto plan = compileGraph(graph);
        expect(sameGraphTopology(before, plan), "Bypass changes no topology or worker assignments");
        auto dsp = std::make_unique<GraphProcessor>(); dsp->inherit(*old, before, plan);
        const auto value = constant(*dsp, plan, {.4f, -.15f}, 2);
        expectNear(value.left, .4 * gain(-15), 1e-7, std::string(kind) + " bypass skips effect/local/linked gain but retains source/sink/cable gains after 10 ms");
        expectNear(value.right, -.15 * gain(-15), 1e-7, std::string(kind) + " bypass preserves independent stereo polarity");
        expect(Json(graph) == saved, "Compiling/processing bypass does not migrate or rewrite saved settings");

        // A saved intermediate mute is deliberately not reinterpreted as dry
        // audio. The UI's explicit first click can clear it and select bypass.
        for (std::size_t mutedNode : {std::size_t{0}, std::size_t{1}, std::size_t{2}, std::size_t{3}}) {
            graph.nodes[mutedNode].muted = true;
            auto mutedPlan = compileGraph(graph);
            const auto silent = constant(*dsp, mutedPlan, {.4f, -.15f});
            expectNear(silent.left, 0, 0, "Bypass preserves own, source, output and linked-control legacy mute safety");
            expectNear(silent.right, 0, 0, "Legacy mute safety includes both stereo channels");
            graph.nodes[mutedNode].muted = false;
        }
        graph.edges[0].muted = true;
        expectNear(constant(*dsp, compileGraph(graph), {.4f, -.15f}).left, 0, 0,
            "Node bypass never re-enables a muted incoming audio cable");
    }

    auto linked = chain("gain"); linked.nodes[1].gainDb = -9;
    linked.nodes.push_back(node("control", "control")); linked.nodes.back().gainDb = -12;
    linked.edges.push_back({"shared", "control", "effect", "control", -3, false});
    auto dsp = std::make_unique<GraphProcessor>();
    expectNear(constant(*dsp, compileGraph(linked), {.4f, .2f}).left, .4 * gain(-24), 1e-7,
        "Enabled control contributes its level and control cable weight");
    linked.nodes.back().bypass = true;
    expectNear(constant(*dsp, compileGraph(linked), {.4f, .2f}, 2).left, .4 * gain(-9), 1e-7,
        "Bypassed control ignores both its level and control cable weight while retaining target level");
    linked.nodes.back().muted = true;
    expectNear(constant(*dsp, compileGraph(linked), {.4f, .2f}).left, 0, 0,
        "Bypassed controller retains its saved legacy mute until explicit action");
    linked.edges.back().muted = true;
    expectNear(constant(*dsp, compileGraph(linked), {.4f, .2f}).left, .4 * gain(-9), 1e-7,
        "Disabled control cable still removes all linked control including legacy mute");
    linked.edges.back().muted = false; linked.nodes.back().muted = false;
    linked.nodes[1].bypass = true;
    expectNear(constant(*dsp, compileGraph(linked), {.4f, .2f}).left, .4, 1e-7,
        "Explicitly cleared legacy controller mute restores the bypassed dry target");

    auto mix = chain("mix"); mix.nodes[1].bypass = true; mix.nodes[1].gainDb = -24;
    mix.nodes.push_back(node("source2", "virtualInput"));
    mix.edges[0].gainDb = -6; mix.edges.push_back({"second", "source2", "effect", "audio", -12, false});
    const auto mixPlan = compileGraph(mix); dsp->reset();
    std::array<Frame, quantumFrames> first, second; first.fill({.4f, -.2f}); second.fill({-.1f, .3f});
    GraphProcessor::Inputs inputs{}; inputs[0] = first; inputs[3] = second;
    for (int block = 0; block < 6; ++block) dsp->process(mixPlan, inputs);
    expectNear(dsp->output(2).back().left, .4 * gain(-6) - .1 * gain(-12), 1e-7,
        "Bypassed Add/Mix preserves every incoming weighted source rather than selecting one");
    expectNear(dsp->output(2).back().right, -.2 * gain(-6) + .3 * gain(-12), 1e-7,
        "Bypassed Add/Mix retains independent right-channel sum");
}
__declspec(noinline) void bypassLevelTransitions() {
    for (const char* kind : {"gain", "mix", "reroute"}) {
        auto graph = chain(kind); graph.nodes[1].gainDb = -12;
        graph.nodes.push_back(node("control", "control")); graph.nodes.back().gainDb = -6;
        graph.edges.push_back({"linked", "control", "effect", "control", -6, false});
        auto plan = compileGraph(graph); auto dsp = std::make_unique<GraphProcessor>();
        constant(*dsp, plan, {.4f, -.2f});
        std::array<Frame, quantumFrames> samples; samples.fill({.4f, -.2f});
        GraphProcessor::Inputs inputs{}; inputs[0] = samples;
        for (bool bypass : {true, false}) {
            double previous = dsp->output(2).back().left;
            const double target = bypass ? .4 : .4 * std::pow(10., -24. / 20.);
            // Float ramps snap the accumulated rounding residual to their
            // exact target on frame 480, as in the existing transition suite.
            const double maximumStep = std::abs(target - previous) / 480 + 2e-6;
            graph.nodes[1].bypass = bypass; const auto after = compileGraph(graph);
            auto next = std::make_unique<GraphProcessor>(); next->inherit(*dsp, plan, after);
            for (int block = 0; block < 2; ++block) {
                next->process(after, inputs);
                for (const auto sample : next->output(2)) {
                    expect(std::abs(sample.left - previous) <= maximumStep
                        && (bypass ? sample.left >= previous - 1e-7 : sample.left <= previous + 1e-7),
                        std::string(kind) + " bypass=" + std::to_string(bypass) + " has bounded monotonic step: previous="
                            + std::to_string(previous) + ", current=" + std::to_string(sample.left) + ", maximumStep=" + std::to_string(maximumStep));
                    expectNear(sample.right, -sample.left * .5, 1e-7, "Bypass transition keeps stereo levels linked");
                    previous = sample.left;
                }
            }
            expectNear(previous, target, 1e-7, "Intermediate bypass/re-enable reaches exact target after 10 ms");
            expect(graph.nodes[1].gainDb == -12 && graph.nodes.back().gainDb == -6 && graph.edges.back().gainDb == -6,
                "Bypass preserves stored levels for subsequent re-enable");
            plan = after; dsp = std::move(next);
        }
    }
}
__declspec(noinline) void effects() {
    auto mono = chain("mono"); auto plan = compileGraph(mono); auto processor = std::make_unique<GraphProcessor>();
    auto value = constant(*processor, plan, {.6f, -.2f}); expectNear(value.left, .2, 1e-6, "Mono average"); expectNear(value.right, value.left, 0, "Mono duplicate");
    auto pan = chain("pan"); pan.nodes[1].balance = -1; plan = compileGraph(pan); processor->reset();
    value = constant(*processor, plan, {.6f, .4f}); expectNear(value.left, .6, 1e-6, "Left pan preserves left"); expectNear(value.right, 0, 0, "Left pan attenuates right");
    pan.nodes[1].balance = 0; plan = compileGraph(pan);
    value = constant(*processor, plan, {.6f, .4f}); expectNear(value.left, .6, 1e-6, "Centered balance left unity"); expectNear(value.right, .4, 1e-6, "Centered balance right unity");
    pan.nodes[1].balance = 1; pan.nodes[1].bypass = true; plan = compileGraph(pan);
    value = constant(*processor, plan, {.6f, .4f}); expectNear(value.left, .6, 1e-6, "Effect bypass retains dry signal");
    pan.nodes[1].muted = true; plan = compileGraph(pan);
    expectNear(constant(*processor, plan, {.6f, .4f}).left, 0, 1e-6, "Bypass never bypasses mute");
    auto comp = chain("compressor"); comp.nodes[1].compressor = {-20, 4, .1, 30, 0}; plan = compileGraph(comp); processor->reset();
    value = constant(*processor, plan, {.5f, .25f}, 240);
    const double expected = .5 * std::pow(5., -0.75);
    expectNear(value.left, expected, 1e-5, "4:1 compressor steady gain reduction"); expectNear(value.right, expected * .5, 1e-5, "Stereo-linked compression");
    comp.nodes[1].bypass = true; plan = compileGraph(comp);
    expectNear(constant(*processor, plan, {.5f, .25f}).left, .5, 1e-6, "Compressor bypass");
    auto flat = chain("eq"); const auto reference = tone(flat, 1000);
    expectNear(reference, .05 / std::sqrt(2.), 1e-5, "Flat five-band EQ is transparent");
    flat.nodes[1].eqBands[2].gainDb = 6;
    expectNear(tone(flat, 1000) / reference, std::pow(10., 6. / 20.), .015, "Parametric EQ +6 dB at center");
    flat.nodes[1].eqBands[2].gainDb = -12;
    expectNear(tone(flat, 1000) / reference, std::pow(10., -12. / 20.), .015, "Parametric EQ -12 dB at center");
    auto low = chain("eq"); low.nodes[1].eqBands = {{"lowPass", true, 1000, 0, .70710678}};
    expect(tone(low, 8000) / tone(chain(), 8000) < .02, "Low-pass attenuates 8 kHz");
    auto high = chain("eq"); high.nodes[1].eqBands = {{"highPass", true, 2000, 0, .70710678}};
    expect(tone(high, 100) / tone(chain(), 100) < .01, "High-pass attenuates 100 Hz");
    auto shelf = chain("eq"); shelf.nodes[1].eqBands = {{"lowShelf", true, 1000, 6, .70710678}};
    expectNear(tone(shelf, 50) / tone(chain(), 50), std::pow(10., .3), .02, "Low shelf raises bass");
    shelf.nodes[1].eqBands = {{"highShelf", true, 2000, -6, .70710678}};
    expectNear(tone(shelf, 16000) / tone(chain(), 16000), std::pow(10., -.3), .02, "High shelf reduces treble");
    shelf.nodes[1].eqBands[0].enabled = false;
    expectNear(tone(shelf, 16000) / tone(chain(), 16000), 1, .001, "Disabled band is transparent");
}
__declspec(noinline) void validation() {
    auto good = chain();
    auto bad = good; bad.edges.push_back({"cycle", "effect", "source"}); rejects([&] { compileGraph(bad); }, "Source input forbidden");
    bad = good; bad.nodes.push_back(node("other", "gain")); bad.edges.push_back({"forward", "effect", "other"}); bad.edges.push_back({"feedback", "other", "effect"});
    rejects([&] { compileGraph(bad); }, "Audio cycles rejected");
    bad.edges.back().muted = true; rejects([&] { compileGraph(bad); }, "Muted cycles still rejected");
    bad = good; bad.nodes[1].id = "source"; rejects([&] { compileGraph(bad); }, "Duplicate node IDs rejected");
    bad = good; bad.edges.push_back(bad.edges[0]); rejects([&] { compileGraph(bad); }, "Duplicate cables rejected");
    bad = good; bad.edges[0].to = "missing"; rejects([&] { compileGraph(bad); }, "Missing node references rejected");
    bad = good; bad.nodes.push_back(node("ctrl", "control")); bad.nodes.push_back(node("ctrl2", "control"));
    bad.edges.push_back({"c1", "ctrl", "effect", "control"}); bad.edges.push_back({"c2", "ctrl2", "effect", "control"});
    rejects([&] { compileGraph(bad); }, "Competing control sources rejected");
    bad = good; bad.nodes[1].gainDb = std::numeric_limits<float>::quiet_NaN(); rejects([&] { compileGraph(bad); }, "Non-finite controls rejected");
    bad = good; bad.nodes[1].eqBands[0].q = 0; rejects([&] { compileGraph(bad); }, "Unstable EQ Q rejected");
    bad = good; bad.nodes[1].compressor.attackMs = 0; rejects([&] { compileGraph(bad); }, "Zero compressor attack rejected");
    bad = good; bad.zoom = 0; rejects([&] { compileGraph(bad); }, "Invalid canvas scale rejected");
    bad = good; for (int i = 0; i < 32; ++i) bad.nodes.push_back(node(("n" + std::to_string(i)).c_str(), "gain"));
    rejects([&] { compileGraph(bad); }, "Node bound enforced");
    Configuration configuration = defaultConfiguration(); configuration.graph = good;
    configuration.graph.nodes[0].device.id = "same"; configuration.graph.nodes.push_back(node("duplicate", "microphone")); configuration.graph.nodes.back().device.id = "same";
    validateConfiguration(configuration);
    expect(configuration.graph.nodes.size() == 4, "Duplicate microphone sources are valid independent graph nodes");
    configuration.graph.nodes[0].kind = configuration.graph.nodes.back().kind = "virtualInput";
    validateConfiguration(configuration);
    expect(configuration.graph.nodes.size() == 4, "Duplicate virtual inputs are valid independent graph nodes");
    configuration.graph.nodes[0].kind = configuration.graph.nodes.back().kind = "appInput";
    configuration.graph.nodes[0].app = {100, "C:\\Synthetic\\player.exe", "Player"};
    configuration.graph.nodes.back().app = configuration.graph.nodes[0].app;
    validateConfiguration(configuration);
    expect(configuration.graph.nodes.size() == 4, "Duplicate application sources with saved identity are valid");
    configuration.graph.nodes.back().app.processId = 101;
    validateConfiguration(configuration);
    expect(configuration.graph.nodes.back().app.processId == 101, "Different instances of the same application remain selectable");
    configuration.graph = good; configuration.graph.nodes[2].device.id = "same-output";
    configuration.graph.nodes.push_back(node("duplicate-output", "output")); configuration.graph.nodes.back().device.id = "same-output";
    rejects([&] { validateConfiguration(configuration); }, "Duplicate listening outputs remain rejected");
    configuration.graph.nodes[2].kind = configuration.graph.nodes.back().kind = "virtualOutput";
    rejects([&] { validateConfiguration(configuration); }, "Duplicate exclusive virtual outputs remain rejected");
    configuration.graph.nodes.back().kind = "output";
    rejects([&] { validateConfiguration(configuration); }, "Output uniqueness applies across physical and virtual sink kinds");
    configuration.graph.nodes[2].device.id = "old-output"; configuration.graph.nodes.back().device.id = "new-output";
    configuration.graph.nodes[2].device.stableId = configuration.graph.nodes.back().device.stableId = "stable-output";
    rejects([&] { validateConfiguration(configuration); }, "Output uniqueness preserves matching stable identities after a reinstall");
    configuration.graph = good; configuration.graph.nodes[0].kind = "appInput"; configuration.graph.nodes[0].app.processId = 100;
    rejects([&] { validateConfiguration(configuration); }, "Bare reusable PID rejected");
}
__declspec(noinline) void extendedEqualizerAndTone() {
    auto empty = chain("eq"); empty.nodes[1].eqBands.clear();
    for (double frequency : {20., 1000., 18000.})
        expectNear(tone(empty, frequency) / tone(chain(), frequency), 1, 1e-5, "Zero-band EQ is a flat pass-through");

    auto eight = empty;
    for (std::size_t band = 0; band < maximumEqBands; ++band) eight.nodes[1].eqBands.push_back({"peak", true, 1000, 1, 1});
    const double flatReference = tone(empty, 1000);
    expectNear(tone(eight, 1000) / flatReference, std::pow(10., 8. / 20.), .004,
        "All eight EQ bands contribute: eight +1 dB filters total +8 dB at their shared center");
    auto seven = eight; seven.nodes[1].eqBands.pop_back();
    expectNear(tone(seven, 1000) / flatReference, std::pow(10., 7. / 20.), .004,
        "Removing the eighth EQ band removes exactly its center-frequency contribution");
    auto invalid = eight; invalid.nodes[1].eqBands.push_back({});
    rejects([&] { compileGraph(invalid); }, "Ninth EQ band rejected");
    invalid = eight; invalid.nodes[1].eqBands[7].q = std::numeric_limits<double>::quiet_NaN();
    rejects([&] { compileGraph(invalid); }, "Eighth-band nonfinite Q rejected");
    invalid = eight; invalid.nodes[1].eqBands[7].gainDb = 24.01;
    rejects([&] { compileGraph(invalid); }, "Eighth-band gain beyond +24 dB rejected");
    invalid = eight; invalid.nodes[1].eqBands[7].frequencyHz = 20001;
    rejects([&] { compileGraph(invalid); }, "Eighth-band ultrasonic frequency rejected");

    auto controls = chain("tone");
    expect(controls.nodes[1].eqBands.size() == 3 && controls.nodes[1].eqBands[0].frequencyHz == 200
        && controls.nodes[1].eqBands[1].frequencyHz == 1000 && controls.nodes[1].eqBands[2].frequencyHz == 5000,
        "Low / Mid / High defaults contain the three fixed filters");
    for (double frequency : {20., 1000., 18000.})
        expectNear(tone(controls, frequency) / tone(chain(), frequency), 1, 1e-5, "Flat tone node is transparent");
    const std::array<double, 3> frequencies{20, 1000, 18000};
    for (std::size_t band = 0; band < frequencies.size(); ++band) for (double db : {-12., 12.}) {
        auto changed = controls; changed.nodes[1].eqBands[band].gainDb = db;
        expectNear(tone(changed, frequencies[band]) / tone(controls, frequencies[band]), std::pow(10., db / 20.), .012,
            "Each tone dial independently applies its expected shelf/center boost or cut");
    }
    auto bypassed = controls;
    bypassed.nodes[1].eqBands[0].gainDb = 9; bypassed.nodes[1].eqBands[1].gainDb = -12; bypassed.nodes[1].eqBands[2].gainDb = 6;
    bypassed.nodes[1].bypass = true;
    for (double frequency : {20., 1000., 18000.})
        expectNear(tone(bypassed, frequency) / tone(chain(), frequency), 1, 1e-5, "Tone bypass restores unprocessed audio for low/mid/high frequencies");
    auto badTone = controls; badTone.nodes[1].eqBands.pop_back();
    rejects([&] { compileGraph(badTone); }, "Tone node requires exactly three bands");
    badTone = controls; badTone.nodes[1].eqBands[0].frequencyHz = 201;
    rejects([&] { compileGraph(badTone); }, "Tone frequencies remain fixed");
    badTone = controls; badTone.nodes[1].eqBands[1].type = "highPass";
    rejects([&] { compileGraph(badTone); }, "Tone filter types remain fixed");
    badTone = controls; badTone.nodes[1].eqBands[1].q = .5;
    rejects([&] { compileGraph(badTone); }, "Tone filter width remains fixed");
    badTone = controls; badTone.nodes[1].eqBands[2].enabled = false;
    rejects([&] { compileGraph(badTone); }, "Tone bands stay enabled; node bypass is explicit");
    for (double invalidGain : {-12.01, 12.01, std::numeric_limits<double>::infinity()}) {
        badTone = controls; badTone.nodes[1].eqBands[1].gainDb = invalidGain;
        rejects([&] { compileGraph(badTone); }, "Tone levels restricted to finite -12..12 dB");
    }

    // Changing a tone coefficient uses the same 480-frame wet-filter crossfade
    // as full EQ. For a settled flat DC input, the new filter starts with zero
    // state, giving an independently predictable first crossfaded sample.
    auto plan = compileGraph(controls);
    auto current = std::make_unique<GraphProcessor>(); constant(*current, plan, {.05f, -.025f}, 200);
    auto edited = controls; edited.nodes[1].eqBands[0].gainDb = 12;
    auto editedPlan = compileGraph(edited);
    auto next = std::make_unique<GraphProcessor>(); next->inherit(*current, plan, editedPlan);
    std::array<Frame, quantumFrames> samples; samples.fill({.05f, -.025f});
    GraphProcessor::Inputs input{}; input[0] = samples;
    next->process(editedPlan, input);
    expectNear(next->output(2).front().left, .05 + (.05 * editedPlan.nodes[1].eq[0].b0 - .05) / 480, 1e-7,
        "Tone edit begins at the first 1/480 crossfade step rather than switching filters abruptly");
    for (int block = 0; block < 120; ++block) {
        next->process(editedPlan, input);
        for (auto frame : next->output(2)) expect(std::isfinite(frame.left) && std::abs(frame.left) < 1
            && std::abs(frame.right + frame.left * .5f) < 1e-7, "Tone coefficient transition stays finite and preserves stereo independence");
    }
    expectNear(next->output(2).back().left, .05 * std::pow(10., 12. / 20.), 1e-5, "Tone shelf settles at expected DC gain");
    edited.nodes[1].bypass = true;
    auto bypassPlan = compileGraph(edited); auto bypassDsp = std::make_unique<GraphProcessor>(); bypassDsp->inherit(*next, editedPlan, bypassPlan);
    const double beforeBypass = next->output(2).back().left;
    double previous = beforeBypass;
    for (int block = 0; block < 2; ++block) {
        bypassDsp->process(bypassPlan, input);
        for (auto frame : bypassDsp->output(2)) {
            expect(frame.left <= previous + 1e-7 && previous - frame.left <= (beforeBypass - .05) / 480 + 2e-7,
                "Tone bypass transition ramps monotonically over 10 ms");
            previous = frame.left;
        }
    }
    expectNear(previous, .05, 1e-6, "Tone bypass reaches dry signal after 480 frames");

    controls.nodes.push_back(node("tone-control", "control")); controls.nodes.back().gainDb = -6;
    controls.edges.push_back({"tone-control-edge", "tone-control", "effect", "control"});
    plan = compileGraph(controls); current->reset(); constant(*current, plan, {.05f, -.025f}, 20);
    edited = controls; edited.nodes.back().gainDb = -12; editedPlan = compileGraph(edited);
    next->reset(); next->inherit(*current, plan, editedPlan);
    previous = current->output(2).back().left;
    const double controlledTarget = .05 * std::pow(10., -12. / 20.);
    // Float ramp accumulation is snapped to the exact target at its final
    // sample; allow its bounded accumulated rounding at that endpoint.
    const double maximumStep = (previous - controlledTarget) / 480 + 2e-6;
    for (int block = 0; block < 2; ++block) {
        next->process(editedPlan, input);
        for (auto frame : next->output(2)) {
            expect(frame.left <= previous + 1e-7 && previous - frame.left <= maximumStep,
                "Linked tone level changes preserve the bounded 10 ms ramp (previous=" + std::to_string(previous)
                    + ", current=" + std::to_string(frame.left) + ", maximum step=" + std::to_string(maximumStep) + ")"); previous = frame.left;
        }
    }
    expectNear(previous, controlledTarget, 1e-6, "Tone linked control reaches its own requested gain");
    edited.nodes.back().muted = true; editedPlan = compileGraph(edited);
    expectNear(constant(*next, editedPlan, {.05f, -.025f}).left, 0, 0, "Linked control mute silences tone output");

    // Repeated insertion/removal exercises the fixed eight-stage state banks;
    // same node IDs preserve routing while each coefficient set crossfades.
    auto changing = empty; plan = compileGraph(changing); current->reset(); constant(*current, plan, {.01f, -.005f}, 10);
    samples.fill({.01f, -.005f});
    for (int step = 0; step < 16; ++step) {
        if (step < 8) changing.nodes[1].eqBands.push_back({"lowShelf", true, 200, 1, .70710678});
        else changing.nodes[1].eqBands.pop_back();
        editedPlan = compileGraph(changing); next->reset(); next->inherit(*current, plan, editedPlan);
        expect(sameGraphTopology(plan, editedPlan), "Adding/removing an EQ band keeps graph routing topology unchanged");
        for (int block = 0; block < 4; ++block) {
            next->process(editedPlan, input);
            for (auto frame : next->output(2)) expect(std::isfinite(frame.left) && std::abs(frame.left) < .1
                && std::abs(frame.right + frame.left * .5f) < 1e-7, "Repeated zero-to-eight band edits remain finite and stereo-linear");
        }
        plan = editedPlan; current.swap(next);
    }
    expectNear(current->output(2).back().left, .01, 1e-6, "Removing every band returns the live EQ to flat output");

    Configuration stored = defaultConfiguration(); stored.graph = eight;
    auto savedTone = node("saved-tone", "tone"); savedTone.eqBands[0].gainDb = -7; savedTone.eqBands[2].gainDb = 4;
    stored.graph.nodes.push_back(savedTone);
    const auto encoded = Json(stored);
    const auto decoded = encoded.get<Configuration>(); validateConfiguration(decoded);
    expect(Json(decoded.graph) == Json(stored.graph), "Eight-band EQ and tone controls round-trip without changing filter values");
    const auto oldFive = chain("eq"); const auto oldEncoded = Json(oldFive);
    expect(Json(oldEncoded.get<AudioGraph>()) == oldEncoded && oldFive.nodes[1].eqBands.size() == 5,
        "Existing five-band EQ documents retain every original band exactly");
}
__declspec(noinline) void transparentFilterCompilation() {
    auto sparse = chain("eq");
    sparse.nodes[1].eqBands = {{"peak", true, 90, 0, 1}, {"peak", true, 400, 3, .7},
        {"lowShelf", false, 200, -18, .7}, {"highShelf", true, 7000, 0, .7},
        {"notch", true, 2500, 0, 2}, {"peak", true, 11000, -4, 1.2}};
    auto compact = sparse;
    compact.nodes[1].eqBands = {sparse.nodes[1].eqBands[1], sparse.nodes[1].eqBands[4], sparse.nodes[1].eqBands[5]};
    for (double frequency : {40., 400., 1400., 2500., 7000., 16000.})
        expectNear(tone(sparse, frequency), tone(compact, frequency), 0,
            "Inactive/flat bell and shelf bands do not alter remaining filters, including a zero-gain notch");
    auto before = compileGraph(sparse);
    auto layout = sparse;
    layout.panX += 80; layout.panY -= 33; layout.zoom = .5;
    for (auto& node : layout.nodes) { node.x += 400; node.y -= 123; node.name += " renamed"; node.collapsed = !node.collapsed; }
    layout.nodes[1].eqBands[1].resetFrequencyHz = 500;
    expect(before == compileGraph(layout), "Layout, labels and EQ reset anchors leave the compiled processing plan identical");
    layout.nodes[1].eqBands[1].enabled = false;
    expect(!(before == compileGraph(layout)), "Disabling an audible EQ band changes the processing plan");
    for (int changed = 0; changed < 5; ++changed) {
        auto graph = sparse;
        if (changed == 0) graph.nodes[1].gainDb = -3;
        if (changed == 1) graph.nodes[1].muted = true;
        if (changed == 2) graph.nodes[1].bypass = true;
        if (changed == 3) graph.edges[0].gainDb = -3;
        if (changed == 4) graph.edges[0].muted = true;
        expect(!(before == compileGraph(graph)), "Node and cable controls remain audible processing changes");
    }
    auto invalid = sparse; invalid.nodes[1].eqBands[0].type = "unsupported";
    rejects([&] { compileGraph(invalid); }, "A flat band's invalid type is still validated before identity elimination");
}
__declspec(noinline) void standaloneFiltersAndNotch() {
    auto high = chain("highPass"), low = chain("lowPass");
    const auto dry = chain();
    expectNear(tone(high, 80) / tone(dry, 80), std::sqrt(.5), .002, "Default high-pass is -3 dB at 80 Hz");
    expectNear(tone(low, 12000) / tone(dry, 12000), std::sqrt(.5), .002, "Default low-pass is -3 dB at 12 kHz");
    expect(tone(high, 20) / tone(dry, 20) < .065, "Default high-pass rejects low-frequency rumble");
    expectNear(tone(high, 1000) / tone(dry, 1000), 1, .002, "High-pass retains its passband");
    expectNear(tone(low, 1000) / tone(dry, 1000), 1, .002, "Low-pass retains its passband");
    expect(tone(low, 22000) / tone(dry, 22000) < .018, "Low-pass rejects high frequencies");
    high.nodes[1].eqBands[0].frequencyHz = 1000;
    expectNear(tone(high, 100) / tone(high, 50), 4, .02, "High-pass has a 12 dB/octave low-frequency slope");
    low.nodes[1].eqBands[0].frequencyHz = 100;
    expectNear(tone(low, 1000) / tone(low, 2000), 4, .08, "Low-pass has a 12 dB/octave stopband slope");

    auto notch = chain("eq"); notch.nodes[1].eqBands = {{"notch", true, 1000, 0, 4}};
    expect(tone(notch, 1000) / tone(dry, 1000) < 1e-5, "Notch removes the selected center frequency");
    expectNear(tone(notch, 100) / tone(dry, 100), 1, .002, "Notch retains distant lower frequencies");
    expectNear(tone(notch, 10000) / tone(dry, 10000), 1, .002, "Notch retains distant higher frequencies");
    auto narrow = notch; narrow.nodes[1].eqBands[0].q = 8;
    auto wide = notch; wide.nodes[1].eqBands[0].q = 1;
    expect(tone(wide, 1200) < tone(narrow, 1200) * .5, "Notch Q changes the rejected width");
    notch.nodes[1].eqBands[0].enabled = false;
    expectNear(tone(notch, 1000) / tone(dry, 1000), 1, 1e-5, "Disabled notch is transparent");

    for (const auto kind : {"highPass", "lowPass"}) {
        auto graph = chain(kind); graph.nodes[1].eqBands[0].frequencyHz = 1000;
        auto plan = compileGraph(graph); auto current = std::make_unique<GraphProcessor>();
        const auto sourceIndex = index(plan, "source"), effectIndex = index(plan, "effect"), outIndex = index(plan, "out");
        GraphChannels channels{}; channels[sourceIndex] = 2;
        graph.nodes[0].device.channel = 0; plan = compileGraph(graph);
        expect(graphSignalChannels(plan, channels)[outIndex] == 1, "Legacy selected-channel source remains mono through dedicated filters");
        graph.nodes[0].device.channel = -1; plan = compileGraph(graph);
        expect(graphSignalChannels(plan, channels)[outIndex] == 2, "Dedicated filters preserve a stereo signal layout");
        std::array<Frame, quantumFrames> samples; GraphProcessor::Inputs inputs{}; inputs[sourceIndex] = samples;
        for (int block = 0; block < 20; ++block) {
            for (std::size_t i = 0; i < quantumFrames; ++i) {
                const float value = static_cast<float>(.05 * std::sin(2 * std::numbers::pi * 1300 * (block * quantumFrames + i) / sampleRate));
                samples[i] = {value, -.3f * value};
            }
            current->process(plan, inputs);
            for (const auto sample : current->output(outIndex)) expectNear(sample.right, -.3 * sample.left, 1e-7, "Dedicated filter processes stereo channels independently");
        }
        auto edited = graph; edited.nodes[1].eqBands[0].frequencyHz = 3800; edited.nodes[1].eqBands[0].q = 1.2;
        const auto editedPlan = compileGraph(edited);
        auto next = std::make_unique<GraphProcessor>(); next->inherit(*current, plan, editedPlan);
        auto reference = std::make_unique<GraphProcessor>(); reference->inherit(*current, plan, plan);
        samples.fill({.05f, -.015f}); reference->process(plan, inputs); next->process(editedPlan, inputs);
        const double oldFirst = reference->output(effectIndex).front().left;
        const double newFirst = .05 * editedPlan.nodes[effectIndex].eq[0].b0;
        expect(sameGraphTopology(plan, editedPlan), "Dedicated filter parameter change preserves topology");
        expectNear(next->output(effectIndex).front().left, oldFirst + (newFirst - oldFirst) / 480, 1e-7,
            "Dedicated filter cutoff/Q edit uses the first 1/480 coefficient-crossfade step");
        constant(*next, editedPlan, {.05f, -.015f}, 80);
        edited.nodes[1].bypass = true; const auto bypassPlan = compileGraph(edited);
        auto bypass = std::make_unique<GraphProcessor>(); bypass->inherit(*next, editedPlan, bypassPlan);
        for (int block = 0; block < 2; ++block) bypass->process(bypassPlan, inputs);
        expectNear(bypass->output(outIndex).back().left, .05, 1e-6, "Dedicated filter bypass restores left dry audio after 10 ms");
        expectNear(bypass->output(outIndex).back().right, -.015, 1e-6, "Dedicated filter bypass restores right dry audio after 10 ms");
        edited.nodes[1].muted = true; const auto mutedPlan = compileGraph(edited);
        expectNear(constant(*bypass, mutedPlan, {.05f, -.015f}).left, 0, 0, "Dedicated filter bypass does not bypass mute");

        // Rapid edits also exercise interruption of a coefficient fade. The
        // output must stay finite and bounded with independent stereo states.
        current->reset(); constant(*current, plan, {.005f, -.0015f}, 20); samples.fill({.005f, -.0015f});
        for (int edit = 0; edit < 16; ++edit) {
            auto changed = graph; changed.nodes[1].eqBands[0].frequencyHz = edit % 2 ? 100 : 10000;
            changed.nodes[1].eqBands[0].q = edit % 2 ? .70710678 : 2;
            auto changedPlan = compileGraph(changed); next->reset(); next->inherit(*current, plan, changedPlan);
            next->process(changedPlan, inputs);
            for (const auto sample : next->output(outIndex)) {
                expect(std::isfinite(sample.left) && std::abs(sample.left) < .03, "Rapid filter edits remain bounded");
                expectNear(sample.right, -.3 * sample.left, 1e-7, "Rapid filter edits preserve independent stereo states");
            }
            plan = changedPlan; current.swap(next);
        }

        for (double frequency : {20., 20000.}) for (double q : {.1, 20.}) {
            auto extreme = graph; extreme.nodes[1].eqBands[0].frequencyHz = frequency; extreme.nodes[1].eqBands[0].q = q;
            const auto extremePlan = compileGraph(extreme); current->reset(); constant(*current, extremePlan, {}, 3);
            samples.fill({}); samples[0] = {.1f, 0};
            for (int block = 0; block < 100; ++block) {
                current->process(extremePlan, inputs);
                for (auto sample : current->output(outIndex)) expect(std::isfinite(sample.left) && std::abs(sample.left) <= 1 && sample.right == 0,
                    "Extreme cutoff/Q impulse remains finite without stereo crosstalk");
                samples.fill({});
            }
        }

        auto invalid = graph; invalid.nodes[1].eqBands.clear(); rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects no band");
        invalid = graph; invalid.nodes[1].eqBands.push_back(invalid.nodes[1].eqBands[0]); rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects multiple bands");
        invalid = graph; invalid.nodes[1].eqBands[0].enabled = false; rejects([&] { compileGraph(invalid); }, "Dedicated filter requires enabled band");
        invalid = graph; invalid.nodes[1].eqBands[0].type = "peak"; rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects mismatched shape");
        invalid = graph; invalid.nodes[1].eqBands[0].gainDb = 1; rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects band gain");
        for (double cutoff : {19., 20001., std::numeric_limits<double>::quiet_NaN()}) {
            invalid = graph; invalid.nodes[1].eqBands[0].frequencyHz = cutoff; rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects invalid cutoff");
        }
        for (double q : {.09, 20.01, std::numeric_limits<double>::infinity()}) {
            invalid = graph; invalid.nodes[1].eqBands[0].q = q; rejects([&] { compileGraph(invalid); }, "Dedicated filter rejects invalid Q");
        }
    }
    Configuration saved = defaultConfiguration(); saved.graph = chain("highPass");
    auto savedLow = node("saved-low", "lowPass"); savedLow.bypass = true; savedLow.eqBands[0].frequencyHz = 6800; savedLow.eqBands[0].q = 1.3;
    saved.graph.nodes.push_back(savedLow); auto savedEq = node("saved-eq", "eq");
    savedEq.eqBands = {{"highPass", true, 300, 7, .8}, {"lowPass", false, 16000, -4, 1.2}, {"notch", true, 2000, 0, 5}};
    saved.graph.nodes.push_back(savedEq); const auto document = Json(saved);
    const auto reopened = document.get<Configuration>(); validateConfiguration(reopened);
    expect(Json(reopened) == document, "Dedicated filters and notch persist exactly without changing saved EQ high/low-pass bands");
}
__declspec(noinline) void finiteAndTransitions() {
    auto graph = chain(); auto plan = compileGraph(graph); auto processor = std::make_unique<GraphProcessor>();
    auto value = constant(*processor, plan, {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity()});
    expectNear(value.left, 0, 0, "NaN input discarded"); expectNear(value.right, 0, 0, "Infinite input discarded");
    value = constant(*processor, plan, {std::numeric_limits<float>::max(), -std::numeric_limits<float>::max()});
    expectNear(value.left, 1, 0, "Positive saturation"); expectNear(value.right, -1, 0, "Negative saturation"); expect(processor->meter(2).clipped, "Clipping exposed");
    processor->reset(); constant(*processor, plan, {.4f, .4f});
    auto changed = graph; changed.nodes[1].gainDb = -60; auto after = compileGraph(changed);
    auto next = std::make_unique<GraphProcessor>(); next->inherit(*processor, plan, after);
    std::array<Frame, quantumFrames> input; input.fill({.4f, .4f}); GraphProcessor::Inputs views{}; views[0] = input;
    next->process(after, views); const auto out = next->output(2);
    expect(out.front().left > .39f && out.back().left < .21f && out.back().left > .19f, "Gain change ramps over 10 ms");
    next->process(after, views); expectNear(next->output(2).back().left, .0004, 1e-6, "Gain ramp reaches target");
    auto reordered = changed; std::reverse(reordered.nodes.begin(), reordered.nodes.end()); auto reorderedPlan = compileGraph(reordered);
    expect(sameGraphTopology(after, reorderedPlan), "Layout reorder is not topology change");
    auto reorderedDsp = std::make_unique<GraphProcessor>(); reorderedDsp->inherit(*next, after, reorderedPlan);
    views = {}; views[index(reorderedPlan, "source")] = input; reorderedDsp->process(reorderedPlan, views);
    expectNear(reorderedDsp->output(index(reorderedPlan, "out")).front().left, .0004, 1e-6, "Reordering preserves current ramps");
    changed.edges.pop_back(); expect(!sameGraphTopology(after, compileGraph(changed)), "Cable deletion triggers runtime crossfade");
    auto eq = chain("eq"); plan = compileGraph(eq); processor->reset(); constant(*processor, plan, {.2f, .2f});
    eq.nodes[1].eqBands[0].gainDb = 12; after = compileGraph(eq); next->reset(); next->inherit(*processor, plan, after);
    views = {}; views[0] = input;
    for (int block = 0; block < 12; ++block) {
        next->process(after, views);
        for (const auto frame : next->output(2)) expect(std::isfinite(frame.left) && std::abs(frame.left) <= 1, "EQ coefficient crossfade remains finite and bounded");
    }
}
std::string contents(const std::filesystem::path& path) { std::ifstream input(path, std::ios::binary); std::ostringstream text; text << input.rdbuf(); return text.str(); }
__declspec(noinline) void stereoMetersAndMicrophoneMono() {
    const auto defaults = defaultConfiguration();
    expect(defaults.graph.nodes.size() == 3 && defaults.graph.edges.size() == 2,
        "Fresh workspace has exactly one physical input and two output routes");
    expect(defaults.graph.nodes[0].id == "starter-input" && defaults.graph.nodes[0].kind == "microphone"
        && defaults.graph.nodes[1].id == "starter-virtual-output" && defaults.graph.nodes[1].kind == "virtualOutput"
        && defaults.graph.nodes[2].id == "starter-listening-output" && defaults.graph.nodes[2].kind == "output",
        "Fresh workspace identities and node kinds are stable");
    expect(defaults.graph.nodes[0].x == 48 && defaults.graph.nodes[0].y == 200
        && defaults.graph.nodes[1].x == 480 && defaults.graph.nodes[1].y == 48
        && defaults.graph.nodes[2].x == 480 && defaults.graph.nodes[2].y == 420
        && defaults.graph.panX == 20 && defaults.graph.panY == 20 && defaults.graph.zoom == 1,
        "Fresh workspace matches the desktop starter layout and camera");
    expect(!defaults.graph.nodes[0].muted && defaults.graph.nodes[1].muted && defaults.graph.nodes[2].muted
        && defaults.graph.nodes[2].gainDb == -24 && !defaults.graph.nodes[0].autoAssignDevice
        && defaults.graph.nodes[1].autoAssignDevice && !defaults.graph.nodes[2].autoAssignDevice,
        "Starter outputs stay muted and physical hardware requires explicit assignment");
    for (const auto& fresh : defaults.graph.nodes)
        expect(fresh.device.id.empty() && fresh.device.stableId.empty() && fresh.device.channel == -1 && !fresh.mono,
            "Every fresh endpoint is unassigned, stereo and Mono off");
    expect(defaults.graph.edges[0].from == "starter-input" && defaults.graph.edges[0].to == "starter-virtual-output"
        && defaults.graph.edges[1].from == "starter-input" && defaults.graph.edges[1].to == "starter-listening-output",
        "Starter input fans out directly to both output nodes");
    validateConfiguration(defaults);
    auto older = Json(defaults); for (auto& item : older["graph"]["nodes"]) item.erase("autoAssignDevice");
    const auto compatibleDefaults = older.get<Configuration>();
    expect(std::all_of(compatibleDefaults.graph.nodes.begin(), compatibleDefaults.graph.nodes.end(),
        [](const auto& item) { return item.autoAssignDevice; }), "Older graphs retain historical automatic-assignment eligibility");
    expect(Json(Json(defaults).get<Configuration>()) == Json(defaults), "Fresh assignment intent survives serialization");
    auto graph = chain(); graph.nodes[0].device.channel = -1; graph.nodes[0].gainDb = -6;
    auto plan = compileGraph(graph); auto dsp = std::make_unique<GraphProcessor>();
    constant(*dsp, plan, {.8f, -.2f});
    const double gain = std::pow(10., -6. / 20.);
    expectNear(dsp->meter(0).peakLeft, .8 * gain, 1e-6, "Left source meter follows its own post-gain signal");
    expectNear(dsp->meter(0).peakRight, .2 * gain, 1e-6, "Right source meter is independent of louder left channel");
    expectNear(dsp->meter(2).peakRight, .2 * gain, 1e-6, "Sink meter retains independent right signal");
    expectNear(dsp->meter(0).peak, dsp->meter(0).peakLeft, 0, "Legacy peak remains the larger channel");
    std::array<Frame, quantumFrames> input{}; input[20] = {.9f, .1f}; input[130] = {.2f, -.7f};
    GraphProcessor::Inputs views{}; views[0] = input; dsp->process(plan, views);
    expectNear(dsp->meter(0).peakLeft, .9 * gain, 1e-6, "Meter retains a left transient within its quantum");
    expectNear(dsp->meter(0).peakRight, .7 * gain, 1e-6, "Meter retains a separate right transient within its quantum");
    graph.nodes[0].gainDb = 0; plan = compileGraph(graph); constant(*dsp, plan, {2.f, -.1f});
    expect(dsp->meter(0).clipped && dsp->meter(2).clipped, "Clipping remains visible with stereo telemetry");
    expectNear(dsp->meter(2).peakLeft, 1, 0, "Left overload meter is bounded");
    expectNear(dsp->meter(2).peakRight, .1, 1e-6, "Left clipping does not inflate right meter");

    constant(*dsp, plan, {.6f, -.2f});
    graph.nodes[0].mono = true; auto monoPlan = compileGraph(graph);
    auto monoDsp = std::make_unique<GraphProcessor>(); monoDsp->inherit(*dsp, plan, monoPlan);
    expect(sameGraphTopology(plan, monoPlan), "Microphone Mono is a control change, not a topology change");
    input.fill({.6f, -.2f}); monoDsp->process(monoPlan, views);
    const auto first = monoDsp->output(0);
    expect(first.front().left > .59 && first.front().right < -.19, "Mono transition starts from current stereo audio");
    expectNear(first.back().left, .4, 1e-5, "Mono transition is halfway after 5 ms");
    monoDsp->process(monoPlan, views);
    expectNear(monoDsp->output(0).back().left, .2, 1e-6, "Microphone Mono averages signed L/R after 10 ms");
    expectNear(monoDsp->output(0).back().right, .2, 1e-6, "Mono is duplicated equally to both internal channels");
    monoDsp->process(monoPlan, views);
    expectNear(monoDsp->meter(0).peakLeft, .2, 1e-6, "Mono meter measures downmixed signal");
    expectNear(monoDsp->meter(0).peakRight, .2, 1e-6, "Mono right peak matches actual output");
    auto gainedMono = graph; gainedMono.nodes[0].gainDb = -6; const auto gainedPlan = compileGraph(gainedMono);
    expectNear(constant(*monoDsp, gainedPlan, {.6f, -.2f}).left, .2 * gain, 1e-6, "Microphone gain applies after mono average");
    expectNear(constant(*monoDsp, gainedPlan, {.6f, -.6f}).left, 0, 0, "Opposite-polarity channels cancel in mono");
    const auto restored = constant(*monoDsp, plan, {.6f, -.2f});
    expectNear(restored.left, .6, 1e-6, "Disabling Mono restores original left signal");
    expectNear(restored.right, -.2, 1e-6, "Disabling Mono restores original right signal");

    GraphChannels capture{}; capture[0] = 2;
    auto channels = graphSignalChannels(plan, capture);
    expect(channels[0] == 2 && channels[1] == 2 && channels[2] == 2, "Stereo layout propagates through gain and output");
    channels = graphSignalChannels(monoPlan, capture);
    expect(channels[0] == 1 && channels[1] == 1 && channels[2] == 1, "Microphone Mono layout propagates through graph");
    capture[0] = 1; channels = graphSignalChannels(plan, capture);
    expect(channels[0] == 1 && channels[2] == 1, "Native single-channel endpoint stays mono without toggle");
    capture[0] = 2; graph.nodes[0].mono = false; graph.nodes[0].device.channel = 1;
    channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 1 && channels[2] == 1, "Legacy selected microphone channel is mono");
    graph.nodes[0].device.channel = -1; graph.nodes[1].kind = "mono";
    channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 2 && channels[1] == 1 && channels[2] == 1, "Explicit mono node changes downstream layout");
    graph.nodes[1].bypass = true; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[1] == 2 && channels[2] == 2, "Bypassed mono node preserves stereo layout");
    graph.nodes[1].kind = "pan"; graph.nodes[1].bypass = false; graph.nodes[1].balance = -.5; capture[0] = 1;
    channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 1 && channels[1] == 2 && channels[2] == 2, "Balance creates distinct channels from mono input");
    graph.nodes[1].balance = 0; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[1] == 1, "Centered balance preserves mono layout");
    capture[0] = 0; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 0 && channels[2] == 0, "Unknown capture format is not mislabeled mono");
    graph.nodes[0].kind = "appInput"; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 2 && channels[2] == 2, "App loopback has a known stereo format");
    graph.nodes[0].kind = "virtualInput"; capture[0] = 1; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[0] == 1, "Single-channel virtual transport is represented honestly");
    graph.nodes.push_back(node("stereo-source", "appInput")); graph.edges.push_back({"stereo-in", "stereo-source", "effect"});
    graph.nodes[1].kind = "mix"; channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[1] == 2 && channels[2] == 2, "Mix with a stereo contributor remains stereo");
    graph.nodes[3].kind = "microphone"; graph.nodes[3].device.channel = -1;
    channels = graphSignalChannels(compileGraph(graph), capture);
    expect(channels[1] == 0, "Mono and unknown contributors cannot promise a mono mix");

    EngineStatus::Node status; status.id = "test"; status.peakLeft = .7f; status.peakRight = .2f; status.peak = .7f; status.channels = 2;
    const auto wire = Json(status);
    expectNear(wire["peakLeft"].get<float>(), .7, 1e-6, "Wire snapshot includes independent left peak");
    expectNear(wire["peakRight"].get<float>(), .2, 1e-6, "Wire snapshot includes independent right peak");
    expect(wire["channels"] == 2 && wire["peak"] == wire["peakLeft"], "Wire preserves legacy peak and signal layout");
}
__declspec(noinline) void dynamicsTelemetry() {
    auto graph = chain("compressor");
    graph.nodes[1].compressor = {-20, 4, .1, 5, 12};
    auto plan = compileGraph(graph); auto processor = std::make_unique<GraphProcessor>(); processor->prepare(plan);
    constant(*processor, plan, {.5f, .25f}, 100);
    const auto initial = processor->meter(1);
    expectNear(initial.detectorDb, 20 * std::log10(.5), .001, "Compressor telemetry reports its stereo-linked input detector");
    expectNear(initial.gainReductionDb, .75 * (20 * std::log10(.5) + 20), .01, "Compressor reduction excludes make-up and output volume");
    graph.nodes[1].compressor.makeupDb = 0; graph.nodes[1].gainDb = -18; plan = compileGraph(graph);
    constant(*processor, plan, {.5f, .25f}, 20);
    expectNear(processor->meter(1).gainReductionDb, initial.gainReductionDb, .01, "Changing make-up/output level does not invent gain reduction");
    graph.nodes[1].bypass = true; plan = compileGraph(graph); constant(*processor, plan, {.5f, .25f});
    expect(processor->meter(1).gainReductionDb == 0 && processor->meter(1).detectorDb == -96, "Bypassed compressor telemetry is inactive");
    graph.nodes[1].bypass = false; graph.nodes[1].muted = true; plan = compileGraph(graph); constant(*processor, plan, {.5f, .25f});
    expect(processor->meter(1).gainReductionDb == 0, "Legacy mute clears compressor reduction telemetry");
    graph.nodes[1].muted = false;
    auto control = node("linked", "control"); control.muted = true; graph.nodes.push_back(control);
    graph.edges.push_back({"linked-mute", "linked", "effect", "control"}); plan = compileGraph(graph);
    constant(*processor, plan, {.5f, .25f});
    expect(processor->meter(1).gainReductionDb == 0 && processor->meter(1).detectorDb == -96, "Linked control mute clears reduction telemetry");
    graph.nodes.pop_back(); graph.edges.pop_back(); plan = compileGraph(graph); constant(*processor, plan, {}, 400);
    expect(processor->meter(1).gainReductionDb < .001 && processor->meter(1).detectorDb == -96, "Silence returns compressor telemetry to zero rather than holding historic peaks");
    for (const char* kind : {"deEsser", "plosive"}) {
        auto bandGraph = chain(kind); bandGraph.nodes[1].deEsser.thresholdDb = -40; bandGraph.nodes[1].plosive.thresholdDb = -40;
        auto bandPlan = compileGraph(bandGraph); auto bandProcessor = std::make_unique<GraphProcessor>(); bandProcessor->prepare(bandPlan);
        std::array<Frame, quantumFrames> input; GraphProcessor::Inputs views{}; views[0] = input;
        const double frequency = std::string(kind) == "deEsser" ? 12000 : 60;
        for (unsigned block = 0; block < 100; ++block) {
            for (unsigned frame = 0; frame < quantumFrames; ++frame) {
                const float value = static_cast<float>(.3 * std::sin(2 * std::numbers::pi * frequency * (block * quantumFrames + frame) / sampleRate));
                input[frame] = {value, value};
            }
            bandProcessor->process(bandPlan, views);
        }
        expect(bandProcessor->meter(1).gainReductionDb > 1 && bandProcessor->meter(1).gainReductionDb <= 12.01,
            std::string(kind) + " publishes actual bounded shelf attenuation");
        expect(bandProcessor->meter(1).detectorDb > -40 && bandProcessor->meter(1).detectorDb < 0,
            std::string(kind) + " publishes measured band detector level");
        bandGraph.nodes[1].bypass = true; bandPlan = compileGraph(bandGraph); bandProcessor->process(bandPlan, views);
        expect(bandProcessor->meter(1).gainReductionDb == 0 && bandProcessor->meter(1).detectorDb == -96,
            std::string(kind) + " bypass clears reduction immediately without inventing output-volume measurements");
    }
    EngineStatus::Node status; status.gainReductionDb = 3.5f; status.detectorDb = -18; status.dynamicsFrame = 480;
    const auto wire = Json(status);
    expect(wire["gainReductionDb"] == 3.5f && wire["detectorDb"] == -18 && wire["dynamicsFrame"] == 480,
        "Dynamics status serializes real measurements and frame freshness independently of configuration");
}
__declspec(noinline) void persistence() {
    const auto root = std::filesystem::temp_directory_path() / (L"AudioMixer-OfflineGraph-" + std::to_wstring(GetCurrentProcessId()));
    expect(!std::filesystem::exists(root), "Isolated test directory does not preexist"); std::filesystem::create_directories(root);
    const auto path = root / "settings.json";
    Configuration legacyFixture; migrateLegacyGraph(legacyFixture);
    auto legacy = Json(legacyFixture); legacy["version"] = 1; legacy.erase("graph"); legacy["revision"] = 3; legacy["busA"] = {{"muted", true}, {"gainDb", -24}};
    const auto original = legacy.dump(3) + "\n"; { std::ofstream stream(path, std::ios::binary); stream << original; }
    ConfigurationStore store(path); auto config = store.load();
    expect(contents(path) == original, "Read-only migration does not rewrite settings");
    ++config.revision; store.save(config);
    expect(contents(path.string() + ".v1.bak") == original, "First save preserves exact v1 bytes");
    ++config.revision; config.graph.nodes[0].x = 123; config.graph.nodes[0].mono = true;
    config.graph.nodes[0].device.channel = -1; config.graph.nodes[1].device.channel = 1;
    config.graph.nodes.push_back(node("stored-high", "highPass")); config.graph.nodes.back().eqBands[0].frequencyHz = 140;
    config.graph.nodes.push_back(node("stored-low", "lowPass")); config.graph.nodes.back().bypass = true;
    config.graph.nodes.push_back(node("stored-notch", "eq")); config.graph.nodes.back().eqBands = {{"notch", true, 3000, 0, 2}};
    store.save(config);
    expect(contents(path.string() + ".v1.bak") == original, "Later saves preserve immutable v1 backup");
    const auto reopened = ConfigurationStore(path).load();
    expect(reopened.version == 2 && reopened.graph.nodes[0].x == 123 && reopened.graph.nodes[3].muted, "Graph round-trip preserves layout and mute");
    expect(reopened.graph.nodes[0].mono && reopened.graph.nodes[0].device.channel == -1 && reopened.graph.nodes[1].device.channel == 1,
        "Persistence retains explicit Mono separately from stereo capture and legacy channel selections");
    expect(Json(reopened.graph) == Json(config.graph), "Atomic disk save/load preserves dedicated filters, bypass and notch parameters exactly");
    auto old = Json(reopened); for (auto& value : old["graph"]["nodes"]) value.erase("mono");
    const auto compatible = old.get<Configuration>();
    expect(!compatible.graph.nodes[0].mono && compatible.graph.nodes[0].device.channel == -1 && compatible.graph.nodes[1].device.channel == 1,
        "Older graphs default Mono off without rewriting channel selections");
    for (const auto& suffix : {"", ".bak", ".v1.bak", ".new", ".established"}) std::filesystem::remove(path.string() + suffix);
    std::filesystem::remove(root); // exact newly-created directory, only when empty
}
__declspec(noinline) void fanoutQueues() {
    FrameQueue healthy(480), disconnected(480); std::array<Frame, quantumFrames> input, received;
    for (int block = 0; block < 20; ++block) {
        input.fill({static_cast<float>(block), 0}); healthy.push(input); disconnected.push(input);
        expect(healthy.pop(received) == quantumFrames && received[0].left == block, "Slow output cannot consume healthy output samples");
    }
    expect(disconnected.size() == 480 && disconnected.droppedFrames() > 0, "Disconnected output storage stays bounded");
    disconnected.requestClear(); disconnected.pop(received); expect(disconnected.size() == 0, "Recovery discards stale audio");
    input.fill({.25f, .5f}); disconnected.push(input); disconnected.pop(received); expectNear(received[0].left, .25, 0, "Reconnected output consumes current audio");
}
}
int main() {
    try {
        SetPriorityClass(GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS);
        migration(); controls(); physicalAndVirtualOutputMuteIsolation(); transparentBypass(); bypassLevelTransitions(); effects(); validation(); extendedEqualizerAndTone(); transparentFilterCompilation(); standaloneFiltersAndNotch(); finiteAndTransitions(); stereoMetersAndMicrophoneMono(); dynamicsTelemetry(); persistence(); fanoutQueues();
        std::cout << "PASS: " << assertions << " offline assertions; 64 routing migrations, controls, independent physical/virtual output mutes, transparent intermediate/control bypass, 0..8-band EQ, Low/Mid/High tone, high/low-pass nodes, notch, compressor, stereo meters, microphone Mono, signal channels, validation, transitions, persistence, and bounded fanout. No audio devices were opened.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL after " << assertions << " assertions: " << error.what() << '\n'; return 1; }
}
