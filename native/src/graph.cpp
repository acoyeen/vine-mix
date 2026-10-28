#include "am/graph.hpp"
#include "am/advanced_effects.hpp"
#include <algorithm>
#include <cmath>
#include <limits>
#include <numbers>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>

namespace am {
namespace {
GraphKind parseKind(const std::string& kind) {
    if (kind == "microphone") return GraphKind::microphone;
    if (kind == "virtualInput") return GraphKind::virtualInput;
    if (kind == "appInput") return GraphKind::appInput;
    if (kind == "mix") return GraphKind::mix;
    if (kind == "gain") return GraphKind::gain;
    if (kind == "output") return GraphKind::output;
    if (kind == "virtualOutput") return GraphKind::virtualOutput;
    if (kind == "control") return GraphKind::control;
    if (kind == "eq") return GraphKind::eq;
    if (kind == "tone") return GraphKind::tone;
    if (kind == "highPass") return GraphKind::highPass;
    if (kind == "lowPass") return GraphKind::lowPass;
    if (kind == "compressor") return GraphKind::compressor;
    if (kind == "limiter") return GraphKind::limiter;
    if (kind == "ducking") return GraphKind::ducking;
    if (kind == "pan") return GraphKind::pan;
    if (kind == "mono") return GraphKind::mono;
    if (kind == "channelSwap") return GraphKind::channelSwap;
    if (kind == "stereoWidth") return GraphKind::stereoWidth;
    if (kind == "stereoPan") return GraphKind::stereoPan;
    if (kind == "crossfade") return GraphKind::crossfade;
    if (kind == "stereoCombine") return GraphKind::stereoCombine;
    if (kind == "stereoSplit") return GraphKind::stereoSplit;
    if (kind == "reroute") return GraphKind::reroute;
    if (kind == "denoiser") return GraphKind::denoiser;
    if (kind == "deEsser") return GraphKind::deEsser;
    if (kind == "plosive") return GraphKind::plosive;
    if (kind == "reverb") return GraphKind::reverb;
    if (kind == "voiceColor") return GraphKind::voiceColor;
    if (kind == "expander") return GraphKind::expander;
    if (kind == "gate") return GraphKind::gate;
    if (kind == "alignmentDelay") return GraphKind::alignmentDelay;
    throw std::runtime_error("Unknown node type: " + kind);
}
void checkGain(float value) {
    if (!std::isfinite(value) || value < -60.f || value > 12.f)
        throw std::runtime_error("Node and cable volumes must be between -60 and +12 dB.");
}
void checkId(const std::string& id) {
    if (id.empty() || id.size() > 64 || std::any_of(id.begin(), id.end(), [](unsigned char c) { return c < 32; }))
        throw std::runtime_error("Node and cable identifiers must contain 1 to 64 printable bytes.");
}
float clean(float sample) noexcept { return std::isfinite(sample) ? sample : 0.f; }
float linear(float db) noexcept { return std::pow(10.f, std::clamp(db, -180.f, 36.f) / 20.f); }
float bounded(double sample, bool sink) noexcept {
    const double maximum = sink ? 1.0 : static_cast<double>(std::numeric_limits<float>::max());
    return static_cast<float>(std::clamp(sample, -maximum, maximum));
}
bool filterProcessor(GraphKind kind) noexcept {
    return kind == GraphKind::eq || kind == GraphKind::tone || kind == GraphKind::highPass || kind == GraphKind::lowPass;
}
bool compressorProcessor(GraphKind kind) noexcept {
    return kind == GraphKind::compressor || kind == GraphKind::limiter;
}
bool processor(GraphKind kind) noexcept {
    return kind == GraphKind::mix || kind == GraphKind::gain || filterProcessor(kind)
        || compressorProcessor(kind) || kind == GraphKind::ducking || kind == GraphKind::pan || kind == GraphKind::mono || kind == GraphKind::reroute
        || kind == GraphKind::channelSwap || kind == GraphKind::stereoWidth || kind == GraphKind::stereoPan
        || kind == GraphKind::crossfade || kind == GraphKind::stereoCombine || kind == GraphKind::stereoSplit
        || AdvancedEffect::supports(kind);
}
GraphPort audioPort(GraphKind kind, const std::string& port, bool output) {
    if ((output && kind == GraphKind::stereoSplit) || (!output && kind == GraphKind::stereoCombine)) {
        if (port == "left") return GraphPort::left;
        if (port == "right") return GraphPort::right;
    } else if (!output && kind == GraphKind::crossfade) {
        if (port == "a") return GraphPort::a;
        if (port == "b") return GraphPort::b;
    } else if (port == "audio") return GraphPort::audio;
    throw std::runtime_error("A cable refers to an unavailable audio socket.");
}
Frame selectedOutput(Frame sample, GraphPort port) noexcept {
    if (port == GraphPort::left) return {sample.left, sample.left};
    if (port == GraphPort::right) return {sample.right, sample.right};
    return sample;
}
std::array<double, 2> panWeights(double position) noexcept {
    position = std::clamp(position, -1., 1.);
    const double angle = (position + 1) * std::numbers::pi / 4;
    return {position == 1 ? 0 : std::cos(angle), position == -1 ? 0 : std::sin(angle)};
}
std::array<double, 2> crossfadeWeights(double position, double curve) noexcept {
    position = std::clamp(position, 0., 1.);
    return {position == 1 ? 0 : (1 - position) * (1 - curve) + std::cos(position * std::numbers::pi / 2) * curve,
        position == 0 ? 0 : position * (1 - curve) + std::sin(position * std::numbers::pi / 2) * curve};
}
void range(double value, double low, double high, const char* message) {
    if (!std::isfinite(value) || value < low || value > high) throw std::runtime_error(message);
}
GraphPlan::Biquad coefficients(const EqBand& band, bool canonicalizeFlat = true) {
    range(band.frequencyHz, 20, 20000, "EQ frequency must be between 20 Hz and 20 kHz.");
    range(band.gainDb, -24, 24, "EQ gain must be between -24 and +24 dB.");
    range(band.q, .1, 20, "EQ Q must be between 0.1 and 20.");
    if (band.resetFrequencyHz != 0) range(band.resetFrequencyHz, 20, 20000, "EQ reset frequency must be unset or between 20 Hz and 20 kHz.");
    const double w = 2 * std::numbers::pi * band.frequencyHz / sampleRate;
    const double c = std::cos(w), s = std::sin(w), alpha = s / (2 * band.q);
    const double a = std::pow(10., band.gainDb / 40.), beta = 2 * std::sqrt(a) * alpha;
    double b0{}, b1{}, b2{}, a0{}, a1{}, a2{};
    if (band.type == "peak") {
        b0 = 1 + alpha * a; b1 = -2 * c; b2 = 1 - alpha * a;
        a0 = 1 + alpha / a; a1 = -2 * c; a2 = 1 - alpha / a;
    } else if (band.type == "lowPass") {
        b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0;
        a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha;
    } else if (band.type == "highPass") {
        b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0;
        a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha;
    } else if (band.type == "notch") {
        b0 = 1; b1 = -2 * c; b2 = 1;
        a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha;
    } else if (band.type == "lowShelf") {
        b0 = a * ((a + 1) - (a - 1) * c + beta); b1 = 2 * a * ((a - 1) - (a + 1) * c);
        b2 = a * ((a + 1) - (a - 1) * c - beta);
        a0 = (a + 1) + (a - 1) * c + beta; a1 = -2 * ((a - 1) + (a + 1) * c);
        a2 = (a + 1) + (a - 1) * c - beta;
    } else if (band.type == "highShelf") {
        b0 = a * ((a + 1) + (a - 1) * c + beta); b1 = -2 * a * ((a - 1) + (a + 1) * c);
        b2 = a * ((a + 1) + (a - 1) * c - beta);
        a0 = (a + 1) - (a - 1) * c + beta; a1 = 2 * ((a - 1) - (a + 1) * c);
        a2 = (a + 1) - (a - 1) * c - beta;
    } else throw std::runtime_error("Unknown EQ band type.");
    if (!band.enabled) return {};
    // A zero-gain bell/shelf is exactly transparent. Canonicalize it before
    // compilation so it needs no filter work or history on the audio thread.
    if (canonicalizeFlat && band.gainDb == 0 && (band.type == "peak" || band.type == "lowShelf" || band.type == "highShelf")) return {};
    return {b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0};
}
}
bool isSource(GraphKind kind) noexcept {
    return kind == GraphKind::microphone || kind == GraphKind::virtualInput || kind == GraphKind::appInput;
}
bool isSink(GraphKind kind) noexcept { return kind == GraphKind::output || kind == GraphKind::virtualOutput; }

GraphPlan compileGraph(const AudioGraph& graph) {
    if (graph.nodes.size() > maximumGraphNodes || graph.edges.size() > maximumGraphEdges)
        throw std::runtime_error("A canvas supports up to 32 nodes and 96 cables.");
    if (!std::isfinite(graph.panX) || !std::isfinite(graph.panY) || !std::isfinite(graph.zoom)
        || std::abs(graph.panX) > 100000 || std::abs(graph.panY) > 100000 || graph.zoom < .15 || graph.zoom > 4)
        throw std::runtime_error("The canvas position or zoom is invalid.");
    GraphPlan plan;
    plan.nodeCount = graph.nodes.size(); plan.edgeCount = graph.edges.size();
    std::unordered_map<std::string, std::size_t> ids;
    std::unordered_set<std::string> edgeIds;
    std::unordered_set<std::string> connections;
    std::array<std::size_t, maximumGraphNodes> indegrees{};
    for (std::size_t i = 0; i < graph.nodes.size(); ++i) {
        const auto& source = graph.nodes[i];
        checkId(source.id); checkGain(source.gainDb);
        if (!ids.emplace(source.id, i).second) throw std::runtime_error("Two nodes have the same identifier.");
        if (source.name.size() > 256 || !std::isfinite(source.x) || !std::isfinite(source.y)
            || std::abs(source.x) > 100000 || std::abs(source.y) > 100000)
            throw std::runtime_error("A node name or position is invalid.");
        plan.nodes[i].id = source.id;
        plan.nodes[i].kind = parseKind(source.kind);
        plan.nodes[i].gainDb = source.gainDb;
        if (!std::isfinite(source.controlMultiplier) || (source.controlMultiplier != -1 && (source.controlMultiplier < 0 || source.controlMultiplier > 2)))
            throw std::runtime_error("Linked control multiplier must be between 0 and 200 percent.");
        plan.nodes[i].controlMultiplier = static_cast<float>(source.controlMultiplier);
        plan.nodes[i].muted = source.muted;
        plan.nodes[i].mono = source.mono;
        plan.nodes[i].selectedMicrophoneChannel = source.kind == "microphone" && source.device.channel >= 0;
        // Structural channel routing has no transparent alternative.
        plan.nodes[i].bypass = source.bypass && source.kind != "stereoSplit" && source.kind != "stereoCombine";
        if (source.dynamicsUiVersion < 0 || source.dynamicsUiVersion > 1)
            throw std::runtime_error("Unknown dynamics interface version.");
        range(source.denoiser.suppressionDb, -40, 0, "Denoiser suppression must be between -40 and 0 dB.");
        if (source.denoiser.algorithm != "speex" && source.denoiser.algorithm != "rnnoise")
            throw std::runtime_error("Unknown denoiser algorithm.");
        range(source.denoiser.rnnoiseMix, 0, 1, "RNNoise mix must be between 0 and 100 percent.");
        range(source.deEsser.frequencyHz, 2000, 12000, "De-esser frequency must be between 2 and 12 kHz.");
        range(source.deEsser.thresholdDb, -60, 0, "De-esser threshold must be between -60 and 0 dB.");
        range(source.deEsser.reductionDb, 0, 24, "De-esser reduction must be between 0 and 24 dB.");
        range(source.plosive.frequencyHz, 60, 300, "Plosive cutoff must be between 60 and 300 Hz.");
        range(source.plosive.thresholdDb, -60, 0, "Plosive threshold must be between -60 and 0 dB.");
        range(source.plosive.reductionDb, 0, 24, "Plosive reduction must be between 0 and 24 dB.");
        range(source.reverb.mix, 0, 1, "Reverb mix must be between 0 and 1.");
        range(source.reverb.decaySeconds, .1, 5, "Reverb decay must be between 0.1 and 5 seconds.");
        range(source.reverb.damping, 0, 1, "Reverb damping must be between 0 and 1.");
        range(source.reverb.preDelayMs, 0, 100, "Reverb pre-delay must be between 0 and 100 ms.");
        range(source.voiceColor.tone, -1, 1, "Voice Color tone must be between -1 (Warm) and +1 (Bright).");
        range(source.voiceColor.ambience, 0, 1, "Voice Color character must be between 0 (Natural) and 1 (Broadcast).");
        plan.nodes[i].denoiser = source.denoiser; plan.nodes[i].deEsser = source.deEsser;
        plan.nodes[i].plosive = source.plosive; plan.nodes[i].reverb = source.reverb;
        plan.nodes[i].voiceColor = source.voiceColor;
        const auto& expander = source.expander;
        if (expander.mode != "expander" && expander.mode != "gate") throw std::runtime_error("Unknown Expander / Gate mode.");
        if (source.kind == "gate" && expander.mode != "gate") throw std::runtime_error("Gate nodes require Gate mode.");
        range(expander.thresholdDb, -80, 0, "Expander threshold must be between -80 and 0 dB.");
        range(expander.ratio, 1, 20, "Expander ratio must be between 1 and 20.");
        range(expander.attackMs, .1, 200, "Expander attack must be between 0.1 and 200 ms.");
        range(expander.holdMs, 0, 1000, "Expander hold must be between 0 and 1000 ms.");
        range(expander.releaseMs, 5, 2000, "Expander release must be between 5 and 2000 ms.");
        range(expander.rangeDb, 0, 80, "Expander range must be between 0 and 80 dB.");
        range(expander.hysteresisDb, 0, 12, "Expander hysteresis must be between 0 and 12 dB.");
        if (source.alignmentDelay.samples > 96000) throw std::runtime_error("Alignment Delay supports 0 to 96000 samples.");
        plan.nodes[i].expander = expander; plan.nodes[i].alignmentDelay = source.alignmentDelay;
        if (source.eqBands.size() > maximumEqBands) throw std::runtime_error("An EQ node supports up to eight bands.");
        if (source.kind == "highPass" || source.kind == "lowPass") {
            if (source.eqBands.size() != 1 || source.eqBands[0].type != source.kind
                || !source.eqBands[0].enabled || source.eqBands[0].gainDb != 0)
                throw std::runtime_error("High-pass and low-pass nodes require exactly one enabled matching filter with zero band gain. Use the node bypass switch to disable it.");
        }
        if (source.kind == "tone") {
            const auto expected = toneDefaults();
            if (source.eqBands.size() != expected.size())
                throw std::runtime_error("Low / Mid / High requires exactly three tone bands.");
            for (std::size_t band = 0; band < expected.size(); ++band) {
                const auto& actual = source.eqBands[band]; const auto& fixed = expected[band];
                range(actual.gainDb, -12, 12, "Low / Mid / High levels must be between -12 and +12 dB.");
                if (actual.type != fixed.type || !actual.enabled || actual.frequencyHz != fixed.frequencyHz
                    || !std::isfinite(actual.q) || std::abs(actual.q - fixed.q) > 1e-7)
                    throw std::runtime_error("Low / Mid / High uses its fixed 200 Hz shelf, 1 kHz bell and 5 kHz shelf; use EQ for custom filters.");
            }
        }
        for (const auto& band : source.eqBands) {
            const auto coefficient = coefficients(band);
            if (coefficient != GraphPlan::Biquad{})
                plan.nodes[i].eq[plan.nodes[i].eqCount++] = coefficient;
        }
        if (source.kind == "voiceColor") {
            auto& node = plan.nodes[i]; const double tone = source.voiceColor.tone;
            // Reserve the strongest contour for the top of the pad while
            // keeping the lower part useful for lighter treatment. The same
            // curve drives dynamics and room in the processor.
            const double character = std::pow(source.voiceColor.ambience, 1.3);
            // Broadcast adds body and presence together, with a deliberate
            // low-mid cut instead of simply making the whole voice louder.
            // Tone independently spans a clearly audible warm/bright range.
            // Compile the same RBJ
            // filters as EQ off the audio thread. Fixed band slots retain
            // history across neutral and rapid direction changes.
            node.eq = {}; node.eqCount = 4;
            // Keep cancelling pole/zero pairs at neutral: replacing them with
            // canonical identity coefficients would make an epsilon movement
            // near zero a large coefficient jump during interpolation.
            node.eq[0] = coefficients({"lowShelf", true, 140, 12 * character - 12 * tone, .7071067811865476}, false);
            node.eq[1] = coefficients({"peak", true, 450, -8 * character, .9}, false);
            node.eq[2] = coefficients({"peak", true, 3000, 12 * character + 3 * tone, .85}, false);
            node.eq[3] = coefficients({"highShelf", true, 7000, 4 * character + 9 * tone, .7071067811865476}, false);
            // Parallel dynamics supply the Broadcast headroom. Warmth can
            // add substantial bass energy, so reserve headroom on that side.
            // Bright already removes voice body; applying the same global
            // cut there would unnecessarily turn ordinary speech down.
            node.voiceColorGain = std::pow(10., (-6 * std::max(-tone, 0.)) / 20.);
            // Only wet amount moves. Delay and decay stay fixed, avoiding
            // pitch modulation while dragging the pad vertically.
            // Most thickening now comes from compression and a short double;
            // the room is only a little space, never the dominant upper axis.
            node.reverb = {.02 * character, .3, .65, 8};
        }
        const auto& comp = source.compressor;
        range(comp.thresholdDb, -60, 0, "Compressor threshold must be between -60 and 0 dB.");
        range(comp.ratio, 1, 20, "Compressor ratio must be between 1 and 20.");
        range(comp.attackMs, .1, 200, "Compressor attack must be between 0.1 and 200 ms.");
        range(comp.releaseMs, 5, 2000, "Compressor release must be between 5 and 2000 ms.");
        range(comp.makeupDb, 0, 24, "Compressor makeup gain must be between 0 and +24 dB.");
        if (comp.mode != "compress" && comp.mode != "limit") throw std::runtime_error("Unknown Compressor mode.");
        if (comp.detector != "internal" && comp.detector != "external") throw std::runtime_error("Unknown Compressor detector.");
        range(comp.ceilingDb, -24, 0, "Limiter ceiling must be between -24 and 0 dB.");
        // Dedicated Limiter and saved Compressor limit mode retain the same
        // algorithm. Saved detector metadata remains readable, but Compressor
        // always listens to its own programme; external triggers use Ducking.
        plan.nodes[i].limit = source.kind == "limiter" || comp.mode == "limit";
        plan.nodes[i].ceiling = std::pow(10., comp.ceilingDb / 20.);
        plan.nodes[i].threshold = std::pow(10., comp.thresholdDb / 20.);
        plan.nodes[i].ratio = comp.ratio;
        plan.nodes[i].attack = std::exp(-1. / (comp.attackMs * .001 * sampleRate));
        plan.nodes[i].release = std::exp(-1. / (comp.releaseMs * .001 * sampleRate));
        plan.nodes[i].makeup = std::pow(10., comp.makeupDb / 20.);
        const auto& duck = source.ducking;
        if (duck.mode != "static" && duck.mode != "dynamic") throw std::runtime_error("Unknown Ducking mode.");
        range(duck.ratio, 1, 20, "Dynamic Ducking ratio must be between 1 and 20.");
        range(duck.thresholdDb, -60, 0, "Ducking threshold must be between -60 and 0 dB.");
        range(duck.amountDb, 0, 60, "Duck amount must be between 0 and 60 dB.");
        range(duck.attackMs, .1, 200, "Ducking attack must be between 0.1 and 200 ms.");
        range(duck.releaseMs, 5, 2000, "Ducking release must be between 5 and 2000 ms.");
        if (source.kind == "ducking") {
            auto& node = plan.nodes[i];
            node.threshold = std::pow(10., duck.thresholdDb / 20.);
            node.dynamicDucking = duck.mode == "dynamic";
            node.ratio = duck.ratio;
            node.duckingCloseThreshold = std::pow(10., (duck.thresholdDb - 3.) / 20.);
            node.duckingAmount = std::pow(10., -duck.amountDb / 20.);
            node.attack = std::exp(-1. / (duck.attackMs * .001 * sampleRate));
            node.release = std::exp(-1. / (duck.releaseMs * .001 * sampleRate));
        }
        range(source.balance, -1, 1, "Balance must be between left and right.");
        plan.nodes[i].panLeft = static_cast<float>(source.balance > 0 ? 1 - source.balance : 1);
        plan.nodes[i].panRight = static_cast<float>(source.balance < 0 ? 1 + source.balance : 1);
        range(source.stereoWidth, 0, 2, "Stereo Width must be between 0 and 200 percent.");
        range(source.panPosition, -1, 1, "Pan must be between left and right.");
        range(source.crossfadePosition, 0, 1, "Crossfade must be between A and B.");
        if (source.crossfadeCurve != "equalPower" && source.crossfadeCurve != "linear")
            throw std::runtime_error("Unknown crossfade curve.");
        plan.nodes[i].stereoWidth = static_cast<float>(source.stereoWidth);
        plan.nodes[i].position = static_cast<float>(source.panPosition);
        plan.nodes[i].fadePosition = static_cast<float>(source.crossfadePosition);
        plan.nodes[i].equalPower = source.crossfadeCurve == "equalPower";
    }
    for (std::size_t i = 0; i < graph.edges.size(); ++i) {
        const auto& source = graph.edges[i];
        checkId(source.id); checkGain(source.gainDb);
        if (!edgeIds.insert(source.id).second) throw std::runtime_error("Two cables have the same identifier.");
        const auto from = ids.find(source.from), to = ids.find(source.to);
        if (from == ids.end() || to == ids.end()) throw std::runtime_error("A cable refers to a missing node.");
        if (from->second == to->second) throw std::runtime_error("A node cannot connect to itself.");
        if (source.kind != "audio" && source.kind != "control" && source.kind != "sidechain") throw std::runtime_error("Unknown cable type.");
        const bool control = source.kind == "control";
        const bool sidechain = source.kind == "sidechain";
        const auto fromKind = plan.nodes[from->second].kind, toKind = plan.nodes[to->second].kind;
        GraphPort fromPort = GraphPort::audio, toPort = GraphPort::audio;
        if (control) {
            if (source.fromPort != "audio" || source.toPort != "audio")
                throw std::runtime_error("A control cable cannot select an audio channel.");
            if (fromKind != GraphKind::control || toKind == GraphKind::control)
                throw std::runtime_error("A control cable must connect a control node to an audio node.");
            auto& controlEdge = plan.nodes[to->second].controlEdge;
            if (controlEdge >= 0) throw std::runtime_error("An audio node can have one shared control connection.");
            controlEdge = static_cast<int>(i);
        } else if (sidechain) {
            fromPort = audioPort(fromKind, source.fromPort, true);
            if (source.toPort != "audio") throw std::runtime_error("A trigger cable cannot select a programme input.");
            if ((!isSource(fromKind) && !processor(fromKind)) || toKind != GraphKind::ducking)
                throw std::runtime_error("A sidechain connects an audio source or effect to a Ducking trigger.");
            auto& detector = plan.nodes[to->second].sidechainEdge;
            if (detector >= 0) throw std::runtime_error("A Ducking node can have one trigger cable.");
            detector = static_cast<int>(i);
        } else {
            fromPort = audioPort(fromKind, source.fromPort, true);
            toPort = audioPort(toKind, source.toPort, false);
            if ((!isSource(fromKind) && !processor(fromKind)) || (!processor(toKind) && !isSink(toKind)))
                throw std::runtime_error("Connect audio from an input or processor to a processor or output.");
            auto& target = plan.nodes[to->second];
            target.incoming[target.incomingCount++] = i;
        }
        // Explicit separators prevent collisions even when an ID contains a dash.
        const auto connection = source.kind + '\0' + source.from + '\0' + source.fromPort + '\0' + source.to + '\0' + source.toPort;
        if (!connections.insert(connection).second) throw std::runtime_error("These nodes are already connected.");
        plan.edges[i] = {source.id, from->second, to->second, control, sidechain, source.gainDb, source.muted, fromPort, toPort};
        ++indegrees[to->second];
    }
    std::array<std::size_t, maximumGraphNodes> ready{};
    std::size_t head{}, tail{}, visited{};
    for (std::size_t i = 0; i < plan.nodeCount; ++i) if (indegrees[i] == 0) ready[tail++] = i;
    while (head != tail) {
        const auto node = ready[head++]; plan.order[visited++] = node;
        for (std::size_t i = 0; i < plan.edgeCount; ++i)
            if (plan.edges[i].from == node && --indegrees[plan.edges[i].to] == 0) ready[tail++] = plan.edges[i].to;
    }
    if (visited != plan.nodeCount) throw std::runtime_error("This connection creates an audio feedback loop. Remove a cable first.");
    return plan;
}

GraphChannels graphSignalChannels(const GraphPlan& plan, const GraphChannels& captureChannels) noexcept {
    GraphChannels channels{};
    for (std::size_t p = 0; p < plan.nodeCount; ++p) {
        const auto i = plan.order[p]; const auto& node = plan.nodes[i];
        if (node.kind == GraphKind::control) continue;
        if (isSource(node.kind)) {
            channels[i] = node.kind == GraphKind::appInput ? 2 : std::clamp(captureChannels[i], 0, 2);
            if (node.kind == GraphKind::microphone && (node.mono || node.selectedMicrophoneChannel)) channels[i] = 1;
            continue;
        }
        bool unknown = node.incomingCount == 0;
        for (std::size_t n = 0; n < node.incomingCount; ++n) {
            const auto& edge = plan.edges[node.incoming[n]];
            if (node.kind == GraphKind::crossfade) {
                if ((node.bypass || node.fadePosition == 0) && edge.toPort == GraphPort::b) continue;
                if (!node.bypass && node.fadePosition == 1 && edge.toPort == GraphPort::a) continue;
            }
            auto upstream = channels[edge.from];
            if (upstream != 0 && edge.fromPort != GraphPort::audio) upstream = 1;
            unknown = unknown || upstream == 0;
            channels[i] = std::max(channels[i], upstream);
        }
        // A known stereo input establishes stereo even when another source is
        // absent. Mono plus an unknown source cannot yet promise mono output.
        if (unknown && channels[i] < 2) channels[i] = 0;
        if (!node.bypass && node.kind == GraphKind::mono) channels[i] = 1;
        if (node.kind == GraphKind::stereoCombine) channels[i] = 2;
        if (!node.bypass && node.kind == GraphKind::stereoWidth && node.stereoWidth == 0) channels[i] = 1;
        if (!node.bypass && node.kind == GraphKind::stereoPan) channels[i] = node.position == 0 ? 1 : 2;
        if (!node.bypass && node.kind == GraphKind::pan && node.panLeft != node.panRight && channels[i] != 0) channels[i] = 2;
        if (!node.bypass && (node.kind == GraphKind::reverb || node.kind == GraphKind::voiceColor)
            && node.reverb.mix > 0 && channels[i] != 0) channels[i] = 2;
    }
    return channels;
}

bool sameGraphTopology(const GraphPlan& a, const GraphPlan& b) noexcept {
    if (a.nodeCount != b.nodeCount || a.edgeCount != b.edgeCount) return false;
    for (std::size_t i = 0; i < a.nodeCount; ++i) {
        bool found = false;
        for (std::size_t j = 0; j < b.nodeCount; ++j)
            if (a.nodes[i].id == b.nodes[j].id && a.nodes[i].kind == b.nodes[j].kind) { found = true; break; }
        if (!found) return false;
    }
    for (std::size_t i = 0; i < a.edgeCount; ++i) {
        bool found = false;
        const auto& edge = a.edges[i];
        for (std::size_t j = 0; j < b.edgeCount; ++j) {
            const auto& other = b.edges[j];
            if (edge.id == other.id && edge.control == other.control && edge.sidechain == other.sidechain
                && edge.fromPort == other.fromPort && edge.toPort == other.toPort && a.nodes[edge.from].id == b.nodes[other.from].id
                && a.nodes[edge.to].id == b.nodes[other.to].id) { found = true; break; }
        }
        if (!found) return false;
    }
    return true;
}

GraphProcessor::GraphProcessor() = default;
GraphProcessor::~GraphProcessor() = default;
void GraphProcessor::prepare(const GraphPlan& plan) {
    for (std::size_t i = 0; i < maximumGraphNodes; ++i) {
        advanced_[i].reset();
        if (i < plan.nodeCount && AdvancedEffect::supports(plan.nodes[i].kind))
            advanced_[i] = std::make_unique<AdvancedEffect>(plan.nodes[i].kind);
    }
}
void GraphProcessor::Ramp::set(float value) noexcept {
    if (value == target) return;
    target = value; step = (target - current) / 480.f; remaining = 480;
}
float GraphProcessor::Ramp::next() noexcept {
    if (remaining && --remaining == 0) current = target;
    else if (remaining) current += step;
    return current;
}
void GraphProcessor::process(const GraphPlan& plan, const Inputs& inputs) noexcept {
    meters_ = {};
    for (std::size_t i = 0; i < plan.nodeCount; ++i) {
        const auto& node = plan.nodes[i];
        // An intermediate bypass is transparent after incoming cable weights:
        // skip its level as well as its DSP. Source/sink levels remain active.
        // Saved mute flags stay authoritative until an explicit user edit;
        // reinterpreting an older muted node as bypass could expose live audio.
        const bool bypass = processor(node.kind) && node.bypass;
        float gain = bypass ? 0.f : node.gainDb, multiplier = 1; bool muted = node.muted;
        if (node.controlEdge >= 0) {
            const auto& edge = plan.edges[static_cast<std::size_t>(node.controlEdge)];
            const auto& control = plan.nodes[edge.from];
            if (!edge.muted) {
                if (!bypass && !control.bypass) {
                    if (control.controlMultiplier < 0) gain += control.gainDb + edge.gainDb;
                    else { gain += edge.gainDb; multiplier = control.controlMultiplier; }
                }
                muted = muted || control.muted;
            }
        }
        nodeRamps_[i].set(muted ? 0.f : linear(gain) * multiplier);
        effects_[i].wet.set(node.bypass ? 0.f : 1.f);
        effects_[i].panLeft.set(node.panLeft); effects_[i].panRight.set(node.panRight);
        effects_[i].makeup.set(static_cast<float>(node.makeup));
        effects_[i].mono.set(node.kind == GraphKind::microphone && node.mono ? 1.f : 0.f);
        effects_[i].limitMix.set(node.limit ? 1.f : 0.f);
        effects_[i].width.set(node.stereoWidth);
        effects_[i].position.set(node.position);
        effects_[i].fadePosition.set(node.fadePosition);
        effects_[i].fadeCurve.set(node.equalPower ? 1.f : 0.f);
        if (advanced_[i]) advanced_[i]->configure(node);
    }
    for (std::size_t i = 0; i < plan.edgeCount; ++i)
        edgeRamps_[i].set(plan.edges[i].muted ? 0.f : linear(plan.edges[i].gainDb));
    for (std::size_t p = 0; p < plan.nodeCount; ++p) {
        const auto i = plan.order[p]; const auto& node = plan.nodes[i];
        const bool source = isSource(node.kind), sink = isSink(node.kind);
        const bool dynamics = compressorProcessor(node.kind) || node.kind == GraphKind::ducking || node.kind == GraphKind::expander || node.kind == GraphKind::gate || node.kind == GraphKind::deEsser || node.kind == GraphKind::plosive;
        bool dynamicsAudible = dynamics && !node.bypass && !node.muted;
        if (node.controlEdge >= 0) {
            const auto& controlEdge = plan.edges[static_cast<std::size_t>(node.controlEdge)];
            const auto& control = plan.nodes[controlEdge.from];
            if (!controlEdge.muted && (control.muted || (!control.bypass && control.controlMultiplier == 0))) dynamicsAudible = false;
        }
        double minimumCompressionGain = 1, detectorPeak{};
        auto& effect = effects_[i];
        // Settled controls use the same weights throughout this quantum.
        // Keep sample-accurate ramps while editing, without repeating sine and
        // cosine for every sample once Pan or Crossfade stops moving.
        const bool settledPan = node.kind == GraphKind::stereoPan && effect.position.remaining == 0;
        const bool settledFade = node.kind == GraphKind::crossfade && effect.fadePosition.remaining == 0 && effect.fadeCurve.remaining == 0;
        const auto settledWeights = settledPan ? panWeights(effect.position.current)
            : settledFade ? crossfadeWeights(effect.fadePosition.current, effect.fadeCurve.current) : std::array<double, 2>{};
        for (std::size_t frame = 0; frame < quantumFrames; ++frame) {
            double left{}, right{}, bLeft{}, bRight{};
            if (source && frame < inputs[i].size()) {
                left = clean(inputs[i][frame].left); right = clean(inputs[i][frame].right);
            }
            for (std::size_t n = 0; n < node.incomingCount; ++n) {
                const auto edge = node.incoming[n];
                const auto gain = edgeRamps_[edge].next();
                const auto& route = plan.edges[edge];
                const auto sample = selectedOutput(buffers_[route.from][frame], route.fromPort);
                if (node.kind == GraphKind::stereoCombine) {
                    const double channel = (static_cast<double>(sample.left) + sample.right) * .5 * gain;
                    if (route.toPort == GraphPort::left) left += channel;
                    else right += channel;
                } else if (node.kind == GraphKind::crossfade && route.toPort == GraphPort::b) {
                    bLeft += static_cast<double>(sample.left) * gain;
                    bRight += static_cast<double>(sample.right) * gain;
                } else {
                    left += static_cast<double>(sample.left) * gain;
                    right += static_cast<double>(sample.right) * gain;
                }
            }
            const double limitMix = effect.limitMix.next();
            // This is a source control, independent of the device's legacy
            // channel selector. Keeping it in DSP avoids reopening the stream.
            const double mono = effect.mono.next();
            const double average = (left + right) * .5;
            left += (average - left) * mono; right += (average - right) * mono;
            double dryLeft = left, dryRight = right;
            if (filterProcessor(node.kind)) {
                const auto filter = [](double sample, const GraphPlan::Biquad& coefficient, FilterState& state) {
                    const double output = coefficient.b0 * sample + state.z1;
                    state.z1 = coefficient.b1 * sample - coefficient.a1 * output + state.z2;
                    state.z2 = coefficient.b2 * sample - coefficient.a2 * output;
                    // Denormal tails must not become an unpredictable CPU load.
                    if (std::abs(state.z1) < 1e-30) state.z1 = 0;
                    if (std::abs(state.z2) < 1e-30) state.z2 = 0;
                    return output;
                };
                double oldLeft = left, oldRight = right;
                for (std::size_t band = 0; band < node.eqCount; ++band) {
                    left = filter(left, node.eq[band], effect.filters[band][0]);
                    right = filter(right, node.eq[band], effect.filters[band][1]);
                }
                if (effect.eqTransition) {
                    for (std::size_t band = 0; band < effect.previousEqCount; ++band) {
                        oldLeft = filter(oldLeft, effect.previousEq[band], effect.previousFilters[band][0]);
                        oldRight = filter(oldRight, effect.previousEq[band], effect.previousFilters[band][1]);
                    }
                    const double mix = 1 - static_cast<double>(--effect.eqTransition) / 480.;
                    left = oldLeft * (1 - mix) + left * mix; right = oldRight * (1 - mix) + right * mix;
                }
            } else if (compressorProcessor(node.kind)) {
                const double peak = std::max(std::abs(left), std::abs(right));
                if (!node.limit) detectorPeak = std::max(detectorPeak, peak);
                const auto makeup = effect.makeup.next();
                {
                    // Preserve the established compressor's exact transfer and
                    // envelope, driven by its own stereo programme signal.
                    const double target = peak > node.threshold ? std::pow(peak / node.threshold, 1. / node.ratio - 1.) : 1.;
                    const double coefficient = target < effect.compressionGain ? node.attack : node.release;
                    effect.compressionGain = target + coefficient * (effect.compressionGain - target);
                    const double amount = limitMix == 0 ? effect.compressionGain * makeup
                        : 1 + (effect.compressionGain * makeup - 1) * (1 - limitMix);
                    left *= amount; right *= amount;
                }
            } else if (node.kind == GraphKind::ducking) {
                double triggerPeak{};
                if (node.sidechainEdge >= 0) {
                    const auto edge = static_cast<std::size_t>(node.sidechainEdge);
                    const auto detectorGain = edgeRamps_[edge].next();
                    const auto detector = selectedOutput(buffers_[plan.edges[edge].from][frame], plan.edges[edge].fromPort);
                    triggerPeak = std::max(std::abs(static_cast<double>(detector.left)), std::abs(static_cast<double>(detector.right))) * detectorGain;
                }
                // Detector-only audio never joins the programme. Fixed 5 ms
                // rise / 50 ms fall smoothing and a 3 dB closing band avoid
                // chatter; the separate gain envelope defines turn-down and
                // recovery times. Both stereo channels share one attenuation.
                constexpr double detectorAttack = .9958420018451100; // exp(-1 / (0.005 * 48000))
                constexpr double detectorRelease = .9995834201268338; // exp(-1 / (0.050 * 48000))
                const double detectorCoefficient = triggerPeak > effect.duckingDetector ? detectorAttack : detectorRelease;
                effect.duckingDetector = triggerPeak + detectorCoefficient * (effect.duckingDetector - triggerPeak);
                if (effect.duckingDetector < 1e-30) effect.duckingDetector = 0;
                if (node.sidechainEdge < 0) effect.duckingActive = false;
                else if (effect.duckingDetector >= node.threshold) effect.duckingActive = true;
                else if (effect.duckingDetector < node.duckingCloseThreshold) effect.duckingActive = false;
                // Dynamic ducking uses the established external compressor
                // curve on the raw stereo trigger, with no programme fallback
                // or make-up gain. Keep the Static detector warm in both modes
                // and share the gain envelope so mode changes retain continuity.
                const double target = node.dynamicDucking
                    ? (triggerPeak > node.threshold ? std::pow(triggerPeak / node.threshold, 1. / node.ratio - 1.) : 1.)
                    : (effect.duckingActive ? node.duckingAmount : 1.);
                const double coefficient = target < effect.duckingGain ? node.attack : node.release;
                effect.duckingGain = target + coefficient * (effect.duckingGain - target);
                detectorPeak = std::max(detectorPeak, node.dynamicDucking ? triggerPeak : effect.duckingDetector);
                left *= effect.duckingGain; right *= effect.duckingGain;
            } else if (node.kind == GraphKind::pan) {
                left *= effect.panLeft.next(); right *= effect.panRight.next();
            } else if (node.kind == GraphKind::mono) {
                left = right = (left + right) * .5;
            } else if (node.kind == GraphKind::channelSwap) {
                std::swap(left, right);
            } else if (node.kind == GraphKind::stereoWidth) {
                const double mid = (left + right) * .5, side = (left - right) * .5 * effect.width.next();
                left = mid + side; right = mid - side;
            } else if (node.kind == GraphKind::stereoPan) {
                const auto weights = settledPan ? settledWeights : panWeights(effect.position.next());
                const double signal = (left + right) * .5;
                left = signal * weights[0]; right = signal * weights[1];
            } else if (node.kind == GraphKind::crossfade) {
                const auto weights = settledFade ? settledWeights : crossfadeWeights(effect.fadePosition.next(), effect.fadeCurve.next());
                left = left * weights[0] + bLeft * weights[1]; right = right * weights[0] + bRight * weights[1];
            } else if (advanced_[i]) {
                const auto processed = advanced_[i]->process({bounded(left, false), bounded(right, false)});
                left = processed.wet.left; right = processed.wet.right;
                dryLeft = processed.dry.left; dryRight = processed.dry.right;
            }
            const double wet = effect.wet.next();
            if (compressorProcessor(node.kind) && !node.limit)
                minimumCompressionGain = std::min(minimumCompressionGain, 1 + (effect.compressionGain - 1) * wet);
            if (node.kind == GraphKind::ducking)
                minimumCompressionGain = std::min(minimumCompressionGain, 1 + (effect.duckingGain - 1) * wet);
            left = dryLeft + (left - dryLeft) * wet; right = dryRight + (right - dryRight) * wet;
            const auto gain = nodeRamps_[i].next(); left *= gain; right *= gain;
            if (!std::isfinite(left) || !std::isfinite(right)) { left = right = 0; effect.filters = {}; effect.previousFilters = {}; }
            if (compressorProcessor(node.kind) && node.limit && !node.bypass) {
                // Zero-lookahead sample-peak limiter. Measure the actual stereo
                // programme AFTER inherited node/control gain and bypass fades.
                // An immediate linked reduction catches even the first impulse;
                // release recovers smoothly. The ceiling guard remains strict
                // during edits; no detector cable can defeat it. Bypass is dry.
                const double peak = std::max(std::abs(left), std::abs(right));
                detectorPeak = std::max(detectorPeak, peak);
                const double required = peak > node.ceiling ? node.ceiling / peak : 1.;
                effect.limiterGain = required < effect.limiterGain ? required
                    : required + node.release * (effect.limiterGain - required);
                left *= effect.limiterGain; right *= effect.limiterGain;
                // A round-to-nearest float conversion must not round above
                // the requested real-valued sample ceiling.
                float safeCeiling = static_cast<float>(node.ceiling);
                if (safeCeiling > node.ceiling) safeCeiling = std::nextafter(safeCeiling, 0.f);
                left = std::clamp(left, -static_cast<double>(safeCeiling), static_cast<double>(safeCeiling));
                right = std::clamp(right, -static_cast<double>(safeCeiling), static_cast<double>(safeCeiling));
                minimumCompressionGain = std::min(minimumCompressionGain, effect.limiterGain);
            } else if (compressorProcessor(node.kind) && wet > 0 && (node.limit || limitMix > 0)) {
                // When leaving Limit, crossfade its retained attenuation out
                // while the normal compressor fades in. No graph/history reset.
                // Bypassing releases the retained limiter with the established
                // wet/dry ramp; intentionally no ceiling guarantee during exit.
                const double retained = 1 + (effect.limiterGain - 1) * (node.limit ? wet : limitMix * wet);
                left *= retained; right *= retained;
            }
            const double peak = std::max(std::abs(left), std::abs(right));
            meters_[i].peakLeft = std::max(meters_[i].peakLeft, static_cast<float>(std::min(std::abs(left), 1.0)));
            meters_[i].peakRight = std::max(meters_[i].peakRight, static_cast<float>(std::min(std::abs(right), 1.0)));
            meters_[i].peak = std::max(meters_[i].peakLeft, meters_[i].peakRight);
            meters_[i].clipped = meters_[i].clipped || peak > 1.0;
            buffers_[i][frame] = {bounded(left, sink), bounded(right, sink)};
        }
        if (dynamicsAudible) {
            if (compressorProcessor(node.kind) || node.kind == GraphKind::ducking) {
                meters_[i].gainReductionDb = static_cast<float>(-20 * std::log10(std::max(1e-6, minimumCompressionGain)));
                meters_[i].detectorDb = static_cast<float>(20 * std::log10(std::max(std::pow(10., -96. / 20), detectorPeak)));
            } else if (advanced_[i]) {
                const double attenuation = std::pow(10., -advanced_[i]->gainReductionDb() / 20.);
                const double audibleGain = 1 + (attenuation - 1) * effects_[i].wet.current;
                meters_[i].gainReductionDb = static_cast<float>(-20 * std::log10(std::max(1e-6, audibleGain)));
                meters_[i].detectorDb = advanced_[i]->detectorDb();
            }
        }
    }
}
void GraphProcessor::reset() noexcept {
    nodeRamps_ = {}; edgeRamps_ = {}; buffers_ = {}; meters_ = {}; effects_ = {};
    for (auto& effect : advanced_) if (effect) effect->reset();
}
void GraphProcessor::inherit(const GraphProcessor& old, const GraphPlan& before, const GraphPlan& after) noexcept {
    for (std::size_t i = 0; i < after.nodeCount; ++i)
        for (std::size_t j = 0; j < before.nodeCount; ++j)
            if (after.nodes[i].id == before.nodes[j].id && after.nodes[i].kind == before.nodes[j].kind) {
                nodeRamps_[i] = old.nodeRamps_[j]; effects_[i] = old.effects_[j];
                if (advanced_[i] && old.advanced_[j]) advanced_[i]->inherit(*old.advanced_[j]);
                if (filterProcessor(after.nodes[i].kind) && after.nodes[i].eq != before.nodes[j].eq) {
                    effects_[i].previousEq = before.nodes[j].eq;
                    effects_[i].previousEqCount = before.nodes[j].eqCount;
                    effects_[i].previousFilters = old.effects_[j].filters;
                    effects_[i].filters = {};
                    effects_[i].eqTransition = 480;
                }
                break;
            }
    for (std::size_t i = 0; i < after.edgeCount; ++i) {
        const auto& edge = after.edges[i];
        for (std::size_t j = 0; j < before.edgeCount; ++j) {
            const auto& previous = before.edges[j];
            if (edge.id == previous.id && edge.control == previous.control && edge.sidechain == previous.sidechain
                && edge.fromPort == previous.fromPort && edge.toPort == previous.toPort
                && after.nodes[edge.from].id == before.nodes[previous.from].id
                && after.nodes[edge.to].id == before.nodes[previous.to].id) {
                edgeRamps_[i] = old.edgeRamps_[j]; break;
            }
        }
    }
}
}
