#pragma once
#include "am/types.hpp"
#include <array>
#include <span>
#include <memory>

namespace am {
enum class GraphKind { microphone, virtualInput, appInput, mix, gain, output, virtualOutput, control,
    eq, tone, compressor, pan, mono, reroute, highPass, lowPass, denoiser, deEsser, plosive, reverb, voiceColor, expander, alignmentDelay, limiter, ducking, gate,
    channelSwap, stereoWidth, stereoPan, crossfade, stereoCombine, stereoSplit };
enum class GraphPort { audio, left, right, a, b };
bool isSource(GraphKind kind) noexcept;
bool isSink(GraphKind kind) noexcept;

struct GraphPlan {
    struct Biquad { double b0 = 1, b1{}, b2{}, a1{}, a2{}; bool operator==(const Biquad&) const = default; };
    struct Node {
        std::string id;
        GraphKind kind{};
        float gainDb{};
        float controlMultiplier = -1;
        bool muted{};
        bool mono{};
        bool selectedMicrophoneChannel{};
        int controlEdge = -1;
        int sidechainEdge = -1;
        std::array<std::size_t, maximumGraphEdges> incoming{};
        std::size_t incomingCount{};
        std::array<Biquad, maximumEqBands> eq{};
        std::size_t eqCount{};
        double threshold = .12589254, ratio = 4, attack = .99791884, release = .9998264, makeup = 1;
        bool limit{};
        double ceiling = .8912509381337456;
        double duckingAmount = 1, duckingCloseThreshold{};
        bool dynamicDucking{};
        ExpanderSettings expander{};
        AlignmentDelaySettings alignmentDelay{};
        float panLeft = 1, panRight = 1;
        float stereoWidth = 1, position{}, fadePosition{};
        bool equalPower = true;
        bool bypass{};
        DenoiserSettings denoiser{};
        DeEsserSettings deEsser{};
        PlosiveSettings plosive{};
        ReverbSettings reverb{};
        VoiceColorSettings voiceColor{};
        double voiceColorGain = 1;
        bool operator==(const Node&) const = default;
    };
    struct Edge {
        std::string id;
        std::size_t from{}, to{};
        bool control{};
        bool sidechain{};
        float gainDb{};
        bool muted{};
        GraphPort fromPort = GraphPort::audio, toPort = GraphPort::audio;
        bool operator==(const Edge&) const = default;
    };
    std::array<Node, maximumGraphNodes> nodes;
    std::array<Edge, maximumGraphEdges> edges;
    std::array<std::size_t, maximumGraphNodes> order{};
    std::size_t nodeCount{}, edgeCount{};
    bool operator==(const GraphPlan&) const = default;
};

// Validation and compilation run on the control thread. A plan has bounded,
// indexed routing; processing never searches a dictionary or grows storage.
GraphPlan compileGraph(const AudioGraph& graph);
bool sameGraphTopology(const GraphPlan& a, const GraphPlan& b) noexcept;
// Signal layout, not hardware output format. Capture counts come from the
// observed endpoint; 0 means unknown. This runs outside the audio callback.
using GraphChannels = std::array<int, maximumGraphNodes>;
GraphChannels graphSignalChannels(const GraphPlan& plan, const GraphChannels& captureChannels) noexcept;

class AdvancedEffect;
class GraphProcessor {
public:
    GraphProcessor();
    ~GraphProcessor();
    // Construct library FFT plans/delay lines before publication, never on the
    // audio thread. Ordinary graphs need no auxiliary allocation.
    void prepare(const GraphPlan& plan);
    using Inputs = std::array<std::span<const Frame>, maximumGraphNodes>;
    struct Meter {
        float peak{}, peakLeft{}, peakRight{};
        bool clipped{};
        // Actual dynamics state, independent of output level/make-up gain.
        float gainReductionDb{}, detectorDb = -96;
    };
    void process(const GraphPlan& plan, const Inputs& inputs) noexcept;
    std::span<const Frame> output(std::size_t index) const noexcept { return buffers_[index]; }
    const Meter& meter(std::size_t index) const noexcept { return meters_[index]; }
    void reset() noexcept;
    // Only the audio thread copies ramp state when adopting an already-built
    // plan. IDs survive reordering; unchanged paths retain their exact level.
    void inherit(const GraphProcessor& old, const GraphPlan& before, const GraphPlan& after) noexcept;
private:
    struct Ramp {
        float current{}, target{}, step{};
        std::uint32_t remaining{};
        void set(float value) noexcept;
        float next() noexcept;
    };
    struct FilterState { double z1{}, z2{}; };
    struct Effects {
        std::array<std::array<FilterState, 2>, maximumEqBands> filters{};
        std::array<std::array<FilterState, 2>, maximumEqBands> previousFilters{};
        std::array<GraphPlan::Biquad, maximumEqBands> previousEq{};
        std::size_t previousEqCount{};
        std::uint32_t eqTransition{};
        double compressionGain = 1, limiterGain = 1;
        double duckingGain = 1, duckingDetector{};
        bool duckingActive{};
        Ramp wet{}, panLeft{}, panRight{}, makeup{}, mono{}, limitMix{};
        Ramp width{}, position{}, fadePosition{}, fadeCurve{};
    };
    std::array<std::array<Frame, quantumFrames>, maximumGraphNodes> buffers_{};
    std::array<Ramp, maximumGraphNodes> nodeRamps_{};
    std::array<Ramp, maximumGraphEdges> edgeRamps_{};
    std::array<Meter, maximumGraphNodes> meters_{};
    std::array<Effects, maximumGraphNodes> effects_{};
    std::array<std::unique_ptr<AdvancedEffect>, maximumGraphNodes> advanced_{};
};
}
