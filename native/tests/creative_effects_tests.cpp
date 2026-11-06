// In-memory synthetic signals only: no endpoint, engine or personal settings.
#include "am/advanced_effects.hpp"
#include "am/configuration.hpp"
#include "am/mix_clock.hpp"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <new>
#include <numbers>
#include <optional>
#include <random>
#include <vector>
#include <windows.h>
#include <avrt.h>

static bool trackAllocations{};
static std::uint64_t allocationOperations{};
void* operator new(std::size_t size) { if (trackAllocations) ++allocationOperations; if (auto p = std::malloc(size ? size : 1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t size) { return ::operator new(size); }
void operator delete(void* p) noexcept { if (trackAllocations) ++allocationOperations; std::free(p); }
void operator delete[](void* p) noexcept { ::operator delete(p); }

namespace {
using namespace am;
std::uint64_t checks{};
void require(bool condition, const std::string& message) { ++checks; if (!condition) throw std::runtime_error(message); }
// Match Engine::mixLoop's scheduling only. This remains synthetic processing
// qualification, not an audio-device deadline or live sound-quality test.
struct TimingSchedule {
    HANDLE task{}, timer{};
    LARGE_INTEGER frequency{};
    std::optional<MixClock> clock;
    TimingSchedule() {
        DWORD index{}; task = AvSetMmThreadCharacteristicsW(L"Pro Audio", &index);
        require(task != nullptr, "Synthetic timing acquired the engine's Pro Audio MMCSS class");
        timer = CreateWaitableTimerExW(nullptr, nullptr, 0x00000002, TIMER_ALL_ACCESS);
        if (!timer) timer = CreateWaitableTimerW(nullptr, FALSE, nullptr);
        if (!timer) { AvRevertMmThreadCharacteristics(task); task = nullptr; throw std::runtime_error("Cannot create synthetic timing clock"); }
        LARGE_INTEGER now{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&now);
        clock.emplace(frequency.QuadPart * static_cast<LONGLONG>(quantumFrames) / sampleRate, now.QuadPart);
    }
    ~TimingSchedule() { if (timer) { CancelWaitableTimer(timer); CloseHandle(timer); } if (task) AvRevertMmThreadCharacteristics(task); }
    void pace() {
        LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
        const auto next = clock->advance(now.QuadPart);
        if (next <= now.QuadPart) return;
        LARGE_INTEGER due{}; due.QuadPart = -std::max<LONGLONG>(1, (next - now.QuadPart) * 10000000 / frequency.QuadPart);
        if (!SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE) || WaitForSingleObject(timer, 1000) != WAIT_OBJECT_0)
            throw std::runtime_error("Synthetic timing clock failed");
    }
    void restore() {
        const bool restored = AvRevertMmThreadCharacteristics(task); if (restored) task = nullptr;
        require(restored, "Synthetic timing restored the calling thread's original scheduling");
    }
};
void expectNear(double value, double expected, double tolerance, const std::string& message) {
    require(std::isfinite(value) && std::abs(value - expected) <= tolerance,
        message + " actual=" + std::to_string(value) + " expected=" + std::to_string(expected));
}
template<class F> void rejects(F action, const std::string& message) {
    bool failed{}; try { action(); } catch (const std::exception&) { failed = true; } require(failed, message);
}
GraphNode node(std::string id, std::string kind) { GraphNode n; n.id = n.name = std::move(id); n.kind = std::move(kind); return n; }
AudioGraph chain(const char* kind) {
    AudioGraph graph; graph.nodes = {node("input", "virtualInput"), node("effect", kind), node("output", "output")};
    graph.edges = {{"in", "input", "effect"}, {"out", "effect", "output"}}; return graph;
}
std::vector<Frame> render(const AudioGraph& graph, const std::vector<Frame>& input, std::size_t outputIndex = 1) {
    const auto plan = compileGraph(graph); auto processor = std::make_unique<GraphProcessor>(); processor->prepare(plan);
    // Let the existing 10-ms graph gain/bypass ramps settle on silence before
    // testing effect impulse amplitudes. This does not excite effect history.
    processor->process(plan, {}); processor->process(plan, {});
    std::vector<Frame> result; result.reserve(input.size()); GraphProcessor::Inputs inputs{};
    for (std::size_t offset = 0; offset < input.size(); offset += quantumFrames) {
        inputs[0] = std::span(input).subspan(offset, quantumFrames); processor->process(plan, inputs);
        // Measure the effect, before the output sink's sample clipping. That
        // separate nonlinearity must not contaminate the aliasing comparison.
        result.insert(result.end(), processor->output(outputIndex).begin(), processor->output(outputIndex).end());
    }
    return result;
}
double sine(std::size_t frame, double hz) { return std::sin(2 * std::numbers::pi * hz * frame / sampleRate); }
std::vector<Frame> signal(double hz, double amplitude, std::size_t seconds = 2) {
    std::vector<Frame> values(sampleRate * seconds);
    for (std::size_t i = 0; i < values.size(); ++i) values[i] = {static_cast<float>(amplitude * sine(i, hz)), 0};
    return values;
}
double rms(const std::vector<Frame>& values, std::size_t first = sampleRate) {
    double energy{}; for (std::size_t i = first; i < values.size(); ++i) energy += static_cast<double>(values[i].left) * values[i].left;
    return std::sqrt(energy / (values.size() - first));
}
double magnitude(const std::vector<Frame>& values, double hz) {
    double real{}, imaginary{};
    for (std::size_t i = sampleRate; i < values.size(); ++i) {
        const double phase = 2 * std::numbers::pi * hz * i / sampleRate;
        real += values[i].left * std::cos(phase); imaginary += values[i].left * std::sin(phase);
    }
    return 2 * std::hypot(real, imaginary) / (values.size() - sampleRate);
}
void persistenceAndValidation() {
    Configuration config; config.graph = chain("echo");
    config.graph.nodes[1].echo = {300.1, 5, .2718281828459045};
    auto distortion = node("saved-distortion", "distortion"); distortion.distortion = {12.3456789012345, 6543.21098765432, .6180339887498948, -6.12345678901234};
    config.graph.nodes.push_back(distortion);
    const auto stored = Json(config); const auto loaded = Json::parse(stored.dump()).get<Configuration>();
    require(Json(loaded) == stored && loaded.graph.nodes[1].echo == config.graph.nodes[1].echo
        && loaded.graph.nodes[3].distortion == distortion.distortion, "Creative settings preserve exact double values through real native JSON serialization");
    auto old = stored; for (auto& n : old["graph"]["nodes"]) { n.erase("echo"); n.erase("distortion"); }
    const auto legacy = old.get<Configuration>();
    require(legacy.graph.nodes[1].echo == EchoSettings{} && legacy.graph.nodes[3].distortion == DistortionSettings{},
        "Legacy graphs missing creative settings load the agreed defaults");
    const double infinity = std::numeric_limits<double>::infinity(), nan = std::numeric_limits<double>::quiet_NaN();
    for (double bad : {19.99, 2000.01, infinity, nan}) { auto graph = chain("echo"); graph.nodes[1].echo.timeMs = bad; rejects([&] { compileGraph(graph); }, "Invalid Echo time rejected"); }
    for (int bad : {0, 9, -1}) { auto graph = chain("echo"); graph.nodes[1].echo.repeats = bad; rejects([&] { compileGraph(graph); }, "Invalid Echo repeat count rejected"); }
    for (const auto& bad : {Json(0), Json(9), Json(3.1), Json("3"), Json(true), Json(nullptr)})
        rejects([&] { Json{{"repeats", bad}}.get<EchoSettings>(); }, "Malformed/nonintegral repeat counts are never truncated or defaulted");
    require(Json{{"repeats", 3.0}}.get<EchoSettings>().repeats == 3, "A numerically integral JSON number is accepted as a repeat count");
    for (const auto& bad : {Json(-.01), Json(.91), Json("0.35"), Json(true), Json(nullptr)})
        rejects([&] { Json{{"feedback", bad}, {"repeats", 3}}.get<EchoSettings>(); }, "Invalid legacy feedback is rejected even when repeats is explicit");
    for (double feedback : {0., .0099, .01, .1, .35, .5, .9}) {
        int expected = 1; for (double level = feedback; expected < 8 && level >= .01; level *= feedback) ++expected;
        const auto migrated = Json{{"feedback", feedback}}.get<EchoSettings>();
        require(migrated.repeats == expected && !Json(migrated).contains("feedback"), "Legacy feedback migrates once to a finite canonical repeat count");
        require(Json{{"feedback", feedback}, {"repeats", 2}}.get<EchoSettings>().repeats == 2, "Explicit repeats wins over valid legacy feedback");
    }
    require(Json::object().get<EchoSettings>() == EchoSettings{}, "An empty Echo record retains the three-repeat default");
    for (const char* kind : {"echo", "distortion"}) for (double bad : {-.001, 1.001, infinity, nan}) {
        auto graph = chain(kind); if (graph.nodes[1].kind == "echo") graph.nodes[1].echo.mix = bad; else graph.nodes[1].distortion.mix = bad;
        rejects([&] { compileGraph(graph); }, "Invalid creative dry/wet mix rejected");
    }
    for (double bad : {-.001, 36.001, infinity, nan}) { auto graph = chain("distortion"); graph.nodes[1].distortion.driveDb = bad; rejects([&] { compileGraph(graph); }, "Invalid Distortion drive rejected"); }
    for (double bad : {499.9, 18000.1, infinity, nan}) { auto graph = chain("distortion"); graph.nodes[1].distortion.toneHz = bad; rejects([&] { compileGraph(graph); }, "Invalid Distortion tone rejected"); }
    for (double bad : {-24.001, .001, infinity, nan}) { auto graph = chain("distortion"); graph.nodes[1].distortion.outputDb = bad; rejects([&] { compileGraph(graph); }, "Invalid Distortion output rejected"); }
    for (const char* kind : {"echo", "distortion"}) {
        auto graph = chain(kind); graph.edges[1].fromPort = "wet";
        if (graph.nodes[1].kind == "distortion") rejects([&] { compileGraph(graph); }, "Distortion exposes only its standard audio output");
        else require(compileGraph(graph).edges[1].fromPort == GraphPort::wet, "Echo exposes its independent Echo output through the wet socket key");
        graph = chain(kind); graph.edges.push_back({"cycle", "effect", "effect"});
        rejects([&] { compileGraph(graph); }, "Internal Echo feedback does not permit graph feedback cycles");
        const auto plan = compileGraph(chain(kind)); GraphChannels input{}; input[0] = 1;
        require(graphSignalChannels(plan, input)[1] == 1, "Creative processing retains a mono signal layout");
        input[0] = 2; require(graphSignalChannels(plan, input)[1] == 2, "Creative processing retains stereo layout");
    }
    require(AdvancedEffect::latencyFrames(GraphKind::echo) == 0 && AdvancedEffect::latencyFrames(GraphKind::distortion) == 32,
        "Distortion reports its fixed 32-frame latency; Echo's direct path remains undelayed");
}
void echoImpulseAndBounds() {
    for (double ms : {20., 300., 2000.}) for (int repeats : {1, 3, 8}) {
        auto graph = chain("echo"); graph.nodes[1].echo = {ms, repeats, .25};
        const auto delay = static_cast<std::size_t>(ms * 48);
        std::vector<Frame> input(sampleRate * 17); input[240] = {.4f, -.2f};
        const auto output = render(graph, input);
        expectNear(output[240].left, .3, 2e-8, "Echo keeps its direct signal at the chosen linear mix");
        for (int repeat = 1; repeat <= repeats; ++repeat) {
            const double expected = .4 * .25 * std::ldexp(1., -repeat) / (1 - std::ldexp(1., -repeats));
            expectNear(output[240 + repeat * delay].left, expected, 3e-8, "Each finite Echo repeat occurs at Time with normalized half-level decay");
            expectNear(output[240 + repeat * delay].right, -expected / 2, 2e-8, "Echo keeps independent stereo signs and level ratio");
        }
        require(std::all_of(output.begin() + 241, output.begin() + 240 + delay, [](Frame f) { return f.left == 0 && f.right == 0; }),
            "Echo produces no early reflection before Time");
        require(std::all_of(output.begin() + 241 + repeats * delay, output.end(), [](Frame f) { return f.left == 0 && f.right == 0; }),
            "Echo stops completely after exactly the requested number of repeats");
    }
    auto graph = chain("echo"); graph.nodes[1].echo = {20.01, 1, 1};
    std::vector<Frame> impulse(sampleRate); impulse[240].left = 1;
    const auto fractional = render(graph, impulse);
    expectNear(fractional[1200].left, .52, 1e-6, "Fractional Echo time uses the newer interpolated tap");
    expectNear(fractional[1201].left, .48, 1e-6, "Fractional Echo time uses the older interpolated tap");
    require(std::all_of(fractional.begin(), fractional.end(), [](Frame f) { return f.right == 0; }), "An Echo in one channel creates no cross-talk");
    graph.nodes[1].echo.mix = 0;
    const auto dry = render(graph, impulse);
    require(std::equal(dry.begin(), dry.end(), impulse.begin(), [](Frame a, Frame b) { return a.left == b.left && a.right == b.right; }),
        "Zero Echo mix is exact undelayed dry audio");

    GraphPlan::Node plan; plan.kind = GraphKind::echo; plan.echo = {20, 8, 1}; AdvancedEffect echo(GraphKind::echo); echo.configure(plan);
    double maximum{}, last{};
    for (std::size_t frame = 0; frame < sampleRate * 3; ++frame) {
        const auto result = echo.process({.7f, -.3f}).wet; maximum = std::max(maximum, std::abs(static_cast<double>(result.left))); last = result.left;
    }
    require(maximum <= .700001 && last > .699, "All eight finite repeats remain peak-bounded on coherent DC");
    std::mt19937 random(5731); std::uniform_real_distribution<float> noise(-.7f, .7f);
    bool finite = true, bounded = true;
    for (std::size_t frame = 0; frame < sampleRate; ++frame) {
        if (frame % 120 == 0) { plan.echo.timeMs = frame % 240 ? 20 : 2000; plan.echo.repeats = frame % 360 ? 8 : 1; plan.echo.mix = frame % 480 ? 1 : 0; echo.configure(plan); }
        const auto value = echo.process({noise(random), noise(random)}).wet;
        finite &= std::isfinite(value.left) && std::isfinite(value.right); bounded &= std::abs(value.left) <= .700001 && std::abs(value.right) <= .700001;
    }
    require(finite && bounded, "Rapid Time/Repeats/Mix edits stay finite and peak-bounded while old tails fade and requests coalesce");
    echo.reset(); echo.configure(plan); bool silent = true;
    for (std::size_t frame = 0; frame < sampleRate * 3; ++frame) { const auto value = echo.process({}).wet; silent &= value.left == 0 && value.right == 0; }
    require(silent, "Echo reset clears all repeats, positions and transitions");
    const float largest = std::numeric_limits<float>::max(); bool extremeFinite = true;
    plan.echo = {20, 8, .5}; echo.configure(plan);
    for (std::size_t frame = 0; frame < 2400; ++frame) {
        const float input = frame % 2 ? largest : -largest; const auto value = echo.process({input, -input}).wet;
        extremeFinite &= std::isfinite(value.left) && std::isfinite(value.right);
    }
    require(extremeFinite, "Echo convex sums remain finite even for opposite extreme finite samples");
}
void echoOutputsAndEdits() {
    auto graph = chain("echo"); graph.nodes[1].echo = {100, 3, 0};
    auto wetGraph = graph; wetGraph.edges[1].fromPort = "wet";
    std::vector<Frame> input(sampleRate); input[240] = {.4f, -.2f};
    const auto dry = render(graph, input), wet = render(wetGraph, input, 2);
    wetGraph.nodes[1].echo.mix = .73; const auto wetWithMix = render(wetGraph, input, 2);
    graph.nodes[1].echo.mix = .37; const auto mixed = render(graph, input);
    bool independent = true, blend = true;
    for (std::size_t i = 0; i < input.size(); ++i) {
        independent &= wet[i].left == wetWithMix[i].left && wet[i].right == wetWithMix[i].right;
        blend &= std::abs(mixed[i].left - (.63 * dry[i].left + .37 * wet[i].left)) < 4e-8
            && std::abs(mixed[i].right - (.63 * dry[i].right + .37 * wet[i].right)) < 4e-8;
    }
    require(independent && wet[240].left == 0 && wet[5040].left > .2, "Echo output is repeat-only and sample-identical at zero/nonzero Mix");
    require(blend, "Audio retains the selected linear dry/echo mix on both channels");
    GraphChannels capture{}; capture[0] = 1;
    require(graphSignalChannels(compileGraph(wetGraph), capture)[2] == 1, "Echo-only routing preserves mono metadata instead of inheriting Reverb stereo behavior");
    capture[0] = 2; require(graphSignalChannels(compileGraph(wetGraph), capture)[2] == 2, "Echo-only routing preserves stereo metadata");
    require(Json(Json(wetGraph).get<AudioGraph>()) == Json(wetGraph), "Echo-only port identity survives native persistence");
    auto invalid = wetGraph; invalid.edges[0].toPort = "wet";
    rejects([&] { compileGraph(invalid); }, "Echo-only cannot be used as an input socket");
    for (bool mute : {false, true}) {
        wetGraph.nodes[1].muted = mute; wetGraph.nodes[1].bypass = !mute;
        const auto silenced = render(wetGraph, input, 2);
        require(std::all_of(silenced.begin(), silenced.end(), [](Frame f) { return f.left == 0 && f.right == 0; }),
            "Stable bypass or mute silences the independent Echo output");
    }

    GraphPlan::Node plan; plan.kind = GraphKind::echo; plan.echo = {100, 1, 1};
    AdvancedEffect echo(GraphKind::echo); echo.configure(plan);
    bool noReplay = true; double first{}, fresh{};
    for (std::uint32_t i = 0; i < sampleRate; ++i) {
        if (i == 9600) { plan.echo.timeMs = 400; echo.configure(plan); }
        const auto value = echo.process({i == 0 || i == 11000 ? 1.f : 0.f, 0}).effectWet;
        if (i == 4800) first = value.left;
        if (i == 30200) fresh = value.left;
        if (i != 4800 && i != 30200) noReplay &= value.left == 0;
    }
    require(first == 1 && fresh == 1 && noReplay,
        "Regression: Time100-to400 after the first repeat never resurrects the old input, while fresh input repeats at400ms");
    echo.reset(); plan.echo = {100, 8, .25}; echo.configure(plan);
    bool dryContinuous = true, quietAfterReset = true;
    for (int i = 0; i < 24000; ++i) {
        if (i == 6000) { plan.echo.repeats = 1; echo.configure(plan); }
        const Frame source{i == 0 ? .8f : 0, 0}; const auto result = echo.process(source);
        dryContinuous &= std::abs(result.wet.left - (.75 * source.left + .25 * result.effectWet.left)) < 4e-8;
        if (i >= 6480) quietAfterReset &= result.effectWet.left == 0;
    }
    require(dryContinuous && quietAfterReset, "Repeats edits clear the old finite tail without altering the immediate dry blend");
    echo.reset(); plan.echo = {100, 8, 1}; echo.configure(plan);
    bool latestOnly = true;
    for (int i = 0; i < 6000; ++i) {
        if (i == 120 || i == 240 || i == 360) { plan.echo = {i == 360 ? 20. : 2000., i == 360 ? 1 : 8, 1}; echo.configure(plan); }
        const auto value = echo.process({i == 0 || i == 1200 ? .4f : 0, 0}).effectWet;
        latestOnly &= i == 2160 ? std::abs(value.left - .4f) < 1e-7 : value.left == 0;
    }
    require(latestOnly, "Rapid Time/count edits finish one fade, apply the latest request and never revisit intermediate timing or old input");
    AdvancedEffect unchanged(GraphKind::echo); echo.reset(); plan.echo = {20.01, 8, 1}; echo.configure(plan); unchanged.configure(plan);
    bool mixIndependent = true;
    for (int i = 0; i < 24000; ++i) {
        if (i % 120 == 0) { plan.echo.mix = i % 360 == 0 ? 0 : i % 360 == 120 ? .5 : 1; echo.configure(plan); }
        const Frame source{static_cast<float>(.3 * sine(i, 997)), static_cast<float>(.2 * sine(i, 1901))};
        const auto a = echo.process(source).effectWet, b = unchanged.process(source).effectWet;
        mixIndependent &= a.left == b.left && a.right == b.right;
    }
    require(mixIndependent, "Continuous Mix edits neither clear nor rescale the independent finite Echo history");
}
void echoDestructiveAdoption() {
    for (int scenario = 0; scenario < 3; ++scenario) {
        GraphPlan::Node plan; plan.kind = GraphKind::echo; plan.echo = {20.01, 8, .3};
        AdvancedEffect old(GraphKind::echo), oldReference(GraphKind::echo), next(GraphKind::echo), nextReference(GraphKind::echo);
        old.configure(plan);
        const std::size_t warm = scenario == 0 ? 100 : scenario == 1 ? echoHistoryFrames + 1234 : 20000;
        for (std::size_t i = 0; i < warm; ++i) old.process({static_cast<float>(.4 * sine(i, 997)), static_cast<float>(.2 * sine(i, 1901))});
        plan.echo.mix = .9;
        if (scenario == 2) { plan.echo.timeMs = 100; plan.echo.repeats = 3; }
        old.configure(plan);
        for (int i = 0; i < 123; ++i) old.process({.1f, -.2f});
        oldReference.inherit(old); nextReference.inherit(old);
        const auto allocations = allocationOperations; trackAllocations = true;
        next.adopt(old, true);
        auto edited = plan; edited.echo.mix = .45; next.configure(edited); nextReference.configure(edited);
        bool sameOld = true, sameNew = true;
        for (int i = 0; i < 2400; ++i) {
            const Frame source{static_cast<float>(.2 * sine(i, 1103)), static_cast<float>(.1 * sine(i, 1733))};
            if (i < 480) {
                const auto a = old.process(source), b = oldReference.process(source);
                sameOld &= std::abs(a.wet.left - b.wet.left) < 1e-7 && std::abs(a.wet.right - b.wet.right) < 1e-7
                    && std::abs(a.effectWet.left - b.effectWet.left) < 1e-7 && std::abs(a.effectWet.right - b.effectWet.right) < 1e-7;
            }
            const auto a = next.process(source), b = nextReference.process(source);
            sameNew &= a.wet.left == b.wet.left && a.wet.right == b.wet.right && a.effectWet.left == b.effectWet.left && a.effectWet.right == b.effectWet.right;
        }
        trackAllocations = false;
        require(sameOld && sameNew, "Destructive Echo adoption preserves both480-frame fading predecessor and exact successor at startup, ring wrap and an in-flight restart");
        require(allocationOperations == allocations, "Echo history transfer and fading-cache playback perform no heap allocation or free");
    }
    for (bool bypass : {false, true}) {
        auto graph = chain("echo"); graph.nodes[1].echo = {20, 8, .4}; graph.nodes[1].bypass = bypass;
        const auto before = compileGraph(graph); graph.edges[1].fromPort = "wet"; graph.nodes[1].bypass = !bypass;
        const auto after = compileGraph(graph);
        auto old = std::make_unique<GraphProcessor>(), oldReference = std::make_unique<GraphProcessor>();
        auto next = std::make_unique<GraphProcessor>(), nextReference = std::make_unique<GraphProcessor>();
        old->prepare(before); oldReference->prepare(before); next->prepare(after); nextReference->prepare(after);
        std::array<Frame, quantumFrames> input{}; input.fill({.3f, -.2f}); GraphProcessor::Inputs inputs{}; inputs[0] = input;
        for (int i = 0; i < 50; ++i) old->process(before, inputs);
        oldReference->inherit(*old, before, before); nextReference->inherit(*old, before, after); next->adopt(*old, before, after, true);
        bool equal = true;
        for (int block = 0; block < 2; ++block) {
            old->process(before, inputs); oldReference->process(before, inputs); next->process(after, inputs); nextReference->process(after, inputs);
            for (std::size_t i = 0; i < quantumFrames; ++i) {
                equal &= old->output(2)[i].left == oldReference->output(2)[i].left && old->output(2)[i].right == oldReference->output(2)[i].right
                    && next->output(2)[i].left == nextReference->output(2)[i].left && next->output(2)[i].right == nextReference->output(2)[i].right;
            }
        }
        require(equal, "Audio-to-Echo rewiring during bypass/unbypass keeps both graph fade contributions identical to independent histories");
    }
}
void distortionSoundAndLatency() {
    auto graph = chain("distortion"); std::vector<Frame> impulse(sampleRate); impulse[240] = {.4f, -.2f};
    graph.nodes[1].distortion.mix = 0; graph.nodes[1].distortion.outputDb = -6;
    auto dry = render(graph, impulse); const double trim = std::pow(10., -6. / 20);
    bool dryMatches = true;
    for (std::size_t i = 0; i < dry.size(); ++i) {
        const Frame expected = i >= 32 ? impulse[i - 32] : Frame{};
        dryMatches &= std::abs(dry[i].left - expected.left * trim) <= 2e-8 && std::abs(dry[i].right - expected.right * trim) <= 2e-8;
    }
    require(dryMatches, "Mix zero retains exact 32-frame dry timing and the selected Output trim");
    graph.nodes[1].bypass = true; dry = render(graph, impulse);
    require(dry[272].left == impulse[240].left && dry[272].right == impulse[240].right
        && dry[240].left == 0, "Distortion Bypass uses the same delay and ignores the effect Output trim");
    graph.nodes[1].muted = true; const auto muted = render(graph, impulse);
    require(std::all_of(muted.begin(), muted.end(), [](Frame f) { return f.left == 0 && f.right == 0; }), "Distortion mute remains silent while bypassed");

    GraphPlan::Node plan; plan.kind = GraphKind::distortion; plan.distortion = {0, 18000, 1, 0}; AdvancedEffect effect(GraphKind::distortion); effect.configure(plan);
    std::vector<Frame> linear(240); for (std::size_t i = 0; i < linear.size(); ++i) linear[i] = effect.process(i == 80 ? Frame{.0001f, 0} : Frame{}).wet;
    const auto peak = std::max_element(linear.begin(), linear.end(), [](Frame a, Frame b) { return std::abs(a.left) < std::abs(b.left); });
    require(std::distance(linear.begin(), peak) == 112, "The low-level wet impulse peaks at the declared 32-frame oversampling delay");
    require(std::all_of(linear.begin(), linear.end(), [](Frame f) { return f.right == 0; }), "Distortion oversampling and filters create no stereo cross-talk");
    const auto input = signal(1000, .05); graph = chain("distortion"); graph.nodes[1].distortion = {0, 18000, 1, 0};
    const auto gentle = render(graph, input); graph.nodes[1].distortion.driveDb = 36; const auto strong = render(graph, input);
    const double gentleRatio = magnitude(gentle, 3000) / magnitude(gentle, 1000), strongRatio = magnitude(strong, 3000) / magnitude(strong, 1000);
    require(strongRatio > .1 && strongRatio > gentleRatio * 30, "Drive spans subtle saturation through clearly measurable odd harmonics");
    graph.nodes[1].distortion.driveDb = 12; graph.nodes[1].distortion.toneHz = 18000;
    const auto bright = render(graph, signal(6000, .1)); graph.nodes[1].distortion.toneHz = 500; const auto dark = render(graph, signal(6000, .1));
    require(rms(dark) < rms(bright) * .12, "Tone controls the wet sound's brightness over its whole range");
    graph.nodes[1].distortion = {36, 18000, 1, 0}; std::vector<Frame> dc(sampleRate * 3, {.2f, -.1f}); const auto blocked = render(graph, dc);
    double mean{}; for (std::size_t i = sampleRate * 2; i < blocked.size(); ++i) mean += blocked[i].left;
    expectNear(mean / sampleRate, 0, 1e-7, "Wet-path DC removal rejects a sustained offset after saturation");
    const auto normal = render(chain("distortion"), signal(1000, .1)); const double defaultDb = 20 * std::log10(rms(normal) / rms(signal(1000, .1)));
    require(defaultDb > -6 && defaultDb < 1, "Default Distortion does not unexpectedly amplify the reference speech-band tone");
    std::cout << "Distortion defaults: 1-kHz reference level change=" << defaultDb << " dB; strong third/fundamental=" << strongRatio << '\n';
    graph.nodes[1].distortion = {24, 8000, .65, -3}; auto stereo = signal(1300, .1);
    for (std::size_t i = 0; i < stereo.size(); ++i) stereo[i].right = static_cast<float>(.07 * sine(i, 2700));
    auto swapped = stereo; for (auto& frame : swapped) std::swap(frame.left, frame.right);
    const auto a = render(graph, stereo), b = render(graph, swapped);
    bool symmetric = true; for (std::size_t i = 0; i < a.size(); ++i) symmetric &= a[i].left == b[i].right && a[i].right == b[i].left;
    require(symmetric, "Every Distortion channel retains its own FIR, DC and tone histories");
}
void distortionAliasing() {
    // Coherent one-second DFT windows. These bins cannot be wanted harmonics
    // below Nyquist for the selected inputs; they are folded products of the
    // base-rate nonlinearity. Compare normalized alias energy to an otherwise
    // identical naive tanh followed by the same output tone/DC filters.
    for (double frequency : {7000., 9000., 11000., 18000., 19000.}) for (double driveDb : {18., 36.}) {
        auto graph = chain("distortion"); graph.nodes[1].distortion = {driveDb, 18000, 1, 0}; const auto input = signal(frequency, frequency == 18000 ? .5 : .2);
        const auto oversampled = render(graph, input); auto naive = input;
        const double boost = std::pow(10., driveDb / 20), g = std::tan(std::numbers::pi * 18000 / sampleRate), pole = .9973854300793629;
        double previous{}, highHistory{}, tone{};
        for (auto& frame : naive) {
            const double saturated = std::tanh(frame.left * boost);
            const double high = (1 + pole) * .5 * (saturated - previous) + pole * highHistory;
            previous = saturated; highHistory = high; const double v = (high - tone) * g / (1 + g); const double low = v + tone; tone = low + v;
            frame.left = static_cast<float>(low);
        }
        double actualEnergy{}, naiveEnergy{};
        for (int harmonic : {3, 5, 7, 9}) {
            if (harmonic * frequency < sampleRate / 2) continue;
            const double wrapped = std::fmod(harmonic * frequency, static_cast<double>(sampleRate));
            const double alias = std::min(wrapped, sampleRate - wrapped);
            if (alias == frequency || alias == 0) continue;
            actualEnergy += std::pow(magnitude(oversampled, alias), 2); naiveEnergy += std::pow(magnitude(naive, alias), 2);
        }
        const double relative = std::sqrt(actualEnergy / naiveEnergy) * magnitude(naive, frequency) / magnitude(oversampled, frequency);
        std::cout << "Alias comparison: " << frequency << " Hz, Drive " << driveDb << " dB, normalized folded-energy change=" << 20 * std::log10(relative) << " dB\n";
        require(naiveEnergy > 1e-6 && std::isfinite(relative) && relative < .25,
            "Fixed oversampling reduces measured folded products by at least 12 dB, including strong drive near Nyquist");
    }
}
void distortionAutomationAndPublication() {
    GraphPlan::Node nodePlan; nodePlan.kind = GraphKind::distortion;
    nodePlan.distortion = {24, 5000, 0, 0};
    AdvancedEffect changing(GraphKind::distortion), dry(GraphKind::distortion), wet(GraphKind::distortion);
    changing.configure(nodePlan); dry.configure(nodePlan);
    auto wetPlan = nodePlan; wetPlan.distortion.mix = 1; wet.configure(wetPlan);
    const auto source = [](std::size_t i) {
        return Frame{static_cast<float>(.2 * sine(i, 997) + .03 * sine(i, 7111)),
            static_cast<float>(.13 * sine(i, 1709))};
    };
    for (std::size_t i = 0; i < 2400; ++i) { const auto input = source(i); changing.process(input); dry.process(input); wet.process(input); }
    nodePlan.distortion.mix = 1; changing.configure(nodePlan);
    bool blend = true;
    for (std::size_t i = 0; i < 960; ++i) {
        const auto input = source(2400 + i), a = changing.process(input).wet, d = dry.process(input).wet, w = wet.process(input).wet;
        const double amount = std::min(1., (i + 1.) / 480);
        blend &= std::abs(a.left - (d.left * (1 - amount) + w.left * amount)) < 2e-7
            && std::abs(a.right - (d.right * (1 - amount) + w.right * amount)) < 2e-7;
    }
    require(blend, "Mix traverses the complete 10-ms original/distorted blend and settles exactly, retaining continuous filter history");
    nodePlan.distortion.outputDb = -24; changing.configure(nodePlan);
    bool trim = true;
    const double endGain = std::pow(10., -24. / 20);
    for (std::size_t i = 0; i < 960; ++i) {
        const auto input = source(3360 + i), a = changing.process(input).wet, w = wet.process(input).wet;
        const double gain = 1 + (endGain - 1) * std::min(1., (i + 1.) / 480);
        trim &= std::abs(a.left - w.left * gain) < 2e-7 && std::abs(a.right - w.right * gain) < 2e-7;
    }
    require(trim, "Output follows its full 10-ms level ramp without a discontinuous step or disturbance of the distorted signal");

    auto graph = chain("distortion"); auto previous = compileGraph(graph);
    auto uninterrupted = std::make_unique<GraphProcessor>(), published = std::make_unique<GraphProcessor>();
    uninterrupted->prepare(previous); published->prepare(previous);
    GraphProcessor::Inputs inputs{}; std::array<Frame, quantumFrames> block{}; inputs[0] = block;
    bool same = true, noAllocation = true;
    // Compare ordinary in-place parameter processing against the real graph
    // publication path, throughout every ramp rather than only its first sample.
    for (int tick = 0; tick < 160; ++tick) {
        for (std::size_t frame = 0; frame < block.size(); ++frame) block[frame] = source(tick * quantumFrames + frame);
        if (tick % 3 == 0) {
            graph.nodes[1].distortion = {tick % 6 ? 36. : 0., tick % 9 ? 500. : 18000., tick % 12 ? 1. : 0., tick % 15 ? -24. : 0.};
            graph.nodes[1].bypass = tick % 21 == 0; graph.nodes[1].muted = tick % 27 == 0;
        }
        const auto nextPlan = compileGraph(graph); auto next = std::make_unique<GraphProcessor>(); next->prepare(nextPlan);
        const auto before = allocationOperations; trackAllocations = true;
        next->adopt(*published, previous, nextPlan, false);
        uninterrupted->process(nextPlan, inputs); next->process(nextPlan, inputs);
        trackAllocations = false; noAllocation &= before == allocationOperations;
        for (std::size_t i = 0; i < quantumFrames; ++i) {
            const auto a = uninterrupted->output(1)[i], b = next->output(1)[i];
            same &= a.left == b.left && a.right == b.right;
        }
        published.swap(next); previous = nextPlan;
    }
    require(same, "Rapid Drive/Tone/Mix/Output, bypass and mute publication preserves every sample of the uninterrupted reference, including mid-ramp retargets");
    require(noAllocation, "Distortion publication and complete automation ramps perform no C++ allocations or frees on the processing path");

    bool quiet = true, finite = true; double maximum{};
    nodePlan.distortion = {36, 18000, 1, 0}; changing.reset(); changing.configure(nodePlan);
    // Strongly excite every history, then edit all controls during silence.
    // Once the short filter tail has decayed, an edit cannot resurrect speech.
    for (std::size_t i = 0; i < sampleRate * 2; ++i) {
        if (i >= sampleRate && i % 137 == 0) {
            nodePlan.distortion = {i % 274 ? 36. : 0., i % 411 ? 500. : 18000., i % 548 ? 1. : 0., i % 685 ? -24. : 0.};
            changing.configure(nodePlan);
        }
        const auto value = changing.process(i < sampleRate / 4 ? source(i) : Frame{}).wet;
        finite &= std::isfinite(value.left) && std::isfinite(value.right);
        maximum = std::max({maximum, std::abs(static_cast<double>(value.left)), std::abs(static_cast<double>(value.right))});
        if (i >= sampleRate) quiet &= std::abs(value.left) < 1e-7 && std::abs(value.right) < 1e-7;
    }
    require(finite && quiet, "Distortion controls cannot replay old audio after its short filter history has decayed");
    std::cout << "Distortion full-transition tests: Mix/Output ramps and rapid graph publication passed; excited wet peak=" << maximum << '\n';
}
void stateContinuityAndRealtime() {
    for (const char* kind : {"echo", "distortion"}) {
        auto graph = chain(kind); graph.nodes[1].echo = {20, 3, .7};
        const auto plan = compileGraph(graph); auto first = std::make_unique<GraphProcessor>(), inherited = std::make_unique<GraphProcessor>(); first->prepare(plan); inherited->prepare(plan);
        std::array<Frame, quantumFrames> input{}; GraphProcessor::Inputs inputs{}; inputs[0] = input;
        for (int block = 0; block < 24; ++block) {
            for (std::size_t i = 0; i < input.size(); ++i) input[i] = {static_cast<float>(.1 * sine(block * quantumFrames + i, 997)), static_cast<float>(.07 * sine(block * quantumFrames + i, 2113))};
            first->process(plan, inputs);
        }
        const auto allocations = allocationOperations; trackAllocations = true;
        inherited->inherit(*first, plan, plan); first->process(plan, inputs); inherited->process(plan, inputs); trackAllocations = false;
        require(std::equal(first->output(2).begin(), first->output(2).end(), inherited->output(2).begin(),
            [](Frame a, Frame b) { return a.left == b.left && a.right == b.right; }), "Creative histories and smoothing survive unchanged plan publication bit-exactly");
        require(allocationOperations == allocations, "Creative processing and state adoption perform no C++ heap operations");
        graph.nodes[1].echo = {2000, 8, 1}; graph.nodes[1].distortion = {36, 500, 1, -24}; const auto edited = compileGraph(graph);
        auto changed = std::make_unique<GraphProcessor>(); changed->prepare(edited); changed->inherit(*first, plan, edited); changed->process(edited, inputs);
        first->process(plan, inputs);
        require(std::abs(changed->output(2).front().left - first->output(2).front().left) < .01, "Creative parameter edits begin close to the unedited continuation of inherited state");
        const auto beforeReset = allocationOperations; trackAllocations = true; changed->reset(); changed->process(edited, {}); trackAllocations = false;
        require(allocationOperations == beforeReset && std::all_of(changed->output(2).begin(), changed->output(2).end(), [](Frame f) { return f.left == 0 && f.right == 0; }),
            "Creative reset clears delay/filter state without allocations or a stale replay");
        graph = chain(kind); graph.nodes[1].bypass = true; graph.nodes[1].echo.timeMs = 20; const auto bypass = compileGraph(graph);
        auto warm = std::make_unique<GraphProcessor>(); warm->prepare(bypass); warm->process(bypass, {}); warm->process(bypass, {});
        input.fill({}); input[0] = {.2f, 0}; warm->process(bypass, inputs);
        input.fill({}); for (int block = 0; block < 2; ++block) warm->process(bypass, inputs);
        graph.nodes[1].bypass = false; const auto enabled = compileGraph(graph); auto resume = std::make_unique<GraphProcessor>(); resume->prepare(enabled); resume->inherit(*warm, bypass, enabled);
        double tail{}; for (int block = 0; block < 8; ++block) { resume->process(enabled, inputs); for (auto f : resume->output(2)) tail = std::max(tail, std::abs(static_cast<double>(f.left))); }
        require(kind != std::string("echo") || tail > .001, "Echo continues its live history while bypassed and resumes the current tail");
    }
    GraphPlan::Node plan; plan.kind = GraphKind::distortion; plan.distortion = {36, 18000, 1, 0};
    AdvancedEffect effect(GraphKind::distortion), unchanged(GraphKind::distortion); effect.configure(plan);
    bool finite = true; double maximumEditStep{};
    for (std::size_t i = 0; i < sampleRate; ++i) {
        const float source = static_cast<float>(.1 * sine(i, 400)); double baseline{};
        if (i % 120 == 0) {
            unchanged.inherit(effect); baseline = unchanged.process({source, -source}).wet.left;
            plan.distortion = i % 240 ? DistortionSettings{36, 500, 1, -24} : DistortionSettings{0, 18000, 0, 0}; effect.configure(plan);
        }
        const auto value = effect.process({source, -source}).wet;
        finite &= std::isfinite(value.left) && std::isfinite(value.right);
        if (i % 120 == 0) maximumEditStep = std::max(maximumEditStep, std::abs(value.left - baseline));
    }
    require(finite && maximumEditStep < .01, "Rapid Distortion edits stay finite and start within 0.01 of the same unedited signal continuation");
    const auto nan = std::numeric_limits<float>::quiet_NaN(); const auto infinity = std::numeric_limits<float>::infinity();
    for (int i = 0; i < 200; ++i) { const auto value = effect.process({nan, infinity}).wet; require(std::isfinite(value.left) && std::isfinite(value.right), "Nonfinite creative input is sanitized before entering persistent filters"); }
    effect.reset(); plan.distortion = {36, 18000, 1, 0}; effect.configure(plan);
    bool tinySilent = true; const float tiny = std::numeric_limits<float>::denorm_min() * 16;
    for (int i = 0; i < 1000; ++i) { const auto value = effect.process({tiny, -tiny}).wet; tinySilent &= value.left == 0 && value.right == 0; }
    require(tinySilent, "Wet Distortion flushes subnormal excitation before storing oversampling history");
}
void maximumGraphTiming(std::string_view only = {}) {
    for (const char* kind : {"echo", "distortion"}) for (int effectCount : {4, 30}) for (bool preserveFade : {false, true}) {
        if (!only.empty() && only != kind) continue;
        AudioGraph graph; graph.nodes.push_back(node("input", "virtualInput"));
        for (int i = 0; i < effectCount; ++i) {
            const auto id = "effect-" + std::to_string(i); auto effect = node(id, kind); effect.distortion = {36, 18000, 1, 0}; effect.echo = {2000, 8, 1};
            graph.nodes.push_back(effect); graph.edges.push_back({"in-" + id, "input", id}); graph.edges.push_back({"out-" + id, id, "output"});
        }
        graph.nodes.push_back(node("output", "output")); const auto plan = compileGraph(graph);
        auto first = std::make_unique<GraphProcessor>(), second = std::make_unique<GraphProcessor>();
        const auto preparing = std::chrono::steady_clock::now(); first->prepare(plan); second->prepare(plan);
        const auto prepareUs = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - preparing).count();
        std::array<Frame, quantumFrames> input{}; for (std::size_t i = 0; i < input.size(); ++i) input[i] = {static_cast<float>(.2 * sine(i, 7000)), static_cast<float>(.2 * sine(i, 9000))};
        GraphProcessor::Inputs inputs{}; inputs[0] = input; std::vector<double> processTimes, inheritTimes, combinedTimes, resetTimes;
        // Full16-second history makes all eight maximum-delay taps read real
        // data, including across wrap. Empty-buffer branching is not a valid
        // worst-case cache/ownership-transfer benchmark.
        if (std::string_view(kind) == "echo")
            for (std::size_t block = 0; block < echoHistoryFrames / quantumFrames + 4; ++block) first->process(plan, inputs);
        std::vector<ULONG64> processCycles, combinedCycles; processCycles.reserve(160); combinedCycles.reserve(160);
        processTimes.reserve(160); inheritTimes.reserve(160); combinedTimes.reserve(160); resetTimes.reserve(16);
        TimingSchedule schedule;
        for (int block = 0; block < 176; ++block) {
            ULONG64 beforeCycles{}, afterCycles{}, afterCombinedCycles{};
            QueryThreadCycleTime(GetCurrentThread(), &beforeCycles);
            const auto start = std::chrono::steady_clock::now(); first->process(plan, inputs);
            const auto processed = std::chrono::steady_clock::now();
            QueryThreadCycleTime(GetCurrentThread(), &afterCycles);
            second->adopt(*first, plan, plan, preserveFade);
            if (preserveFade) first->process(plan, inputs); // Old graph's first fading quantum is additional real engine work.
            const auto inherited = std::chrono::steady_clock::now();
            QueryThreadCycleTime(GetCurrentThread(), &afterCombinedCycles);
            first.swap(second);
            if (block >= 16) { processTimes.push_back(std::chrono::duration<double, std::micro>(processed - start).count()); inheritTimes.push_back(std::chrono::duration<double, std::micro>(inherited - processed).count()); combinedTimes.push_back(std::chrono::duration<double, std::micro>(inherited - start).count()); processCycles.push_back(afterCycles - beforeCycles); combinedCycles.push_back(afterCombinedCycles - beforeCycles); }
            if (block >= 16 && block % 10 == 0) { const auto before = std::chrono::steady_clock::now(); second->reset(); resetTimes.push_back(std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - before).count()); }
            schedule.pace();
        }
        schedule.restore();
        std::sort(processTimes.begin(), processTimes.end()); std::sort(inheritTimes.begin(), inheritTimes.end()); std::sort(resetTimes.begin(), resetTimes.end());
        std::sort(processCycles.begin(), processCycles.end()); std::sort(combinedCycles.begin(), combinedCycles.end()); std::sort(combinedTimes.begin(), combinedTimes.end());
        const auto process99 = processTimes[158], inherit99 = inheritTimes[158];
        std::cout << effectCount << " parallel " << kind << (preserveFade ? " (routing fade)" : " (parameter handoff)")
            << ": process median=" << processTimes[80] << " us, p95=" << processTimes[152]
            << " us, p99=" << process99 << " us; adopt" << (preserveFade ? "+old quantum" : "") << " median=" << inheritTimes[80] << " us, p95=" << inheritTimes[152]
            << " us, p99=" << inherit99 << " us; measured combined median=" << combinedTimes[80] << " us, p99=" << combinedTimes[158]
            << " us; reset max=" << resetTimes.back() << " us; process thread cycles median="
            << processCycles[80] << ", p99=" << processCycles[158] << "; combined thread cycles median=" << combinedCycles[80] << ", p99=" << combinedCycles[158]
            << "; scheduling=Pro Audio MMCSS with production 5-ms MixClock; two-runtime prepare=" << prepareUs
            << " us; Echo history bytes/runtime=" << GraphProcessor::echoHistoryBytes(plan) << '\n';
        require(combinedTimes[158] < 5000, "Representative and maximum-size creative graphs including old/new routing fades fit the measured combined synthetic 5-ms budget at p99");
        require(resetTimes.back() < 5000, "Creative graph reset fits one synthetic quantum without large stack temporaries");
    }
}
}
int main(int argc, char** argv) {
    try {
        if (argc == 2 && std::string_view(argv[1]) == "--timing") maximumGraphTiming();
        else if (argc == 2 && std::string_view(argv[1]) == "--distortion") { distortionSoundAndLatency(); distortionAliasing(); distortionAutomationAndPublication(); maximumGraphTiming("distortion"); }
        else { persistenceAndValidation(); echoImpulseAndBounds(); echoOutputsAndEdits(); echoDestructiveAdoption(); distortionSoundAndLatency(); distortionAliasing(); distortionAutomationAndPublication(); stateContinuityAndRealtime(); maximumGraphTiming(); }
        std::cout << "PASS: " << checks << " creative-effect checks; synthetic audio only, no devices, engine, live settings or personal workspace.\n"; return 0;
    } catch (const std::exception& error) { trackAllocations = false; std::cerr << "FAIL after " << checks << " checks: " << error.what() << '\n'; return 1; }
}
