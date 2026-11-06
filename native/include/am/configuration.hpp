#pragma once
#include "am/types.hpp"
#include <filesystem>
#include <cmath>
#include <limits>
#include <nlohmann/json.hpp>
#include <stdexcept>
#include <string>

namespace am {
using Json = nlohmann::json;
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(SendControl, enabled, gainDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(SourceControl, muted, a, b)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE(BusControl, muted, gainDb)
inline std::int64_t readIntegerSetting(const Json& value, std::int64_t minimum, std::int64_t maximum, const char* message) {
    // Check the original JSON value before narrowing. Arithmetic conversion
    // otherwise wraps large identities or truncates a different channel.
    if (!value.is_number_integer()) throw std::runtime_error(message);
    if (value.is_number_unsigned()) {
        const auto number = value.get<std::uint64_t>();
        if (number > static_cast<std::uint64_t>(maximum) || (minimum > 0 && number < static_cast<std::uint64_t>(minimum)))
            throw std::runtime_error(message);
        return static_cast<std::int64_t>(number);
    }
    const auto number = value.get<std::int64_t>();
    if (number < minimum || number > maximum) throw std::runtime_error(message);
    return number;
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(DeviceBinding, id, stableId, name, channel)
inline void from_json(const Json& value, DeviceBinding& binding) {
    value.at("id").get_to(binding.id); value.at("stableId").get_to(binding.stableId); value.at("name").get_to(binding.name);
    binding.channel = static_cast<int>(readIntegerSetting(value.at("channel"), -1, 255,
        "The selected microphone channel must be a whole number between -1 and 255."));
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(ProcessBinding, processId, executablePath, name)
inline void from_json(const Json& value, ProcessBinding& binding) {
    if (!value.is_object()) throw std::runtime_error("Application binding must be an object.");
    binding.processId = static_cast<std::uint32_t>(readIntegerSetting(value.value("processId", Json(0)),
        0, std::numeric_limits<std::uint32_t>::max(), "The application process ID must be a nonnegative 32-bit whole number."));
    binding.executablePath = value.value("executablePath", std::string{});
    binding.name = value.value("name", std::string{});
}
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
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(EchoSettings, timeMs, repeats, mix)
inline void from_json(const Json& value, EchoSettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Echo settings must be an object.");
    settings = {};
    const auto number = [&](const char* name, double fallback) {
        if (!value.contains(name)) return fallback;
        const auto& item = value.at(name);
        if (!item.is_number()) throw std::runtime_error("Echo settings must contain finite numbers.");
        const double result = item.get<double>();
        if (!std::isfinite(result)) throw std::runtime_error("Echo settings must contain finite numbers.");
        return result;
    };
    settings.timeMs = number("timeMs", settings.timeMs); settings.mix = number("mix", settings.mix);
    // Feedback was an unpublished development setting. Validate it even when
    // the explicit repeat count wins, then emit only the canonical new fields.
    const double feedback = number("feedback", 0);
    if (feedback < 0 || feedback > .9) throw std::runtime_error("Legacy Echo feedback must be between 0 and 90 percent.");
    if (value.contains("repeats")) {
        const double repeats = number("repeats", 3);
        if (repeats < 1 || repeats > 8 || repeats != std::floor(repeats))
            throw std::runtime_error("Echo repeats must be a whole number between 1 and 8.");
        settings.repeats = static_cast<int>(repeats);
    } else if (value.contains("feedback")) {
        settings.repeats = 1;
        for (double level = feedback; settings.repeats < 8 && level >= .01; level *= feedback) ++settings.repeats;
    }
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(DistortionSettings, driveDb, toneHz, mix, outputDb)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(VoiceColorSettings, tone, ambience)
inline void from_json(const Json& value, VoiceColorSettings& settings) {
    if (!value.is_object()) throw std::runtime_error("Voice Color settings must be an object.");
    settings.tone = value.value("tone", 0.); settings.ambience = value.value("ambience", 0.);
}
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(GraphNode, id, kind, name, x, y, gainDb, muted, mono, device, app, eqBands, compressor, ducking, dynamicsUiVersion, expander, alignmentDelay, denoiser, deEsser, plosive, reverb, echo, distortion, voiceColor, balance, bypass, collapsed, autoAssignDevice, stereoWidth, panPosition, crossfadePosition, crossfadeCurve, controlMultiplier)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(GraphEdge, id, from, to, kind, gainDb, muted, fromPort, toPort)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_WITH_DEFAULT(AudioGraph, nodes, edges, panX, panY, zoom)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(Configuration, version, revision, microphones,
    virtualInputCapture, virtualInputPlayback, mainOutputs, virtualOutputRender,
    virtualOutputCapture, sources, busA, busB, startAtLogin, graph)
std::uint64_t readRevision(const Json&);
void from_json(const Json&, Configuration&);
void migrateLegacyGraph(Configuration&);
Configuration defaultConfiguration();
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(Endpoint, id, stableId, name,
    direction, state, isVirtual, channels, rate, transport, formFactor, description, interfaceName)
NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE_ONLY_SERIALIZE(PathState, state, message, reason,
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
class SettingsWriteError : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};
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
