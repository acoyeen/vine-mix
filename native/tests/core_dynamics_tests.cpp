// Synthetic audio only: no devices, production pipe, recordings or user settings.
#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <new>
#include <numbers>
#include <random>

static bool tracking{};
static std::uint64_t allocations{};
void* operator new(std::size_t size) { if (tracking) ++allocations; if (auto* p = std::malloc(size ? size : 1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t size) { return ::operator new(size); }
void operator delete(void* p) noexcept { if (tracking) ++allocations; std::free(p); }
void operator delete[](void* p) noexcept { ::operator delete(p); }
namespace {
using namespace am;
int checks{};
void check(bool result, const char* message) { ++checks; if (!result) throw std::runtime_error(message); }
void near(double actual, double expected, double tolerance, const char* message) {
    if (!std::isfinite(actual) || std::abs(actual - expected) > tolerance) {
        std::cerr << "actual=" << actual << " expected=" << expected << " tolerance=" << tolerance << '\n';
        check(false, message);
    }
    check(true, message);
}
template<class F> void rejects(F action, const char* message) {
    bool rejected{}; try { action(); } catch (const std::exception&) { rejected = true; } check(rejected, message);
}
AudioGraph chain(const std::string& kind) {
    AudioGraph graph; GraphNode source; source.id = "source"; source.kind = "virtualInput";
    GraphNode effect; effect.id = "effect"; effect.kind = kind;
    graph.nodes = {source, effect}; graph.edges = {{"signal", "source", "effect"}}; return graph;
}
struct Runner {
    AudioGraph graph; std::unique_ptr<GraphPlan> plan; std::unique_ptr<GraphProcessor> processor;
    std::array<Frame, quantumFrames> programme{}, key{};
    GraphProcessor::Inputs inputs{};
    explicit Runner(AudioGraph value) : graph(std::move(value)), plan(std::make_unique<GraphPlan>(compileGraph(graph))), processor(std::make_unique<GraphProcessor>()) {
        processor->prepare(*plan); inputs[0] = programme; inputs[2] = key;
        run(10);
    }
    void update() { auto next = std::make_unique<GraphPlan>(compileGraph(graph)); auto replacement = std::make_unique<GraphProcessor>();
        replacement->prepare(*next); replacement->inherit(*processor, *plan, *next); processor = std::move(replacement); plan = std::move(next); }
    void run(int blocks = 1) { for (int i = 0; i < blocks; ++i) processor->process(*plan, inputs); }
    Frame last() const { return processor->output(1).back(); }
};
double db(double gain) { return 20 * std::log10(std::max(gain, 1e-20)); }
void legacyCompressor() {
    auto graph = chain("compressor"); auto original = Json(graph);
    for (auto& node : original["nodes"]) { node.erase("expander"); node.erase("alignmentDelay"); node.erase("ducking"); node.erase("dynamicsUiVersion");
        node["compressor"].erase("mode"); node["compressor"].erase("detector"); node["compressor"].erase("ceilingDb"); }
    const auto loaded = original.get<AudioGraph>();
    check(compileGraph(graph) == compileGraph(loaded), "Old JSON compiles identically with default additions");
    Runner r(loaded); double gain = 1; std::mt19937 rng(807); std::uniform_real_distribution<float> random(-.7f, .7f);
    bool exact = true;
    for (int block = 0; block < 80; ++block) {
        for (auto& f : r.programme) f = {random(rng), random(rng)};
        r.run(); const auto output = r.processor->output(1); const auto& n = r.plan->nodes[1];
        for (std::size_t i = 0; i < quantumFrames; ++i) {
            const auto input = r.programme[i]; const double peak = std::max(std::abs(static_cast<double>(input.left)), std::abs(static_cast<double>(input.right)));
            const double target = peak > n.threshold ? std::pow(peak / n.threshold, 1. / n.ratio - 1.) : 1.;
            const double coefficient = target < gain ? n.attack : n.release;
            gain = target + coefficient * (gain - target);
            const double l = input.left, rr = input.right;
            // Existing graph uses dry + (wet-dry)*1, including its exact rounding.
            const auto expectedL = static_cast<float>(l + (l * gain - l));
            const auto expectedR = static_cast<float>(rr + (rr * gain - rr));
            exact = exact && output[i].left == expectedL && output[i].right == expectedR;
        }
    }
    check(exact, "Legacy compressor output remains bit-exact against original envelope/transfer implementation");
}
void sidechain() {
    auto graph = chain("compressor"); GraphNode key; key.id = "key"; key.kind = "virtualInput"; graph.nodes.push_back(key);
    graph.nodes[1].compressor = {-30, 6, 10, 250, 0, "compress", "external", -1};
    graph.edges.push_back({"detector", "key", "effect", "sidechain"}); Runner r(graph);
    r.programme.fill({.1f, -.04f}); r.run(300); near(r.last().left, .1, 2e-8, "Silent external detector never falls back to programme");
    r.key.fill({0, .5f}); r.run(300);
    const double expected = std::pow(.5 / std::pow(10., -30. / 20), 1. / 6 - 1);
    near(r.last().left / .1, expected, 1e-6, "Ducking preset follows external right-channel detector");
    near(r.last().right / r.last().left, -.4, 1e-7, "Ducking preserves linked stereo programme balance");
    check(r.processor->meter(1).gainReductionDb > 15 && r.processor->meter(1).detectorDb > -7, "Ducking reports measured external detector and attenuation");
    r.programme.fill({}); r.run(10); near(r.last().left, 0, 0, "Sidechain never leaks to audible left"); near(r.last().right, 0, 0, "Sidechain never leaks to audible right");
    GraphChannels capture{}; capture[0] = 1; capture[2] = 2;
    check(graphSignalChannels(*r.plan, capture)[1] == 1, "Sidechain stereo does not alter mono programme metadata");
    r.programme.fill({.1f, -.04f}); r.graph.edges.back().muted = true; r.update(); r.run(1000);
    near(r.last().left, .1, 1e-7, "Muted sidechain releases smoothly back to programme unity");
    r.graph.edges.pop_back(); r.update(); r.run(500); near(r.last().left, .1, 1e-7, "Disconnected external detector is silent and releases");
    auto invalid = graph; invalid.edges.push_back({"duplicate", "source", "effect", "sidechain"}); rejects([&] { compileGraph(invalid); }, "Reject two detector connections");
    invalid = graph; invalid.edges.back().to = "source"; rejects([&] { compileGraph(invalid); }, "Reject detector into non-Compressor");
    invalid = graph; invalid.nodes[2].kind = "gain"; invalid.edges.push_back({"feedback", "effect", "key"}); rejects([&] { compileGraph(invalid); }, "Sidechain dependencies participate in feedback loop rejection");
    invalid = graph; invalid.edges.back().kind = "audio"; check(!sameGraphTopology(compileGraph(graph), compileGraph(invalid)), "Changing sidechain to programme changes topology");
}
void limiter(const std::string& kind) {
    auto graph = chain(kind); auto& c = graph.nodes[1].compressor; c.mode = kind == "compressor" ? "limit" : "compress"; c.ceilingDb = -6; c.makeupDb = 24; c.detector = "external";
    graph.nodes[1].gainDb = 12; GraphNode control; control.kind = "control"; control.id = "control"; control.gainDb = 12;
    graph.nodes.push_back(control); graph.edges.push_back({"volume", "control", "effect", "control", 12}); Runner r(graph);
    std::mt19937 random(41); std::uniform_real_distribution<float> noise(-8, 8); bool bounded = true, stereo = true;
    for (int block = 0; block < 300; ++block) {
        if (block % 7 == 0) { r.graph.nodes[1].compressor.ceilingDb = -(block % 25); r.update(); }
        for (auto& f : r.programme) { f.left = noise(random); f.right = -.25f * f.left; }
        r.run(); const auto ceiling = r.plan->nodes[1].ceiling;
        for (const auto f : r.processor->output(1)) { bounded = bounded && std::isfinite(f.left) && std::abs(f.left) <= ceiling && std::abs(f.right) <= ceiling;
            stereo = stereo && std::abs(f.right + .25 * f.left) < 2e-7; }
    }
    check(bounded, "Limit enforces first-impulse/sample ceiling despite rapid edits, makeup and +36dB inherited gain");
    check(stereo, "Limit attenuation is stereo linked");
    check(r.processor->meter(1).gainReductionDb > 0, "Limit meters actual gain reduction");
    check(r.processor->meter(1).detectorDb > 20, "Limit detector meter measures the actual programme after inherited node/control gain");
    r.graph.nodes[1].gainDb = 0; r.graph.edges.pop_back(); r.graph.nodes[1].compressor.ceilingDb = -6; r.update();
    r.programme.fill({.1f, -.05f}); r.run(1000); near(r.last().left, .1, 1e-7, "Limit unity below ceiling; stored makeup does not boost Limit");
    r.programme.fill({.8f, -.4f}); r.run(); check(std::abs(r.last().left) <= r.plan->nodes[1].ceiling, "No high-ratio leakage above ceiling");
    float previous = r.last().left; double bypassStep{};
    r.graph.nodes[1].bypass = true; r.update();
    for (int block = 0; block < 10; ++block) { r.run(); for (const auto f : r.processor->output(1)) {
        bypassStep = std::max(bypassStep, std::abs(static_cast<double>(f.left - previous))); previous = f.left; } }
    check(bypassStep < .001, "Limit bypass releases attenuation over10ms without an instantaneous DC jump");
    near(r.last().left, .8, 3e-8, "Limit bypass restores ordinary dry signal; ceiling intentionally disabled");
    r.graph.nodes[1].bypass = false; r.update(); r.run(); bool firstBound = true;
    for (const auto f : r.processor->output(1)) firstBound = firstBound && std::abs(f.left) <= r.plan->nodes[1].ceiling;
    check(firstBound, "Re-enabling Limit enforces ceiling from the first sample through bypass fade");
    r.programme[0] = {std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()}; r.run();
    bool finite = true; for (const auto f : r.processor->output(1)) finite = finite && std::isfinite(f.left) && std::isfinite(f.right);
    check(finite, "Nonfinite input cannot poison limiter state");
}
void ducking() {
    auto graph = chain("ducking"); GraphNode key; key.id = "key"; key.kind = "virtualInput"; graph.nodes.push_back(key);
    graph.edges.push_back({"trigger", "key", "effect", "sidechain"}); Runner r(graph);
    r.programme.fill({.1f, -.04f}); r.run(300);
    near(r.last().left, .1, 2e-8, "Silent trigger remains unity and never falls back to programme");
    r.key.fill({0, .1f}); r.run(400);
    const double target = std::pow(10., -18. / 20.);
    near(r.last().left / .1, target, 1e-7, "Dedicated Ducking reaches its fixed 18 dB target from the right trigger channel");
    near(r.last().right / r.last().left, -.4, 1e-7, "Ducking attenuation remains stereo linked");
    near(r.processor->meter(1).gainReductionDb, 18, 1e-5, "Ducking meter reports measured reduction");
    near(r.processor->meter(1).detectorDb, -20, 1e-5, "Ducking meter reports the smoothed trigger");
    r.key.fill({.8f, 0}); r.run(400);
    near(r.last().left / .1, target, 1e-7, "Higher trigger levels do not increase fixed duck depth");
    r.programme.fill({}); r.run(10); near(r.last().left, 0, 0, "Trigger never leaks into left output"); near(r.last().right, 0, 0, "Trigger never leaks into right output");
    GraphChannels capture{}; capture[0] = 1; capture[2] = 2;
    check(graphSignalChannels(*r.plan, capture)[1] == 1, "Ducking trigger does not change programme channel metadata");
    r.programme.fill({.1f, -.04f}); r.key.fill({.02660725f, 0}); r.run(400);
    near(r.last().left / .1, target, 1e-7, "Three dB hysteresis retains active ducking at -31.5 dB");
    r.key.fill({.01584893f, 0}); r.run(1500); near(r.last().left, .1, 2e-8, "Trigger below closing threshold releases to unity");
    r.key.fill({.02660725f, 0}); r.run(400); near(r.last().left, .1, 2e-8, "Trigger inside hysteresis band cannot activate an inactive duck");
    r.key.fill({.5f, -.25f}); r.run(400);
    const float beforeDetach = r.last().left; r.graph.edges.pop_back(); r.update(); r.run();
    const auto firstAfterDetach = r.processor->output(1).front().left;
    check(firstAfterDetach > beforeDetach && firstAfterDetach - beforeDetach < .00002f,
        "Removing an active trigger inherits its gain and begins a smooth release on the first sample");
    check(r.last().left < .02f, "Trigger removal does not reset attenuation to unity");
    r.run(1500); near(r.last().left, .1, 2e-8, "Missing trigger fully recovers to unity");
    r.graph.edges.push_back({"trigger", "key", "effect", "sidechain"}); r.update(); r.run(400);
    const float beforeRewire = r.last().left; r.graph.edges.back().from = "source"; r.update(); r.run();
    check(std::abs(r.processor->output(1).front().left - beforeRewire) < .00002f, "Trigger rewiring retains detector and gain histories");
    r.graph.edges.back().muted = true; r.update(); r.run(1500); near(r.last().left, .1, 2e-8, "Muted trigger releases to unity");
    r.graph.edges.back().muted = false; r.update(); r.run(400);
    r.graph.nodes[1].ducking.amountDb = 0; r.update(); r.run(1500); near(r.last().left, .1, 2e-8, "Zero duck amount is unity even with an active trigger");
    // Keep an already-active detector to isolate each gain-envelope time constant.
    r.graph.nodes[1].ducking.amountDb = 18; r.update(); r.run(2);
    near(r.last().left / .1, target + (1 - target) * std::exp(-1.), 1e-7, "Attack reaches one gain-envelope time constant after exactly 10 ms");
    r.run(400); r.graph.nodes[1].ducking.amountDb = 0; r.update(); r.run(50);
    near(r.last().left / .1, 1 + (target - 1) * std::exp(-1.), 1e-7, "Release reaches one gain-envelope time constant after exactly 250 ms");
    r.graph.nodes[1].ducking.amountDb = 18; r.update(); r.run(400);
    r.graph.nodes[1].bypass = true; float previous = r.last().left; r.update(); double maximumStep{};
    for (int block = 0; block < 10; ++block) { r.run(); for (const auto f : r.processor->output(1)) {
        maximumStep = std::max(maximumStep, std::abs(static_cast<double>(f.left - previous))); previous = f.left; } }
    check(maximumStep < .0003, "Ducking bypass uses the established 10 ms transition");
    near(r.last().left, .1, 2e-8, "Ducking bypass passes the main audio at unity");
    r.graph.nodes[1].bypass = false; r.update(); r.run(10);
    near(r.last().left / .1, target, 1e-7, "Re-enabling Ducking fades to the retained attenuation");
    r.graph.nodes[1].ducking.thresholdDb = 0; r.update(); r.run(1500); near(r.last().left, .1, 2e-8, "Threshold edits smoothly release an active duck");
    r.graph.nodes[1].ducking.thresholdDb = -60; r.update(); r.run(400); near(r.last().left / .1, target, 1e-7, "Threshold edits smoothly activate an inactive duck");
    r.graph.edges.back().from = "key"; r.update();
    r.key.fill({std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()}); r.run(1500);
    near(r.last().left, .1, 2e-8, "Nonfinite trigger samples are silent and cannot poison the detector or gain");
    auto invalid = graph; invalid.edges.push_back({"duplicate", "source", "effect", "sidechain"});
    rejects([&] { compileGraph(invalid); }, "Ducking accepts only one trigger");
    invalid = graph; invalid.nodes[1].kind = "limiter";
    rejects([&] { compileGraph(invalid); }, "Dedicated Limiter has no trigger input");
    invalid = graph; invalid.nodes[2].kind = "gain"; invalid.edges.push_back({"feedback", "effect", "key"});
    rejects([&] { compileGraph(invalid); }, "Ducking trigger participates in cycle validation");
}
void expansion() {
    auto graph = chain("expander"); auto& e = graph.nodes[1].expander; e.holdMs = 0; e.hysteresisDb = 0; e.releaseMs = 5;
    Runner r(graph); r.programme.fill({.001f, -.0005f}); r.run(200);
    near(db(r.last().left / .001), -20, .0001, "2:1 downward expansion attenuates a signal20dB below threshold by20dB");
    r.programme.fill({.1f, -.05f}); r.run(100); near(r.last().left, .1, 2e-8, "Above-threshold voice opens expander");
    r.graph.nodes[1].expander.mode = "gate"; r.graph.nodes[1].expander.rangeDb = 40; r.update();
    r.programme.fill({.001f, -.0005f}); r.run(300); near(db(r.last().left / .001), -40, .001, "Gate reaches its selected40dB floor");
    near(r.last().right / r.last().left, -.5, 1e-7, "Gate detector and gain preserve stereo balance");
    check(r.processor->meter(1).gainReductionDb > 39, "Expander/gate report actual attenuation");
    r.graph.nodes[1].expander.rangeDb = 0; r.update(); r.run(100); near(r.last().left, .001, 1e-10, "Zero range gate is transparent");
    r.graph.nodes[1].expander = {}; r.graph.nodes[1].expander.ratio = 1; r.update(); r.run(500); near(r.last().left, .001, 1e-10, "Ratio1 expander is transparent");
    r.graph.nodes[1].expander = {}; auto& gate = r.graph.nodes[1].expander; gate.mode = "gate"; gate.holdMs = 100; gate.releaseMs = 5; gate.hysteresisDb = 6;
    r.update(); r.programme.fill({.02f, .01f}); r.run(100);
    r.programme.fill({.007f, .0035f}); r.run(100); near(r.last().left, .007, 3e-9, "Hysteresis keeps an open gate open inside the closing band");
    r.programme.fill({.0001f, .00005f}); r.run(10); near(r.last().left, .0001, 1e-10, "Gate hold preserves brief speech gaps");
    r.run(200); check(r.last().left < .0001f * .002, "Gate eventually closes after detector decay and hold");
    r.graph.nodes[1].bypass = true; r.update(); r.run(10); near(r.last().left, .0001, 1e-10, "Expander bypass is dry");
}
void delay() {
    for (std::uint32_t samples : {0u, 1u, 48u, 239u, 240u, 241u, 48000u, 96000u}) {
        auto graph = chain("alignmentDelay"); graph.nodes[1].alignmentDelay.samples = samples; Runner r(graph);
        const std::size_t count = static_cast<std::size_t>(samples) + quantumFrames; std::vector<Frame> result;
        for (std::size_t offset = 0; offset < count; offset += quantumFrames) {
            r.programme.fill({}); if (offset == 0) r.programme[0] = {.25f, -.125f}; r.run();
            const auto out = r.processor->output(1); result.insert(result.end(), out.begin(), out.end());
        }
        bool precise = true; for (std::size_t i = 0; i < result.size(); ++i) precise = precise && result[i].left == (i == samples ? .25f : 0) && result[i].right == (i == samples ? -.125f : 0);
        check(precise, "Alignment delay gives exact requested integer impulse position independently in both channels");
    }
    auto graph = chain("alignmentDelay"); graph.nodes[1].alignmentDelay.samples = 4800; Runner r(graph);
    r.programme.fill({.123f, -.06f}); r.run(100);
    for (std::uint32_t s : {0u, 96000u, 479u, 1u, 48000u}) { r.graph.nodes[1].alignmentDelay.samples = s; r.update(); r.run(); }
    r.graph.nodes[1].alignmentDelay.samples = 0; r.update(); r.run(4); near(r.last().left, .123, 4e-9, "Latest rapidly retargeted delay settles within20ms without resetting history");
    r.graph.nodes[1].alignmentDelay.samples = 96000; r.graph.nodes[1].bypass = true; r.update(); r.run(10); near(r.last().left, .123, 4e-9, "Delay bypass has zero delay");
}
void historyAndRealtime() {
    for (const std::string kind : {"expander", "alignmentDelay", "compressor", "limiter", "ducking"}) {
        auto graph = chain(kind);
        if (kind == "ducking") { GraphNode key; key.id = "key"; key.kind = "virtualInput"; graph.nodes.push_back(key); graph.edges.push_back({"trigger", "key", "effect", "sidechain"}); }
        Runner r(graph); r.key.fill({.2f, -.1f}); auto other = std::make_unique<GraphProcessor>(); other->prepare(*r.plan);
        r.programme.fill({.001f, -.0003f}); r.run(100); bool identical = true, bounded = true; double largestStep{}; float previous = r.last().left;
        const auto before = allocations;
        for (int update = 0; update < 100; ++update) {
            r.graph.nodes[1].alignmentDelay.samples = update % 2 ? 0 : 48;
            r.graph.nodes[1].expander.thresholdDb = update % 2 ? -60 : -20;
            r.graph.nodes[1].expander.mode = update % 3 ? "expander" : "gate";
            r.graph.nodes[1].compressor.mode = update % 3 ? "compress" : "limit";
            r.graph.nodes[1].ducking.amountDb = update % 2 ? 36 : 3;
            r.graph.nodes[1].ducking.thresholdDb = update % 3 ? -30 : 0;
            auto next = std::make_unique<GraphPlan>(compileGraph(r.graph));
            tracking = true; other->inherit(*r.processor, *r.plan, *next);
            r.processor->process(*next, r.inputs); other->process(*next, r.inputs);
            for (std::size_t i = 0; i < quantumFrames; ++i) {
                const auto a = r.processor->output(1)[i], b = other->output(1)[i]; identical = identical && a.left == b.left && a.right == b.right;
                bounded = bounded && std::isfinite(a.left) && std::abs(a.left) <= .0011;
                largestStep = std::max(largestStep, std::abs(static_cast<double>(a.left - previous))); previous = a.left;
            }
            tracking = false; r.plan = std::move(next);
        }
        check(allocations == before, "Realtime configure/process/history inheritance allocate and free no heap memory");
        check(identical, "State publication exactly preserves detector, delay history and in-flight transitions");
        check(bounded && largestStep < .00003, "Rapid continuous edits remain finite/bounded with smooth DC transitions");
        tracking = true; r.processor->reset(); tracking = false;
        check(allocations == before, "Realtime reset does not allocate/free");
    }
}
void persistence() {
    auto graph = chain("expander"); graph.nodes[1].expander = {"gate", -52, 7, 3, 81, 401, 72, 8}; graph.nodes[1].bypass = true;
    GraphNode d; d.id = "delay"; d.kind = "alignmentDelay"; d.alignmentDelay.samples = 12345; d.x = 82; d.y = -51; graph.nodes.push_back(d);
    GraphNode c; c.id = "compress"; c.kind = "compressor"; c.compressor = {-31, 8, 9, 270, 1, "limit", "external", -3.4}; graph.nodes.push_back(c);
    graph.edges.push_back({"sidechain", "source", "compress", "sidechain"});
    GraphNode duck; duck.id = "duck"; duck.kind = "ducking"; duck.name = "Custom Duck"; duck.ducking = {-33.3, 21.45, .25, 333.75};
    duck.gainDb = -4; duck.x = -123; duck.y = 456; duck.bypass = true; graph.nodes.push_back(duck);
    graph.edges.push_back({"trigger", "source", "duck", "sidechain", -2, true});
    GraphNode limit; limit.id = "limit"; limit.kind = "limiter"; limit.compressor.ceilingDb = -2.5; limit.compressor.releaseMs = 135.25; graph.nodes.push_back(limit);
    GraphNode modern; modern.id = "modern"; modern.kind = "compressor"; modern.dynamicsUiVersion = 1; graph.nodes.push_back(modern);
    const auto json = Json(graph); const auto roundtrip = json.get<AudioGraph>();
    check(Json(roundtrip) == json && compileGraph(roundtrip) == compileGraph(graph), "New settings, sidechain, bypass and layout survive exact JSON roundtrip");
    const auto roundTripConfiguration = [&] { auto cfg = defaultConfiguration(); cfg.graph = graph; return Json(cfg).get<Configuration>(); }();
    check(Json(roundTripConfiguration.graph) == json, "Whole configuration roundtrip preserves additions");
    auto interfaceOnly = graph; interfaceOnly.nodes[3].dynamicsUiVersion = 1;
    check(compileGraph(interfaceOnly) == compileGraph(graph), "Interface version changes no DSP values or audio equivalence");
    for (auto bad : {Json(-1), Json(-4294967296LL), Json(1.5), Json(96001), Json(1e30), Json(true), Json("24"), Json(nullptr)}) {
        auto invalid = json; invalid["nodes"][2]["alignmentDelay"]["samples"] = bad;
        rejects([&] { compileGraph(invalid.get<AudioGraph>()); }, "Delay rejects fractional/negative/overflow/string/null samples");
    }
    for (const auto field : {"thresholdDb", "ratio", "attackMs", "holdMs", "releaseMs", "rangeDb", "hysteresisDb"}) {
        auto invalid = graph; auto value = Json(invalid); value["nodes"][1]["expander"][field] = 999999;
        rejects([&] { compileGraph(value.get<AudioGraph>()); }, "Expander rejects each out-of-range setting");
    }
    auto invalid = graph; invalid.nodes[1].expander.thresholdDb = std::numeric_limits<double>::quiet_NaN(); rejects([&] { compileGraph(invalid); }, "Nonfinite dynamics setting rejected");
    invalid = graph; invalid.nodes[3].compressor.mode = "unknown"; rejects([&] { compileGraph(invalid); }, "Invalid compressor mode rejected");
    invalid = graph; invalid.nodes[3].compressor.detector = "auto"; rejects([&] { compileGraph(invalid); }, "Invalid detector rejected");
    invalid = graph; invalid.nodes[3].compressor.ceilingDb = 1; rejects([&] { compileGraph(invalid); }, "Unsafe positive ceiling rejected");
    for (const auto field : {"thresholdDb", "amountDb", "attackMs", "releaseMs"}) {
        auto value = json; value["nodes"][4]["ducking"][field] = 999999;
        rejects([&] { compileGraph(value.get<AudioGraph>()); }, "Ducking rejects each out-of-range setting");
        value["nodes"][4]["ducking"][field] = nullptr;
        rejects([&] { compileGraph(value.get<AudioGraph>()); }, "Ducking rejects null numeric settings");
    }
    invalid = graph; invalid.nodes[4].ducking.amountDb = std::numeric_limits<double>::quiet_NaN();
    rejects([&] { compileGraph(invalid); }, "Ducking rejects nonfinite settings");
    auto missing = json; for (auto& node : missing["nodes"]) { node.erase("ducking"); node.erase("dynamicsUiVersion"); }
    const auto legacy = missing.get<AudioGraph>();
    check(legacy.nodes[3].kind == "compressor" && legacy.nodes[3].dynamicsUiVersion == 0 && legacy.nodes[3].compressor.mode == "limit"
        && legacy.nodes[3].compressor.detector == "external", "Absent interface version leaves legacy compressor identity and modes unchanged");
    check(Json(legacy.nodes[3].compressor) == Json(graph.nodes[3].compressor), "Legacy hidden compressor settings retain exact values");
}
void realtimeBudget() {
    AudioGraph graph; GraphNode source; source.id = "source"; source.kind = "virtualInput"; graph.nodes.push_back(source);
    for (int i = 0; i < 30; ++i) { GraphNode node; node.id = "delay-" + std::to_string(i); node.kind = "alignmentDelay"; node.alignmentDelay.samples = 96000;
        graph.nodes.push_back(node); graph.edges.push_back({"edge-" + std::to_string(i), i ? "delay-" + std::to_string(i-1) : "source", node.id}); }
    auto plan = std::make_unique<GraphPlan>(compileGraph(graph)); auto a = std::make_unique<GraphProcessor>(), b = std::make_unique<GraphProcessor>(); a->prepare(*plan); b->prepare(*plan);
    std::array<Frame, quantumFrames> signal{}; signal.fill({.01f, -.01f}); GraphProcessor::Inputs inputs{}; inputs[0] = signal;
    std::array<double, 400> elapsed{};
    for (auto& us : elapsed) { const auto start = std::chrono::steady_clock::now(); b->inherit(*a, *plan, *plan); b->process(*plan, inputs);
        us = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now()-start).count(); a.swap(b); }
    std::sort(elapsed.begin(), elapsed.end());
    std::cout << "30 maximum-length delays, inherit+5ms process: median=" << elapsed[200] << "us p99=" << elapsed[396] << "us maximum=" << elapsed.back() << "us (diagnostic; not audio device latency).\n";
    check(std::isfinite(elapsed.back()), "Worst-sized bounded delay-history publication benchmark completes");
}
}
int main() {
    try { legacyCompressor(); sidechain(); limiter("compressor"); limiter("limiter"); ducking(); expansion(); delay(); historyAndRealtime(); persistence(); realtimeBudget();
        std::cout << "PASS: " << checks << " focused core dynamics/delay checks; legacy sound, fixed-depth ducking/trigger isolation, strict sample peaks, gate/expansion, exact alignment, histories, allocations, JSON/validation.\n"; return 0;
    } catch (const std::exception& e) { tracking = false; std::cerr << "FAIL: " << e.what() << '\n'; return 1; }
}
