// Synthetic audio only: no WASAPI, production pipe, user files or devices.
#include "am/advanced_effects.hpp"
#include "am/configuration.hpp"
#include <algorithm>
#include <cmath>
#include <chrono>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <new>
#include <numbers>
#include <random>
#include <vector>

static bool tracking{};
static std::uint64_t allocations{};
void* operator new(std::size_t size) { if (tracking) ++allocations; if (auto* p = std::malloc(size ? size : 1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t size) { return ::operator new(size); }
void operator delete(void* p) noexcept { if (tracking) ++allocations; std::free(p); }
void operator delete[](void* p) noexcept { ::operator delete(p); }

namespace {
using namespace am;
int checks{};
void check(bool result, const std::string& message) { ++checks; if (!result) throw std::runtime_error(message); }
void near(double actual, double expected, double tolerance, const std::string& message) {
    check(std::isfinite(actual) && std::abs(actual - expected) <= tolerance,
        message + " actual=" + std::to_string(actual) + " expected=" + std::to_string(expected));
}
template<class F> void rejects(F action, const char* message) {
    bool rejected{}; try { action(); } catch (const std::exception&) { rejected = true; } check(rejected, message);
}
AudioGraph chain(double tone = 0, double ambience = 0) {
    AudioGraph graph; GraphNode source; source.id = "input"; source.kind = "virtualInput";
    GraphNode effect; effect.id = "color"; effect.kind = "voiceColor"; effect.voiceColor = {tone, ambience};
    graph.nodes = {source, effect}; graph.edges = {{"signal", "input", "color"}}; return graph;
}
double db(double ratio) { return 20 * std::log10(std::max(ratio, 1e-20)); }
double energy(const std::vector<Frame>& samples, std::size_t begin = 0) {
    double result{}; for (std::size_t i = begin; i < samples.size(); ++i) result += samples[i].left * samples[i].left; return result;
}
std::vector<Frame> render(const AudioGraph& graph, const std::vector<Frame>& input) {
    auto plan = std::make_unique<GraphPlan>(compileGraph(graph)); auto processor = std::make_unique<GraphProcessor>(); processor->prepare(*plan);
    std::array<Frame, quantumFrames> buffer{}; GraphProcessor::Inputs views{}; views[0] = buffer;
    for (int i = 0; i < 10; ++i) processor->process(*plan, views);
    std::vector<Frame> result(input.size());
    for (std::size_t offset = 0; offset < input.size(); offset += quantumFrames) {
        buffer = {}; const auto count = std::min(quantumFrames, input.size() - offset);
        std::copy_n(input.begin() + offset, count, buffer.begin()); processor->process(*plan, views);
        std::copy_n(processor->output(1).begin(), count, result.begin() + offset);
    }
    return result;
}
double error(const std::vector<Frame>& actual, const std::vector<Frame>& expected) {
    double result{}; for (std::size_t i = 0; i < actual.size(); ++i) {
        result = std::max({result, std::abs(static_cast<double>(actual[i].left - expected[i].left)),
            std::abs(static_cast<double>(actual[i].right - expected[i].right))});
    } return result;
}
AudioGraph eqReference(double tone, double character = 0) {
    auto graph = chain(tone, character); auto& node = graph.nodes[1]; node.kind = "eq";
    character = std::pow(character, 1.3);
    node.eqBands = {{"lowShelf", true, 140, 12*character-12*tone, .7071067811865476},
        {"peak", true, 450, -8*character, .9}, {"peak", true, 3000, 12*character+3*tone, .85},
        {"highShelf", true, 7000, 4*character+9*tone, .7071067811865476}};
    node.gainDb = static_cast<float>(-6*std::max(-tone, 0.)); return graph;
}
double response(double tone, double frequency, double character = 0) {
    std::vector<Frame> input(sampleRate);
    for (std::size_t i = 0; i < input.size(); ++i) { const auto x = static_cast<float>(.003 * std::sin(2 * std::numbers::pi * frequency * i / sampleRate)); input[i] = {x, -.35f * x}; }
    const auto output = render(chain(tone, character), input); return db(std::sqrt(energy(output, sampleRate / 2) / energy(input, sampleRate / 2)));
}
void neutralAndTone() {
    std::mt19937 random(50128); std::uniform_real_distribution<float> noise(-.1f, .1f);
    std::vector<Frame> input(sampleRate); for (auto& sample : input) sample = {noise(random), noise(random)};
    near(error(render(chain(), input), input), 0, 0, "Neutral is bit-exact, undelayed and stereo-independent");
    for (double t : {-1., -.5, 0., .5, 1.}) {
        const auto low = response(t, 60), mid = response(t, 2500), high = response(t, 12000);
        std::cout << "Natural tone=" << t << ": 60Hz=" << low << "dB; 2.5kHz=" << mid << "dB; 12kHz=" << high << "dB\n";
        if (t < 0) check(low > 5 * -t && low - high > 18 * -t, "Warm produces at least 18 dB body-to-treble contrast at its extreme");
        if (t > 0) check(low < -10 * t && high - low > 18 * t, "Bright produces at least 18 dB treble-to-body contrast at its extreme");
        near(error(render(chain(t), input), render(eqReference(t), input)), 0, 3e-8, "Natural edge is only the mapped EQ and trim, without compression or doubling");
        for (double f : {20., 60., 160., 500., 2500., 6000., 12000., 20000.})
            check(response(t, f) <= 9.1, "Natural-edge steady response remains within the intentional nine-decibel maximum boost");
    }
    const auto plan = compileGraph(chain()); GraphChannels channels{}; channels[0] = 1;
    check(graphSignalChannels(plan, channels)[1] == 1, "Neutral preserves mono metadata");
    channels[0] = 2; check(graphSignalChannels(plan, channels)[1] == 2, "Neutral preserves stereo metadata");
    auto ambient = compileGraph(chain(0, 1)); channels[0] = 1;
    check(graphSignalChannels(ambient, channels)[1] == 2, "Active Character reports stereo thickening");
    ambient.nodes[1].bypass = true; check(graphSignalChannels(ambient, channels)[1] == 1, "Bypass restores source channel metadata");
}
void characterAndThickening() {
    std::vector<Frame> pulse(sampleRate * 2); pulse[0] = {.1f, .1f};
    const auto dry = render(chain(), pulse); near(error(dry, pulse), 0, 0, "Natural neutral impulse has no delayed tail");
    for (double amount : {.25, .5, 1.}) {
        const auto graph = chain(0, amount); const auto plan = compileGraph(graph);
        const double effective = std::pow(amount, 1.3);
        near(plan.nodes[1].reverb.mix, .02 * effective, 1e-15, "Character keeps room mix at no more than two percent");
        near(plan.nodes[1].reverb.decaySeconds, .3, 0, "Short room decay stays fixed while dragging vertically");
        const auto output = render(graph, pulse);
        // EQ itself has an impulse tail: find the deliberately added short
        // duplicate at the known delay, rather than mistaking filter ringing
        // for the start of an echo.
        check(std::abs(output[576].left) > .0004 * amount && std::abs(output[912].right) > .0004 * amount,
            "Both 12 ms and 19 ms copies add measurable low-level thickness");
        const double tail = energy(output, 1599);
        check(tail > 0 && tail < .00005, "Later room energy remains small relative to the direct impulse");
        check(energy(output, sampleRate) < 1e-12, "Short character tail decays before one second");
        const auto body = response(0, 80, amount), presence = response(0, 3000, amount), middle = response(0, 450, amount);
        check(body - middle > 16 * effective && presence - middle > 16 * effective,
            "Body and presence each exceed low-mid response by at least 16 dB at maximum, independent of overall loudness");
        std::cout << "Character=" << amount << "; 80Hz=" << body << "dB; 450Hz=" << middle << "dB; 3kHz=" << presence << "dB; late energy=" << tail << '\n';
    }
    for (double t : {-1., 0., 1.}) for (double amount : {0., 1.}) {
        const auto out = render(chain(t, amount), pulse);
        bool finite = true; for (auto s : out) finite = finite && std::isfinite(s.left) && std::isfinite(s.right) && std::abs(s.left) < .5 && std::abs(s.right) < .5;
        check(finite, "All pad corners produce a finite impulse response with at least six decibels of headroom");
    }
}
double peak(const std::vector<Frame>& samples) {
    double result{}; for (const auto x : samples) result = std::max({result, std::abs(static_cast<double>(x.left)), std::abs(static_cast<double>(x.right))});
    return result;
}
void compressionAndHeadroom() {
    // Speech-like harmonic bursts at two delivery levels; 50 ms syllable
    // ramps avoid conflating a compressor's intentional onset with a click.
    constexpr std::size_t syllable = sampleRate / 2, ramp = sampleRate / 20;
    std::vector<Frame> speech(syllable * 12);
    for (std::size_t i = 0; i < speech.size(); ++i) {
        const auto segment = i / syllable, position = i % syllable;
        const double level = segment == 3 || segment == 9 ? .5 : .075;
        const double edge = std::min({1., static_cast<double>(position) / ramp, static_cast<double>(syllable - 1 - position) / ramp});
        const double phase = 2 * std::numbers::pi * 125 * i / sampleRate;
        const auto x = static_cast<float>(level * edge * (.6 * std::sin(phase) + .25 * std::sin(phase * 2) + .15 * std::sin(phase * 19)));
        speech[i] = {x, -.37f * x};
    }
    const auto baseline = render(eqReference(0, 1), speech), processed = render(chain(0, 1), speech);
    const double normalize = std::sqrt(energy(processed) / energy(baseline));
    const double crestReduction = db(peak(baseline) * normalize / peak(processed));
    const auto levelContrast = [&](const std::vector<Frame>& samples) {
        double quiet{}, loud{};
        for (std::size_t i = sampleRate / 4; i < sampleRate * 2 / 5; ++i) {
            quiet += samples[i].left * samples[i].left;
            loud += samples[i + 3 * syllable].left * samples[i + 3 * syllable].left;
        }
        return db(std::sqrt(loud / quiet));
    };
    const auto before = levelContrast(baseline), after = levelContrast(processed);
    std::cout << "Speech dynamics: level contrast " << before << " -> " << after
        << "dB; RMS-matched crest reduction=" << crestReduction << "dB; output peak=" << peak(processed) << '\n';
    check(before - after > 1.5, "Parallel Broadcast compression reduces syllable contrast while retaining direct voice body");
    check(crestReduction > .8, "Stronger Broadcast density reduces crest factor against the RMS-matched EQ-only baseline");
    check(peak(processed) < .7, "Representative speech retains peak headroom without a hard limiter");
    for (double tone : {-1., 0., 1.}) for (double amount : {0., .5, 1.}) {
        const auto output = render(chain(tone, amount), speech);
        check(peak(output) < 1, "Representative speech stays below full scale across the pad");
        check(std::all_of(output.begin(), output.end(), [](Frame x) { return std::isfinite(x.left) && std::isfinite(x.right); }),
            "Stereo and anti-phase speech remain finite across the pad");
    }
    // Compare mono summing with this same processed stereo output, so the
    // deliberately stronger density/tonal attenuation cannot be mistaken for
    // cancellation caused by the double or room.
    for (double frequency : {80., 160., 650., 2500., 6000.}) {
        std::vector<Frame> tone(sampleRate);
        for (std::size_t i = 0; i < tone.size(); ++i) { const auto x = static_cast<float>(.002 * std::sin(2 * std::numbers::pi * frequency * i / sampleRate)); tone[i] = {x, x}; }
        const auto output = render(chain(0, 1), tone);
        double mono{}, stereo{};
        for (std::size_t i = sampleRate / 2; i < tone.size(); ++i) {
            const auto sum = (output[i].left + output[i].right) * .5; mono += sum * sum;
            stereo += (output[i].left * output[i].left + output[i].right * output[i].right) * .5;
        }
        const auto difference = db(std::sqrt(mono / stereo));
        check(difference > -.2 && difference < .0001, "Folding the processed voice to mono loses less than 0.2 dB compared with processed stereo energy");
    }
    // Isolate dynamics from the explicitly stereo room. At steady state the
    // same gain must be applied to unequal channels, regardless of which is loud.
    for (bool swap : {false, true}) {
        auto plan = compileGraph(chain(0, 1)); plan.nodes[1].reverb.mix = 0;
        auto effect = std::make_unique<AdvancedEffect>(GraphKind::voiceColor); effect->configure(plan.nodes[1]);
        const Frame input = swap ? Frame{.03f, .3f} : Frame{.3f, .03f}; Frame output;
        for (std::uint32_t i = 0; i < sampleRate; ++i) output = effect->process(input).wet;
        near(output.left / output.right, input.left / input.right, 1e-5, "Compression is linked and preserves unequal stereo balance");
    }
    // Several low-level threshold crossings exercise the soft knee. No
    // broadband jumps, discontinuity, or persistent gain pumping at one level.
    auto plan = compileGraph(chain(0, 1)); plan.nodes[1].reverb.mix = 0;
    auto effect = std::make_unique<AdvancedEffect>(GraphKind::voiceColor); effect->configure(plan.nodes[1]);
    double jump{}, last{}, settledVariation{};
    for (std::uint32_t i = 0; i < sampleRate * 4; ++i) {
        const double level = .055 + .022 * std::sin(2 * std::numbers::pi * i / sampleRate);
        const auto x = effect->process({static_cast<float>(level), static_cast<float>(-level)}).wet.left;
        if (i > sampleRate) jump = std::max(jump, std::abs(x - last)); last = x;
    }
    double minimum = 1, maximum = -1;
    for (std::uint32_t i = 0; i < sampleRate * 2; ++i) {
        const auto x = effect->process({.065f, -.065f}).wet.left;
        if (i > sampleRate) { minimum = std::min(minimum, static_cast<double>(x)); maximum = std::max(maximum, static_cast<double>(x)); }
    }
    settledVariation = maximum - minimum;
    check(jump < .00005 && settledVariation < 1e-6, "Slow knee crossings and steady delivery avoid abrupt gain steps or continuing pumping");
    std::cout << "Knee crossing max sample jump=" << jump << "; settled gain variation=" << settledVariation << '\n';
}
void levelRelativeDensity() {
    // Isolate Character's dynamics from tonal contrast. Equal-amplitude
    // harmonic analysis at speech and very quiet levels catches a fixed
    // threshold accidentally making the density stage disappear.
    const auto harmonic = [](const std::vector<Frame>& signal, double hz) {
        double real{}, imaginary{};
        for (std::size_t i = sampleRate; i < signal.size(); ++i) {
            const double phase = 2 * std::numbers::pi * hz * i / sampleRate;
            real += signal[i].left * std::cos(phase); imaginary += signal[i].left * std::sin(phase);
        }
        return std::hypot(real, imaginary) * 2 / (signal.size() - sampleRate);
    };
    double quietThird{}, normalThird{};
    for (double level : {.00003, .003, .03}) {
        auto plan = compileGraph(chain(0, 1)); plan.nodes[1].eq = {};
        plan.nodes[1].voiceColorGain = 1; plan.nodes[1].reverb.mix = 0;
        auto effect = std::make_unique<AdvancedEffect>(GraphKind::voiceColor); effect->configure(plan.nodes[1]);
        std::vector<Frame> output(sampleRate * 2);
        for (std::size_t i = 0; i < output.size(); ++i) {
            const float sample = static_cast<float>(level * std::sin(2 * std::numbers::pi * 300 * i / sampleRate));
            output[i] = effect->process({sample, -.37f * sample}).wet;
        }
        const double third = harmonic(output, 900) / harmonic(output, 300);
        std::cout << "Density input peak=" << db(level) << "dBFS; third harmonic=" << db(third) << "dBc\n";
        check(peak(output) <= level * 1.585, "Relative density never adds gain beyond the existing fixed compressor makeup");
        if (level < .0001) check(third < .0001, "Noise-floor normalization fades out instead of turning tiny signals into distortion");
        else {
            check(third > .04 && third < .12, "Quiet and normal speech levels both receive stronger controlled nonlinear colour, with third harmonic between four and twelve percent");
            if (level == .003) quietThird = third; else normalThird = third;
        }
    }
    check(std::abs(db(quietThird / normalThird)) < 1,
        "Density's harmonic signature stays within 1 dB when input level changes by 20 dB");
    auto plan = compileGraph(chain(0, 1)); auto effect = std::make_unique<AdvancedEffect>(GraphKind::voiceColor); effect->configure(plan.nodes[1]);
    bool silent = true;
    for (unsigned i = 0; i < sampleRate; ++i) { const auto output = effect->process({}).wet; silent &= output.left == 0 && output.right == 0; }
    check(silent, "Maximum Character cannot create sound from silence");
}
void stateAndUpdates() {
    auto graph = chain(-.7, .8); auto plan = std::make_unique<GraphPlan>(compileGraph(graph));
    auto a = std::make_unique<GraphProcessor>(), b = std::make_unique<GraphProcessor>(); a->prepare(*plan); b->prepare(*plan);
    std::array<Frame, quantumFrames> input{}; GraphProcessor::Inputs views{}; views[0] = input;
    input.fill({.1f, -.037f}); for (int block = 0; block < 80; ++block) a->process(*plan, views);
    bool identical = true, finite = true; double largestChange{}, largestOutput{}; float previous = a->output(1).back().left;
    const auto before = allocations;
    tracking = true; b->inherit(*a, *plan, *plan); tracking = false;
    for (int block = 0; block < 160; ++block) {
        // Changes every 5ms are faster than both the UI publish cadence and
        // smoothing time. A surviving DSP and an inherited replacement must
        // match exactly, even with an in-flight previous transition.
        graph.nodes[1].voiceColor = {block % 2 ? -1. : 1., block % 3 ? .0 : 1.};
        auto next = std::make_unique<GraphPlan>(compileGraph(graph));
        tracking = true;
        b->inherit(*a, *plan, *next);
        a->process(*next, views); b->process(*next, views);
        for (std::size_t i = 0; i < quantumFrames; ++i) {
            const auto x = a->output(1)[i], y = b->output(1)[i];
            identical = identical && x.left == y.left && x.right == y.right;
            finite = finite && std::isfinite(x.left) && std::isfinite(x.right);
            largestOutput = std::max({largestOutput, std::abs(static_cast<double>(x.left)), std::abs(static_cast<double>(x.right))});
            largestChange = std::max(largestChange, std::abs(static_cast<double>(x.left - previous))); previous = x.left;
        }
        tracking = false; plan = std::move(next);
    }
    check(allocations == before, "Realtime configure/process/inherit make no C++ heap allocations or deallocations");
    check(identical, "Ordinary rapid edits preserve exact tone, room and smoothing histories across publication");
    std::cout << "Rapid movement maximum adjacent DC-signal jump=" << largestChange << "; peak=" << largestOutput << '\n';
    check(finite && largestOutput < .5 && largestChange < .003, "Fast full-pad reversals retain at least 6 dB headroom without abrupt sample jumps");
    // A label/layout-only publication must also retain a room tail already in flight.
    input.fill({}); tracking = true; b->inherit(*a, *plan, *plan); tracking = false;
    bool sameTail = true; double tailEnergy{};
    for (int block = 0; block < 10; ++block) {
        a->process(*plan, views); b->process(*plan, views);
        for (std::size_t i = 0; i < quantumFrames; ++i) { const auto x = a->output(1)[i], y = b->output(1)[i]; sameTail &= x.left == y.left && x.right == y.right; tailEnergy += x.left * x.left; }
    }
    check(sameTail && tailEnergy > 0, "Layout publication preserves the audible room tail exactly");
    graph.nodes[1].bypass = true; auto bypass = std::make_unique<GraphPlan>(compileGraph(graph));
    b->inherit(*a, *plan, *bypass); input.fill({.123f, -.076f}); for (int i = 0; i < 10; ++i) b->process(*bypass, views);
    check(b->output(1).back().left == input.back().left && b->output(1).back().right == input.back().right, "Standard bypass settles to the original stereo input");
    graph.nodes[1].muted = true; *bypass = compileGraph(graph); for (int i = 0; i < 3; ++i) b->process(*bypass, views);
    check(b->output(1).back().left == 0, "Bypass retains explicit saved mute safety");
    graph.nodes[1].muted = graph.nodes[1].bypass = false; graph.nodes[1].voiceColor = {}; auto reset = std::make_unique<GraphPlan>(compileGraph(graph));
    b->inherit(*a, *plan, *reset); for (int i = 0; i < 10; ++i) b->process(*reset, views);
    near(b->output(1).back().left, input.back().left, 1e-7, "Reset to neutral settles to dry input without a history restart (left)");
    near(b->output(1).back().right, input.back().right, 1e-7, "Reset to neutral settles to dry input without a history restart (right)");
    tracking = true; b->reset(); input.fill({}); b->process(*reset, views); tracking = false;
    check(allocations == before, "Recovery reset also avoids realtime heap work");
    near(b->output(1).back().left, 0, 0, "Recovery clears all retained voice and room history");
}
void neutralCrossings() {
    auto graph = chain(); auto plan = std::make_unique<GraphPlan>(compileGraph(graph));
    auto dsp = std::make_unique<GraphProcessor>(); dsp->prepare(*plan);
    std::array<Frame, quantumFrames> input{}; input.fill({.1f, -.037f}); GraphProcessor::Inputs views{}; views[0] = input;
    for (int i = 0; i < 20; ++i) dsp->process(*plan, views);
    float previous = dsp->output(1).back().left; double jump{}, deviation{};
    for (int block = 0; block < 120; ++block) {
        graph.nodes[1].voiceColor.tone = block % 3 == 0 ? 0 : block % 3 == 1 ? .001 : -.001;
        *plan = compileGraph(graph); dsp->process(*plan, views);
        for (const auto x : dsp->output(1)) {
            jump = std::max(jump, std::abs(static_cast<double>(x.left - previous)));
            deviation = std::max(deviation, std::abs(static_cast<double>(x.left - .1f))); previous = x.left;
        }
    }
    check(jump < 2e-6 && deviation < .00015, "Tiny movement through exact neutral does not produce coefficient discontinuities");
    jump = 0;
    for (int block = 0; block < 200; ++block) {
        graph.nodes[1].voiceColor.tone = .02 * std::sin(2 * std::numbers::pi * block / 40);
        *plan = compileGraph(graph); dsp->process(*plan, views);
        for (const auto x : dsp->output(1)) { jump = std::max(jump, std::abs(static_cast<double>(x.left - previous))); previous = x.left; }
    }
    check(jump < 1e-5, "Continuous gentle sweeps through neutral retain smooth response");
    std::cout << "Tiny neutral crossing max error=" << deviation << "; sweep max adjacent jump=" << jump << '\n';
    std::mt19937 random(81247); std::uniform_real_distribution<float> noise(-.1f, .1f);
    for (bool broadband : {false, true}) {
        graph.nodes[1].voiceColor = {}; *plan = compileGraph(graph); dsp->reset();
        input.fill({}); for (int i = 0; i < 10; ++i) dsp->process(*plan, views);
        double residual{};
        for (int block = 0; block < 120; ++block) {
            for (std::size_t i = 0; i < input.size(); ++i) {
                const float x = broadband ? noise(random) : static_cast<float>(.1 * std::sin(2 * std::numbers::pi * 997 * (block * quantumFrames + i) / sampleRate));
                input[i] = {x, -.37f * x};
            }
            graph.nodes[1].voiceColor.tone = block % 3 == 0 ? 0 : block % 3 == 1 ? .001 : -.001;
            *plan = compileGraph(graph); dsp->process(*plan, views);
            for (std::size_t i = 0; i < input.size(); ++i)
                residual = std::max(residual, std::abs(static_cast<double>(dsp->output(1)[i].left - input[i].left)));
        }
        check(residual < .00015, broadband ? "Broadband epsilon-neutral edits have no large burst" : "Sine epsilon-neutral edits have no large burst");
    }
}
void timing() {
    for (int count : {1, 30}) {
        AudioGraph graph; GraphNode source; source.id = "source"; source.kind = "virtualInput"; graph.nodes.push_back(source);
        std::string previous = source.id;
        for (int i = 0; i < count; ++i) {
            GraphNode color; color.id = "color-" + std::to_string(i); color.kind = "voiceColor"; color.voiceColor = {.7, .8};
            graph.edges.push_back({"edge-" + std::to_string(i), previous, color.id}); previous = color.id; graph.nodes.push_back(color);
        }
        auto plan = std::make_unique<GraphPlan>(compileGraph(graph)); auto dsp = std::make_unique<GraphProcessor>(); dsp->prepare(*plan);
        std::array<Frame, quantumFrames> input{}; input.fill({.03f, -.01f}); GraphProcessor::Inputs views{}; views[0] = input;
        std::vector<double> times; times.reserve(300);
        for (int block = 0; block < 350; ++block) {
            const auto start = std::chrono::steady_clock::now(); dsp->process(*plan, views);
            const auto elapsed = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - start).count();
            if (block >= 50) times.push_back(elapsed);
        }
        std::sort(times.begin(), times.end());
        std::cout << count << " Voice Color nodes: median=" << times[150] << "us; p99=" << times[297] << "us per 240-frame quantum (5000us). Offline processing time only, not device latency.\n";
    }
}
void persistenceAndValidation() {
    auto config = defaultConfiguration(); config.graph = chain(.37, .62); config.graph.nodes[1].bypass = true;
    config.graph.nodes[1].x = 218.25; config.graph.nodes[1].y = -173.5;
    const Json json = config; const auto loaded = json.get<Configuration>(); validateConfiguration(loaded);
    check(Json(loaded) == json, "Both axes, bypass and node layout round-trip through version-2 configuration");
    auto copy = loaded.graph.nodes[1]; copy.voiceColor = {}; check(loaded.graph.nodes[1].voiceColor.tone == .37 && loaded.graph.nodes[1].voiceColor.ambience == .62,
        "Node copy settings are independent; defaults are neutral");
    Configuration legacyFixture; migrateLegacyGraph(legacyFixture);
    auto legacy = Json(legacyFixture); for (auto& node : legacy["graph"]["nodes"]) node.erase("voiceColor");
    const auto old = legacy.get<Configuration>(); auto upgraded = Json(old); for (auto& node : upgraded["graph"]["nodes"]) node.erase("voiceColor");
    check(upgraded == legacy, "Existing saved graphs retain every old field, route, sound control and position");
    check(compileGraph(old.graph) == compileGraph(legacyFixture.graph), "Missing Voice Color fields preserve the exact old DSP plan");
    for (double invalid : {-1.001, 1.001, std::numeric_limits<double>::infinity(), std::numeric_limits<double>::quiet_NaN()}) {
        auto graph = chain(); graph.nodes[1].voiceColor.tone = invalid; rejects([&] { compileGraph(graph); }, "Invalid or nonfinite tone rejected");
    }
    for (double invalid : {-.001, 1.001, std::numeric_limits<double>::infinity(), std::numeric_limits<double>::quiet_NaN()}) {
        auto graph = chain(); graph.nodes[1].voiceColor.ambience = invalid; rejects([&] { compileGraph(graph); }, "Invalid or nonfinite ambience rejected");
    }
    auto malformed = json; malformed["graph"]["nodes"][1]["voiceColor"] = "bad";
    rejects([&] { auto bad = malformed.get<Configuration>(); validateConfiguration(bad); }, "Malformed Voice Color settings cannot silently load");
    malformed["graph"]["nodes"][1]["voiceColor"] = nullptr;
    rejects([&] { auto bad = malformed.get<Configuration>(); validateConfiguration(bad); }, "Null Voice Color settings cannot silently load");
}
}
int main() {
    try { persistenceAndValidation(); neutralAndTone(); characterAndThickening(); compressionAndHeadroom(); levelRelativeDensity(); stateAndUpdates(); neutralCrossings(); timing();
        std::cout << "PASS: " << checks << " Voice Color assertions; synthetic audio only.\n"; return 0;
    } catch (const std::exception& e) { tracking = false; std::cerr << "FAIL after " << checks << " checks: " << e.what() << '\n'; return 1; }
}
