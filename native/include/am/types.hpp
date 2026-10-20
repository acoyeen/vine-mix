#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>
#include "am/application_types.hpp"

namespace am {
inline constexpr std::uint32_t sampleRate = 48000;
inline constexpr std::size_t quantumFrames = 240;
inline constexpr std::size_t sourceCount = 3;
struct Frame { float left{}; float right{}; };
struct SendControl { bool enabled{}; float gainDb{}; };
struct SourceControl { bool muted{}; SendControl a{}; SendControl b{}; };
struct BusControl { bool muted{}; float gainDb{}; };
struct DeviceBinding {
    std::string id;
    std::string stableId;
    std::string name;
    int channel = 0;
};
inline constexpr std::size_t maximumGraphNodes = 32;
inline constexpr std::size_t maximumGraphEdges = 96;
inline constexpr std::size_t maximumEqBands = 8;
struct EqBand {
    std::string type = "peak";
    bool enabled = true;
    double frequencyHz = 1000;
    double gainDb{};
    double q = 1;
    double resetFrequencyHz{};
};
inline std::vector<EqBand> toneDefaults() {
    return {{"lowShelf", true, 200, 0, .70710678}, {"peak", true, 1000, 0, 1},
        {"highShelf", true, 5000, 0, .70710678}};
}
struct CompressorSettings {
    double thresholdDb = -18, ratio = 4, attackMs = 10, releaseMs = 120, makeupDb{};
    std::string mode = "compress", detector = "internal";
    double ceilingDb = -1;
};
struct DuckingSettings {
    double thresholdDb = -30, amountDb = 18, attackMs = 10, releaseMs = 250;
    std::string mode = "static";
    double ratio = 4;
    bool operator==(const DuckingSettings&) const = default;
};
struct ExpanderSettings {
    std::string mode = "expander";
    double thresholdDb = -40, ratio = 2, attackMs = 5, holdMs = 50, releaseMs = 150, rangeDb = 60, hysteresisDb = 3;
    bool operator==(const ExpanderSettings&) const = default;
};
struct AlignmentDelaySettings { std::uint32_t samples{}; bool operator==(const AlignmentDelaySettings&) const = default; };
struct DenoiserSettings {
    double suppressionDb = -18;
    std::string algorithm = "speex";
    double rnnoiseMix = 1;
    bool operator==(const DenoiserSettings&) const = default;
};
struct DeEsserSettings { double frequencyHz = 6000, thresholdDb = -24, reductionDb = 9; bool operator==(const DeEsserSettings&) const = default; };
struct PlosiveSettings { double frequencyHz = 150, thresholdDb = -24, reductionDb = 12; bool operator==(const PlosiveSettings&) const = default; };
struct ReverbSettings { double mix = .2, decaySeconds = 1.2, damping = .5, preDelayMs = 15; bool operator==(const ReverbSettings&) const = default; };
struct VoiceColorSettings { double tone{}, ambience{}; bool operator==(const VoiceColorSettings&) const = default; };
struct GraphNode {
    std::string id;
    std::string kind = "gain";
    std::string name;
    double x{}, y{};
    float gainDb{};
    bool muted{};
    bool mono{};
    DeviceBinding device{};
    ProcessBinding app{};
    std::vector<EqBand> eqBands{{"lowShelf", true, 80, 0, .70710678, 80},
        {"peak", true, 250, 0, 1, 250}, {"peak", true, 1000, 0, 1, 1000},
        {"peak", true, 4000, 0, 1, 4000}, {"highShelf", true, 10000, 0, .70710678, 10000}};
    CompressorSettings compressor{};
    DuckingSettings ducking{};
    int dynamicsUiVersion{};
    ExpanderSettings expander{};
    AlignmentDelaySettings alignmentDelay{};
    DenoiserSettings denoiser{};
    DeEsserSettings deEsser{};
    PlosiveSettings plosive{};
    ReverbSettings reverb{};
    VoiceColorSettings voiceColor{};
    double balance{};
    bool bypass{};
    bool collapsed{};
};
struct GraphEdge {
    std::string id;
    std::string from;
    std::string to;
    std::string kind = "audio";
    float gainDb{};
    bool muted{};
};
struct AudioGraph {
    std::vector<GraphNode> nodes;
    std::vector<GraphEdge> edges;
    double panX = 20, panY = 20, zoom = 1;
};
struct Configuration {
    int version = 2;
    std::uint64_t revision = 0;
    std::array<DeviceBinding, 2> microphones{};
    DeviceBinding virtualInputCapture{};
    DeviceBinding virtualInputPlayback{};
    std::array<DeviceBinding, 2> mainOutputs{};
    DeviceBinding virtualOutputRender{};
    DeviceBinding virtualOutputCapture{};
    std::array<SourceControl, 3> sources{{
        {false, {false, 0}, {false, 0}},
        {false, {false, 0}, {false, 0}},
        {false, {true, 0}, {false, 0}}
    }};
    BusControl busA{};
    BusControl busB{};
    bool startAtLogin = false;
    AudioGraph graph;
};
struct Endpoint {
    std::string id;
    std::string stableId;
    std::string name;
    std::string direction;
    std::string state;
    bool isVirtual{};
    std::uint32_t channels{};
    std::uint32_t rate{};
    std::string transport;
    // Windows EndpointFormFactor; -1 means the optional property is unavailable.
    // UnknownFormFactor (10) remains distinct from an unavailable property.
    int formFactor = -1;
    std::string description;
    std::string interfaceName;
};
struct PathState {
    std::string state = "Unassigned";
    std::string message;
    std::string endpointId;
    std::uint32_t rate{};
    std::uint32_t channels{};
    std::uint64_t underruns{};
    std::uint64_t overruns{};
    std::uint64_t reconnects{};
    double queueMs{};
    double periodMs{}, bufferMs{}, targetQueueMs{}, resamplerPendingMs{};
};
struct EngineStatus {
    std::uint64_t audioFrames{};
    std::array<float, 3> sourcePeaks{};
    float peakA{};
    float peakB{};
    bool clipA{};
    bool clipB{};
    std::array<PathState, 3> inputs{};
    std::array<PathState, 3> outputs{};
    struct Node {
        std::string id;
        float peak{};
        float peakLeft{};
        float peakRight{};
        int channels{};
        bool clipped{};
        float gainReductionDb{}, detectorDb = -96;
        std::uint64_t dynamicsFrame{};
        std::vector<float> spectrumDb;
        std::uint64_t spectrumFrame{};
        std::uint64_t spectrumRevision{};
        PathState path;
    };
    std::vector<Node> nodes;
};
}
