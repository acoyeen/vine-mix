#include "am/advanced_effects.hpp"
#include <speex/speex_preprocess.h>
#include <rnnoise.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <numbers>
#include <stdexcept>

extern "C" void am_speex_copy(SpeexPreprocessState*, const SpeexPreprocessState*);
namespace am {
namespace {
double clean(double x) noexcept { return std::isfinite(x) ? x : 0; }
double denormal(double x) noexcept { return std::abs(x) < 1e-30 ? 0 : x; }
struct Smooth {
    double value{}, target{}, step{}; unsigned remaining{}; bool initialized{};
    void set(double next) noexcept {
        if (!initialized) { value = target = next; initialized = true; return; }
        if (target == next) return;
        target = next; remaining = 480; step = (target - value) / 480;
    }
    double next() noexcept {
        if (remaining && --remaining == 0) value = target;
        else if (remaining) value += step;
        return value;
    }
};
struct Expander {
    Smooth threshold, ratio, attack, release, floor, hysteresis, gateMix, holdSamples;
    double envelope{}, attenuation = 1;
    std::uint32_t hold{};
    bool open{};
    void configure(const ExpanderSettings& s) noexcept {
        threshold.set(std::pow(10., s.thresholdDb / 20)); ratio.set(s.ratio);
        attack.set(std::exp(-1. / (s.attackMs * .001 * sampleRate)));
        release.set(std::exp(-1. / (s.releaseMs * .001 * sampleRate)));
        floor.set(std::pow(10., -s.rangeDb / 20)); hysteresis.set(std::pow(10., -s.hysteresisDb / 20));
        holdSamples.set(s.holdMs * 48); gateMix.set(s.mode == "gate" ? 1 : 0);
    }
    Frame process(Frame input) noexcept {
        const double peak = std::max(std::abs(input.left), std::abs(input.right));
        // Stereo-linked peak detector: fast enough to reopen on speech onset,
        // with a 20ms decay so waveform zero crossings cannot chatter the gate.
        constexpr double detectorAttack = .9591894571091382; // 0.5ms
        constexpr double detectorRelease = .9989588756797245; // 20ms
        envelope = denormal(peak + (peak > envelope ? detectorAttack : detectorRelease) * (envelope - peak));
        const double level = threshold.next(), exponent = ratio.next() - 1, minimum = floor.next();
        const double closeLevel = level * hysteresis.next(), amount = gateMix.next();
        const auto duration = static_cast<std::uint32_t>(std::lround(holdSamples.next()));
        if (envelope >= level) { open = true; hold = duration; }
        else if (open && envelope >= closeLevel) hold = duration;
        else if (hold) --hold;
        else open = false;
        const double expanded = std::max(minimum, std::pow(std::min(1., envelope / level), exponent));
        const double gated = open ? 1 : minimum;
        const double target = expanded + (gated - expanded) * amount;
        const double opening = attack.next(), closing = release.next();
        attenuation = target + (target > attenuation ? opening : closing) * (attenuation - target);
        return {static_cast<float>(input.left * attenuation), static_cast<float>(input.right * attenuation)};
    }
};
struct AlignmentDelay {
    static constexpr std::size_t capacity = 96001;
    std::array<Frame, capacity> history{};
    std::size_t position{};
    std::uint32_t current{}, destination{}, requested{}, remaining{};
    bool configured{};
    void configure(std::uint32_t samples) noexcept {
        requested = samples;
        if (!configured) { current = destination = requested; configured = true; }
    }
    Frame process(Frame input) noexcept {
        history[position] = input;
        if (!remaining && current != requested) { destination = requested; remaining = 480; }
        const auto at = [&](std::uint32_t delay) { return history[(position + capacity - delay) % capacity]; };
        Frame result = at(current);
        if (remaining) {
            const auto next = at(destination);
            const double mix = 1 - static_cast<double>(--remaining) / 480;
            result = {static_cast<float>(result.left + (next.left - result.left) * mix),
                static_cast<float>(result.right + (next.right - result.right) * mix)};
            if (!remaining) current = destination;
        }
        position = (position + 1) % capacity;
        return result;
    }
};
struct SpeexDenoise {
    static constexpr std::size_t block = 480, latency = 960;
    SpeexPreprocessState* active[2]{};
    SpeexPreprocessState* pristine[2]{};
    std::array<std::array<spx_int16_t, block>, 2> input{}, output{};
    std::array<Frame, latency> dry{};
    std::size_t position{}, dryPosition{};
    int suppression = -18, targetSuppression = -18;
    SpeexDenoise() {
        try { for (int channel = 0; channel < 2; ++channel) {
            active[channel] = speex_preprocess_state_init(block, sampleRate);
            pristine[channel] = speex_preprocess_state_init(block, sampleRate);
            if (!active[channel] || !pristine[channel]) throw std::runtime_error("Cannot allocate denoiser state.");
            int off = 0;
            for (auto* state : {active[channel], pristine[channel]}) {
                speex_preprocess_ctl(state, SPEEX_PREPROCESS_SET_AGC, &off);
                speex_preprocess_ctl(state, SPEEX_PREPROCESS_SET_DEREVERB, &off);
                // VAD already defaults off; its setter emits a deprecation warning.
                speex_preprocess_ctl(state, SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, &suppression);
            }
        } } catch (...) {
            for (auto* state : active) if (state) speex_preprocess_state_destroy(state);
            for (auto* state : pristine) if (state) speex_preprocess_state_destroy(state);
            throw;
        }
    }
    ~SpeexDenoise() {
        for (auto* state : active) if (state) speex_preprocess_state_destroy(state);
        for (auto* state : pristine) if (state) speex_preprocess_state_destroy(state);
    }
    void reset() noexcept {
        for (int channel = 0; channel < 2; ++channel) am_speex_copy(active[channel], pristine[channel]);
        input = {}; output = {}; dry = {}; position = dryPosition = 0; suppression = targetSuppression = -18;
    }
    void inherit(const SpeexDenoise& old) noexcept {
        for (int channel = 0; channel < 2; ++channel) am_speex_copy(active[channel], old.active[channel]);
        input = old.input; output = old.output; dry = old.dry;
        position = old.position; dryPosition = old.dryPosition; suppression = old.suppression; targetSuppression = old.targetSuppression;
    }
    AdvancedEffect::Result process(Frame sample) noexcept {
        Frame delayed = dry[dryPosition]; dry[dryPosition] = sample;
        dryPosition = (dryPosition + 1) % latency;
        Frame processed{output[0][position] / 32768.f, output[1][position] / 32768.f};
        input[0][position] = static_cast<spx_int16_t>(std::lround(std::clamp(clean(sample.left), -1., 32767. / 32768) * 32768));
        input[1][position] = static_cast<spx_int16_t>(std::lround(std::clamp(clean(sample.right), -1., 32767. / 32768) * 32768));
        if (++position == block) {
            position = 0;
            // A live reduction edit advances at most 1 dB per 10 ms analysis
            // frame. Speex's overlap/add handles the within-frame transition.
            if (suppression != targetSuppression) {
                suppression += std::clamp(targetSuppression-suppression,-1,1);
                for (auto* state : active) speex_preprocess_ctl(state,SPEEX_PREPROCESS_SET_NOISE_SUPPRESS,&suppression);
            }
            for (int channel = 0; channel < 2; ++channel) {
                output[channel] = input[channel];
                speex_preprocess_run(active[channel], output[channel].data());
            }
        }
        return {processed, delayed};
    }
};
struct NeuralDenoise {
    static constexpr std::size_t block = 480, latency = 1440;
    using State = std::unique_ptr<DenoiseState, decltype(&rnnoise_destroy)>;
    std::array<State, 2> active{{{nullptr, rnnoise_destroy}, {nullptr, rnnoise_destroy}}};
    std::array<State, 2> pristine{{{nullptr, rnnoise_destroy}, {nullptr, rnnoise_destroy}}};
    const std::size_t stateSize = static_cast<std::size_t>(rnnoise_get_size());
    std::array<std::array<float, block>, 2> input{}, output{};
    std::array<Frame, latency> dry{};
    std::size_t position{}, dryPosition{};
    NeuralDenoise() {
        if (rnnoise_get_frame_size() != block) throw std::runtime_error("Unexpected RNNoise frame size.");
        for (int channel = 0; channel < 2; ++channel) {
            active[channel].reset(rnnoise_create(nullptr));
            pristine[channel].reset(rnnoise_create(nullptr));
            if (!active[channel] || !pristine[channel]) throw std::runtime_error("Cannot allocate RNNoise state.");
        }
    }
    // Audited for the pinned v0.2/default model: DenoiseState owns inline
    // histories and read-only pointers into the embedded static weights only.
    // Never use this snapshot copy with an external model or an unaudited update.
    void reset() noexcept {
        for (int channel = 0; channel < 2; ++channel)
            std::memcpy(active[channel].get(), pristine[channel].get(), stateSize);
        input = {}; output = {}; dry = {}; position = dryPosition = 0;
    }
    void inherit(const NeuralDenoise& old) noexcept {
        for (int channel = 0; channel < 2; ++channel)
            std::memcpy(active[channel].get(), old.active[channel].get(), stateSize);
        input = old.input; output = old.output; dry = old.dry;
        position = old.position; dryPosition = old.dryPosition;
    }
    AdvancedEffect::Result process(Frame sample) noexcept {
        Frame delayed = dry[dryPosition]; dry[dryPosition] = sample;
        dryPosition = (dryPosition + 1) % latency;
        Frame processed{output[0][position] / 32768.f, output[1][position] / 32768.f};
        input[0][position] = static_cast<float>(std::clamp(clean(sample.left), -1., 1.) * 32768);
        input[1][position] = static_cast<float>(std::clamp(clean(sample.right), -1., 1.) * 32768);
        if (++position == block) {
            position = 0;
            for (int channel = 0; channel < 2; ++channel)
                rnnoise_process_frame(active[channel].get(), output[channel].data(), input[channel].data());
        }
        return {processed, delayed};
    }
};
struct Denoise {
    SpeexDenoise speex;
    NeuralDenoise neural;
    Smooth algorithmMix, neuralMix;
    bool targetNeural{};
    unsigned warmup{};
    void configure(const DenoiserSettings& setting, bool first) noexcept {
        const bool next = setting.algorithm == "rnnoise";
        neuralMix.set(setting.rnnoiseMix);
        speex.targetSuppression = static_cast<int>(std::lround(setting.suppressionDb));
        if (first) {
            targetNeural = next; algorithmMix.set(next ? 1 : 0);
            speex.suppression = speex.targetSuppression;
            for (auto* state : speex.active)
                speex_preprocess_ctl(state, SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, &speex.suppression);
        } else if (targetNeural != next) {
            targetNeural = next;
            if (next && algorithmMix.value == 0 && !algorithmMix.remaining) { neural.reset(); warmup = 4800; }
            else if (next) { warmup = 0; algorithmMix.set(1); }
            else { warmup = 0; algorithmMix.set(0); }
        }
    }
    void reset() noexcept {
        speex.reset(); neural.reset(); algorithmMix = {}; neuralMix = {}; targetNeural = false; warmup = 0;
    }
    void inherit(const Denoise& old) noexcept {
        speex.inherit(old.speex); neural.inherit(old.neural);
        algorithmMix = old.algorithmMix; neuralMix = old.neuralMix; targetNeural = old.targetNeural; warmup = old.warmup;
    }
    AdvancedEffect::Result process(Frame sample) noexcept {
        const auto spectral = speex.process(sample);
        const double strength = neuralMix.next();
        if (!targetNeural && algorithmMix.value == 0 && !algorithmMix.remaining) return spectral;
        const auto cleaned = neural.process(sample);
        if (warmup && --warmup == 0) algorithmMix.set(1);
        const double blend = algorithmMix.next();
        const auto mix = [](Frame a, Frame b, double amount) {
            if (amount == 0) return a;
            if (amount == 1) return b;
            return Frame{static_cast<float>(a.left + (static_cast<double>(b.left) - a.left) * amount),
                static_cast<float>(a.right + (static_cast<double>(b.right) - a.right) * amount)};
        };
        return {mix(spectral.wet, mix(cleaned.dry, cleaned.wet, strength), blend),
            mix(spectral.dry, cleaned.dry, blend)};
    }
};
struct BandReduction {
    struct Svf {
        double integrator1{}, integrator2{};
        std::array<double, 3> process(double input, double g) noexcept {
            constexpr double k = 1.4142135623730951;
            const double a1 = 1 / (1 + g * (g + k));
            const double a2 = g * a1, a3 = g * a2;
            const double v3 = input - integrator2;
            const double band = a1 * integrator1 + a2 * v3;
            const double low = integrator2 + a2 * integrator1 + a3 * v3;
            integrator1 = denormal(2 * band - integrator1);
            integrator2 = denormal(2 * low - integrator2);
            return {low, band, input - k * band - low};
        }
    };
    std::array<Svf, 2> detector, shelf;
    double envelope{}, attenuation = 1;
    Smooth frequencyG, threshold, floor;
    bool high{};
    Frame process(Frame input) noexcept {
        const double g = frequencyG.next();
        const auto leftDetector = detector[0].process(input.left, g);
        const auto rightDetector = detector[1].process(input.right, g);
        const auto index = high ? 2u : 0u;
        const double peak = std::max(std::abs(leftDetector[index]), std::abs(rightDetector[index]));
        // Stereo-linked sidechain prevents the image shifting on consonants.
        constexpr double detectorAttack = .9591894571091382; // 0.5 ms
        constexpr double detectorRelease = .9989588756797245; // 20 ms
        constexpr double gainAttack = .9793821813312401; // 1 ms
        constexpr double gainRelease = .9995834201268338; // 50 ms
        envelope = peak + (peak > envelope ? detectorAttack : detectorRelease) * (envelope - peak);
        const double thresholdValue = threshold.next(), minimum = floor.next();
        const double desired = envelope > thresholdValue ? std::max(minimum, std::pow(thresholdValue / envelope, .75)) : 1;
        attenuation = desired + (desired < attenuation ? gainAttack : gainRelease) * (attenuation - desired);
        // Dynamic second-order shelf, using the topology-preserving SVF
        // formulation described by Andrew Simper (Cytomic). The previous
        // first-order subtract/recombine path could never cut more than 3 dB
        // at its crossover, regardless of the requested range.
        // A is the square root of shelf amplitude; Frequency is the midpoint
        // (half the shelf's dB reduction). At unity the output is exactly dry.
        constexpr double k = 1.4142135623730951;
        const double A = std::sqrt(attenuation), rootA = std::sqrt(A);
        const double shelfG = high ? g * rootA : g / rootA;
        const double m0 = high ? attenuation : 1;
        const double m1 = high ? k * (1 - A) * A : k * (A - 1);
        const double m2 = high ? 1 - attenuation : attenuation - 1;
        const auto left = shelf[0].process(input.left, shelfG);
        const auto right = shelf[1].process(input.right, shelfG);
        return {static_cast<float>(m0 * input.left + m1 * left[1] + m2 * left[0]),
            static_cast<float>(m0 * input.right + m1 * right[1] + m2 * right[0])};
    }
};
struct VoiceColor {
    struct Band {
        std::array<Smooth, 5> coefficients;
        struct History { double z1{}, z2{}; };
        std::array<History, 2> history{};
        void configure(const GraphPlan::Biquad& c) noexcept {
            coefficients[0].set(c.b0); coefficients[1].set(c.b1); coefficients[2].set(c.b2);
            coefficients[3].set(c.a1); coefficients[4].set(c.a2);
        }
        void process(double& left, double& right) noexcept {
            const double b0 = coefficients[0].next(), b1 = coefficients[1].next(), b2 = coefficients[2].next();
            const double a1 = coefficients[3].next(), a2 = coefficients[4].next();
            const auto filter = [&](double input, History& state) {
                const double output = b0 * input + state.z1;
                state.z1 = denormal(b1 * input - a1 * output + state.z2);
                state.z2 = denormal(b2 * input - a2 * output);
                return output;
            };
            left = filter(left, history[0]); right = filter(right, history[1]);
        }
    };
    std::array<Band, 4> bands;
    Smooth gain, character;
    static constexpr std::size_t doubleSize = 913; // Longest tap: 19 ms at 48 kHz.
    std::array<Frame, doubleSize> doubleHistory{};
    std::size_t doublePosition{};
    double envelope{}, densityEnvelope{}, attenuation = 1;
    void configure(const GraphPlan::Node& node) noexcept {
        for (std::size_t i = 0; i < bands.size(); ++i) bands[i].configure(node.eq[i]);
        gain.set(node.voiceColorGain);
        character.set(std::pow(node.voiceColor.ambience, 1.3));
    }
    Frame process(Frame input) noexcept {
        double left = input.left, right = input.right;
        for (auto& band : bands) band.process(left, right);
        const double trim = gain.next(), amount = character.next();
        left *= trim; right *= trim;
        // Linked peak envelope and linked gain keep stereo balance stable.
        // The soft knee has a continuous slope and no lookahead; this is a
        // vocal compressor, not a sample-peak ceiling or an AGC.
        constexpr double detectorAttack = .9591894571091382; // 0.5 ms
        constexpr double detectorRelease = .9995834201268338; // 50 ms
        constexpr double gainAttack = .9793821813312401; // 1 ms
        constexpr double gainRelease = .999826403958668; // 120 ms
        constexpr double threshold = -28, knee = 6, slope = 1 - 1 / 4.5;
        const double peak = std::max(std::abs(left), std::abs(right));
        envelope = denormal(peak + (peak > envelope ? detectorAttack : detectorRelease) * (envelope - peak));
        const double above = 20 * std::log10(std::max(envelope, 1e-12)) - threshold;
        const double reduction = above <= -knee / 2 ? 0 : above >= knee / 2
            ? slope * above : slope * (above + knee / 2) * (above + knee / 2) / (2 * knee);
        const double target = std::pow(10., -std::min(18., reduction) / 20);
        attenuation = target + (target < attenuation ? gainAttack : gainRelease) * (attenuation - target);
        constexpr double makeup = 1.5848931924611136; // Fixed 4 dB; blended by Character.
        // Parallel compression keeps the direct vocal body even when strong
        // speech drives substantial reduction. Its bounded wet share avoids
        // making a louder microphone much quieter at the Broadcast edge.
        const double compression = 1 + .4 * amount * (attenuation * makeup - 1);
        left *= compression; right *= compression;
        // Envelope-relative density remains audible on quiet or previously
        // compressed voices. Unlike AGC it never raises input gain: a bounded
        // parallel soft saturation only rounds peaks, using one stereo-linked
        // gain to retain channel balance. Fade below -80..-60 dBFS so the
        // normalization cannot turn tiny noise or denormals into an effect.
        constexpr double densityAttack = .9979188352992993; // 10 ms
        constexpr double densityRelease = .999826403958668; // 120 ms
        const double densityPeak = std::max(std::abs(left), std::abs(right));
        densityEnvelope = denormal(densityPeak + (densityPeak > densityEnvelope ? densityAttack : densityRelease)
            * (densityEnvelope - densityPeak));
        const double activity = std::clamp((densityEnvelope - .0001) / .0009, 0., 1.);
        const double activeMix = .5 * amount * activity * activity * (3 - 2 * activity);
        const double normalized = 2.5 * densityPeak / std::max(densityEnvelope, .0001);
        const double rounded = normalized > 1e-8 ? std::tanh(normalized) / normalized : 1.;
        const double densityGain = 1 + activeMix * (rounded - 1);
        left *= densityGain; right *= densityGain;
        // A low-level, feedback-free double adds thickness without a large
        // echo or variable-delay pitch movement. Convex mixing bounds gain.
        const auto leftTap = (doublePosition + doubleSize - 576) % doubleSize;
        const auto rightTap = (doublePosition + doubleSize - 912) % doubleSize;
        const double doubleMix = .12 * amount;
        const double doubledLeft = left * (1 - doubleMix) + doubleHistory[leftTap].left * doubleMix;
        const double doubledRight = right * (1 - doubleMix) + doubleHistory[rightTap].right * doubleMix;
        doubleHistory[doublePosition] = {static_cast<float>(left), static_cast<float>(right)};
        doublePosition = (doublePosition + 1) % doubleSize;
        return {static_cast<float>(doubledLeft), static_cast<float>(doubledRight)};
    }
};
struct Reverb {
    static constexpr std::size_t combSize = 2048, allpassSize = 640, preSize = 6042;
    static constexpr std::array<int, 8> combLengths{1215,1293,1390,1476,1547,1623,1695,1760};
    static constexpr std::array<int, 4> allpassLengths{605,480,371,245};
    struct Channel {
        std::array<std::array<float, combSize>, 8> comb{};
        std::array<std::array<float, allpassSize>, 4> allpass{};
        std::array<std::size_t, 8> combPosition{};
        std::array<std::size_t, 4> allpassPosition{};
        std::array<double, 8> low{};
    };
    std::array<Channel, 2> channels{};
    std::array<Frame, preSize> predelay{};
    std::size_t prePosition{};
    Smooth mix, damping, preSamples;
    std::array<std::array<Smooth, 8>, 2> feedback;
    Frame process(Frame input) noexcept {
        predelay[prePosition] = input;
        const double delay = preSamples.next();
        const auto whole = static_cast<std::size_t>(delay);
        const double fraction = delay - whole;
        const auto first = (prePosition + preSize - whole) % preSize;
        const auto second = (first + preSize - 1) % preSize;
        Frame delayed{static_cast<float>(predelay[first].left * (1-fraction) + predelay[second].left * fraction),
            static_cast<float>(predelay[first].right * (1-fraction) + predelay[second].right * fraction)};
        prePosition = (prePosition + 1) % preSize;
        const double damp = damping.next(), amount = mix.next();
        double result[2]{};
        for (std::size_t side = 0; side < 2; ++side) {
            auto& c = channels[side];
            const double excitation = std::clamp(side == 0 ? .75 * delayed.left + .25 * delayed.right : .75 * delayed.right + .25 * delayed.left, -16., 16.);
            double value{};
            for (std::size_t band = 0; band < 8; ++band) {
                const auto length = static_cast<std::size_t>(combLengths[band] + side * 25);
                auto& position = c.combPosition[band];
                const double previous = c.comb[band][position];
                c.low[band] = denormal(previous * (1-damp) + c.low[band] * damp);
                const double feedbackGain = feedback[side][band].next();
                // A lossless two-port scattering junction, with the damped
                // delay loop as its second port. The wet transfer is
                // (L(z)*z^-D-g)/(1-g*L(z)*z^-D): <= unity in magnitude because
                // the one-pole loop filter is passive. Unlike unnormalised
                // feedback combs, longer decay cannot multiply steady energy.
                const double coupling = std::sqrt(1-feedbackGain*feedbackGain);
                c.comb[band][position] = static_cast<float>(excitation * coupling + c.low[band] * feedbackGain);
                position = (position + 1) % length;
                value += (c.low[band] * coupling - excitation * feedbackGain) * .125;
            }
            for (std::size_t band = 0; band < 4; ++band) {
                const auto length = static_cast<std::size_t>(allpassLengths[band] + side * 25);
                auto& position = c.allpassPosition[band];
                const double previous = c.allpass[band][position];
                // True Schroeder allpass: H(z)=(z^-D-g)/(1-g*z^-D).
                // The former Freeverb approximation boosted frequencies in
                // each of four diffusion stages instead of preserving energy.
                const double next = previous - value * .5;
                c.allpass[band][position] = static_cast<float>(denormal(value + next * .5));
                position = (position + 1) % length; value = next;
            }
            result[side] = value;
        }
        return {static_cast<float>(input.left * (1-amount) + result[0] * amount),
            static_cast<float>(input.right * (1-amount) + result[1] * amount)};
    }
};
}
struct AdvancedEffect::State {
    GraphKind kind;
    std::unique_ptr<Denoise> denoise;
    std::unique_ptr<Reverb> reverb;
    std::unique_ptr<VoiceColor> voiceColor;
    std::unique_ptr<AlignmentDelay> alignmentDelay;
    Expander expander;
    BandReduction band;
    DenoiserSettings denoiserSettings;
    DeEsserSettings deEsserSettings;
    PlosiveSettings plosiveSettings;
    ReverbSettings reverbSettings;
    VoiceColorSettings voiceColorSettings;
    ExpanderSettings expanderSettings;
    AlignmentDelaySettings alignmentDelaySettings;
    bool configured{};
    explicit State(GraphKind value) : kind(value) {
        if (kind == GraphKind::denoiser) denoise = std::make_unique<Denoise>();
        else if (kind == GraphKind::reverb || kind == GraphKind::voiceColor) reverb = std::make_unique<Reverb>();
        if (kind == GraphKind::voiceColor) voiceColor = std::make_unique<VoiceColor>();
        if (kind == GraphKind::alignmentDelay) alignmentDelay = std::make_unique<AlignmentDelay>();
        band.high = kind == GraphKind::deEsser;
    }
};
bool AdvancedEffect::supports(GraphKind kind) noexcept {
    return kind == GraphKind::denoiser || kind == GraphKind::deEsser || kind == GraphKind::plosive
        || kind == GraphKind::reverb || kind == GraphKind::voiceColor || kind == GraphKind::expander || kind == GraphKind::gate || kind == GraphKind::alignmentDelay;
}
AdvancedEffect::AdvancedEffect(GraphKind kind) : state_(std::make_unique<State>(kind)) {}
AdvancedEffect::~AdvancedEffect() = default;
void AdvancedEffect::configure(const GraphPlan::Node& node) noexcept {
    if (state_->configured && state_->denoiserSettings == node.denoiser && state_->deEsserSettings == node.deEsser
        && state_->plosiveSettings == node.plosive && state_->reverbSettings == node.reverb
        && state_->voiceColorSettings == node.voiceColor && state_->expanderSettings == node.expander
        && state_->alignmentDelaySettings == node.alignmentDelay) return;
    const bool firstConfiguration = !state_->configured;
    state_->denoiserSettings = node.denoiser; state_->deEsserSettings = node.deEsser;
    state_->plosiveSettings = node.plosive; state_->reverbSettings = node.reverb; state_->configured = true;
    state_->voiceColorSettings = node.voiceColor;
    state_->expanderSettings = node.expander; state_->alignmentDelaySettings = node.alignmentDelay;
    if (state_->kind == GraphKind::expander || state_->kind == GraphKind::gate) { state_->expander.configure(node.expander); return; }
    if (state_->alignmentDelay) { state_->alignmentDelay->configure(node.alignmentDelay.samples); return; }
    if (state_->voiceColor) state_->voiceColor->configure(node);
    if (state_->denoise) {
        state_->denoise->configure(node.denoiser, firstConfiguration);
    } else if (state_->reverb) {
        auto& effect = *state_->reverb; const auto& setting = node.reverb;
        effect.mix.set(setting.mix); effect.damping.set(setting.damping * .9);
        // The scattering sections have a feed-forward term. Preserve the
        // network's original first-reflection onset before the user pre-delay.
        effect.preSamples.set(setting.preDelayMs * 48 + 1215);
        for (std::size_t side = 0; side < 2; ++side) for (std::size_t band = 0; band < 8; ++band)
            effect.feedback[side][band].set(std::pow(.001, (Reverb::combLengths[band] + side * 25) / (sampleRate * setting.decaySeconds)));
    } else {
        const double frequency = state_->band.high ? node.deEsser.frequencyHz : node.plosive.frequencyHz;
        const double threshold = state_->band.high ? node.deEsser.thresholdDb : node.plosive.thresholdDb;
        const double reduction = state_->band.high ? node.deEsser.reductionDb : node.plosive.reductionDb;
        const double g = std::tan(std::numbers::pi * frequency / sampleRate);
        state_->band.frequencyG.set(g);
        state_->band.threshold.set(std::pow(10., threshold / 20)); state_->band.floor.set(std::pow(10., -reduction / 20));
    }
}
AdvancedEffect::Result AdvancedEffect::process(Frame input) noexcept {
    input.left = static_cast<float>(clean(input.left)); input.right = static_cast<float>(clean(input.right));
    if (state_->denoise) return state_->denoise->process(input);
    if (state_->alignmentDelay) return {state_->alignmentDelay->process(input), input};
    if (state_->kind == GraphKind::expander || state_->kind == GraphKind::gate) return {state_->expander.process(input), input};
    if (state_->voiceColor) return {state_->reverb->process(state_->voiceColor->process(input)), input};
    return {state_->reverb ? state_->reverb->process(input) : state_->band.process(input), input};
}
void AdvancedEffect::inherit(const AdvancedEffect& previous) noexcept {
    if (state_->kind != previous.state_->kind) return;
    if (state_->denoise) state_->denoise->inherit(*previous.state_->denoise);
    if (state_->reverb) *state_->reverb = *previous.state_->reverb;
    if (state_->voiceColor) *state_->voiceColor = *previous.state_->voiceColor;
    if (state_->alignmentDelay) *state_->alignmentDelay = *previous.state_->alignmentDelay;
    state_->expander = previous.state_->expander;
    state_->band = previous.state_->band;
    state_->denoiserSettings = previous.state_->denoiserSettings; state_->deEsserSettings = previous.state_->deEsserSettings;
    state_->plosiveSettings = previous.state_->plosiveSettings; state_->reverbSettings = previous.state_->reverbSettings;
    state_->voiceColorSettings = previous.state_->voiceColorSettings;
    state_->expanderSettings = previous.state_->expanderSettings; state_->alignmentDelaySettings = previous.state_->alignmentDelaySettings;
    state_->configured = previous.state_->configured;
}
void AdvancedEffect::reset() noexcept {
    if (state_->denoise) state_->denoise->reset();
    if (state_->reverb) *state_->reverb = {};
    if (state_->voiceColor) *state_->voiceColor = {};
    if (state_->alignmentDelay) {
        // Avoid a 768KiB temporary on the realtime thread's stack.
        state_->alignmentDelay->history.fill({}); state_->alignmentDelay->position = 0;
        state_->alignmentDelay->current = state_->alignmentDelay->destination = state_->alignmentDelay->requested = state_->alignmentDelay->remaining = 0;
        state_->alignmentDelay->configured = false;
    }
    state_->expander = {};
    state_->band = {}; state_->band.high = state_->kind == GraphKind::deEsser;
    state_->configured = false;
}
float AdvancedEffect::gainReductionDb() const noexcept {
    if (state_->kind == GraphKind::expander || state_->kind == GraphKind::gate) return static_cast<float>(-20 * std::log10(std::clamp(state_->expander.attenuation, 1e-12, 1.)));
    if (state_->kind != GraphKind::deEsser && state_->kind != GraphKind::plosive) return 0;
    return static_cast<float>(-20 * std::log10(std::clamp(state_->band.attenuation, 1e-12, 1.)));
}
float AdvancedEffect::detectorDb() const noexcept {
    if (state_->kind == GraphKind::expander || state_->kind == GraphKind::gate) return static_cast<float>(20 * std::log10(std::max(state_->expander.envelope, 1.584893192461114e-5)));
    if (state_->kind != GraphKind::deEsser && state_->kind != GraphKind::plosive) return -96;
    return static_cast<float>(20 * std::log10(std::max(state_->band.envelope, 1.584893192461114e-5)));
}
}
