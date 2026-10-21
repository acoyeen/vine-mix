#pragma once
#include "am/types.hpp"
#include <filesystem>
#include <nlohmann/json.hpp>
#include <string>

namespace am {
using Json = nlohmann::json;
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(SendControl, enabled, gainDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(SourceControl, muted, a, b)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(BusControl, muted, gainDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(DeviceBinding, id, stableId, name, channel)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(ProcessBinding, processId, executablePath, name)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(ApplicationInfo, processId, executablePath, name, hasAudioSession)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(EqBand, type, enabled, frequencyHz, gainDb, q, resetFrequencyHz)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(CompressorSettings, thresholdDb, ratio, attackMs, releaseMs, makeupDb, mode, detector, ceilingDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(DuckingSettings, thresholdDb, amountDb, attackMs, releaseMs, mode, ratio)
inline void from_json(const Json& value, DuckingSettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Ducking settings must be an object.");
    settings.thresholdDb = value.value("thresholdDb", -30.); settings.amountDb = value.value("amountDb", 18.);
    settings.attackMs = value.value("attackMs", 10.); settings.releaseMs = value.value("releaseMs", 250.);
    settings.mode = value.value("mode", std::string("static")); settings.ratio = value.value("ratio", 4.);
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(ExpanderSettings, mode, thresholdDb, ratio, attackMs, holdMs, releaseMs, rangeDb, hysteresisDb)
inline void from_json(const Json& value, ExpanderSettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Expander settings must be an object.");
    settings.mode = value.value("mode", std::string("expander"));
    settings.thresholdDb = value.value("thresholdDb", -40.); settings.ratio = value.value("ratio", 2.);
    settings.attackMs = value.value("attackMs", 5.); settings.holdMs = value.value("holdMs", 50.);
    settings.releaseMs = value.value("releaseMs", 150.); settings.rangeDb = value.value("rangeDb", 60.);
    settings.hysteresisDb = value.value("hysteresisDb", 3.);
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(AlignmentDelaySettings, samples)
inline void from_json(const Json& value, AlignmentDelaySettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Alignment Delay settings must be an object.");
    const auto samples = value.value("samples", Json(0));
    if (!samples.is_number_integer() || samples.get<double>() < 0 || samples.get<double>() > 96000)
        throw std::runtime_error("Alignment Delay must contain 0 to 96000 whole samples.");
    settings.samples = samples.get<std::uint32_t>();
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(DenoiserSettings, suppressionDb, algorithm, rnnoiseMix)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(DeEsserSettings, frequencyHz, thresholdDb, reductionDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(PlosiveSettings, frequencyHz, thresholdDb, reductionDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(ReverbSettings, mix, decaySeconds, damping, preDelayMs)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(VoiceColorSettings, tone, ambience)
inline void from_json(const Json& value, VoiceColorSettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Voice Color settings must be an object.");
    settings.tone = value.value("tone", 0.); settings.ambience = value.value("ambience", 0.);
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(GraphNode, id, kind, name, x, y, gainDb, muted, mono, device, app, eqBands, compressor, ducking, dynamicsUiVersion, expander, alignmentDelay, denoiser, deEsser, plosive, reverb, voiceColor, balance, bypass, collapsed, autoAssignDevice)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(GraphEdge, id, from, to, kind, gainDb, muted)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(AudioGraph, nodes, edges, panX, panY, zoom)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(Configuration, version, revision, microphones,
    virtualInputCapture, virtualInputPlayback, mainOutputs, virtualOutputRender,
    virtualOutputCapture, sources, busA, busB, startAtLogin, graph)
void from_json(const Json&, Configuration&);
void migrateLegacyGraph(Configuration&);
Configuration defaultConfiguration();
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(Endpoint, id, stableId, name,
    direction, state, isVirtual, channels, rate, transport, formFactor, description, interfaceName)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(PathState, state, message,
    endpointId, rate, channels, underruns, overruns, reconnects, queueMs,
    periodMs, bufferMs, targetQueueMs, resamplerPendingMs)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(EngineStatus::Node, id, peak, peakLeft, peakRight, channels, clipped, gainReductionDb, detectorDb, dynamicsFrame, spectrumDb, spectrumFrame, spectrumRevision, path)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(EngineStatus, audioFrames,
    sourcePeaks, peakA, peakB, clipA, clipB, inputs, outputs, nodes)
void validateConfiguration(const Configuration&);
std::filesystem::path applicationDataDirectory();
std::string currentUserSid();
std::wstring pipeName();
std::wstring widen(const std::string&);
std::string narrow(const std::wstring&);
class ConfigurationStore {
public:
    explicit ConfigurationStore(std::filesystem::path path = {});
    Configuration load();
    Configuration recover(const std::string& action);
    void pauseForRecovery(const std::string& message);
    void save(const Configuration&);
    const std::string& loadState() const noexcept { return loadState_; }
    bool writable() const noexcept { return loadState_ == "new" || loadState_ == "loaded"; }
    const std::filesystem::path& path() const noexcept { return path_; }
    std::filesystem::path physicalPath() const;
    const std::string& warning() const noexcept { return warning_; }
private:
    std::filesystem::path path_;
    std::string warning_;
    std::string loadState_ = "new";
    bool inspected_ = false;
    bool primaryValid_ = true;
    std::filesystem::path legacySource_;
};
}
