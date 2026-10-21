#include "am/process_capture.hpp"
#include "am/dsp.hpp"
#include <windows.h>
#include <nlohmann/json.hpp>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

// Explicit, bounded observation of ONE exact discovered application. This
// helper has no renderer, microphone worker, engine, settings store or UI. It
// retains only aggregate statistics; it does not record or save audio samples.
namespace {
using Json = nlohmann::json;
using Clock = std::chrono::steady_clock;
struct Options {
    bool run{};
    std::string applicationName;
    double seconds = 10;
    std::filesystem::path output;
};
std::string utf8(const std::wstring& value) {
    const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length == 0 && !value.empty()) throw std::runtime_error("Invalid Unicode argument");
    std::string result(static_cast<std::size_t>(length), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    return result;
}
Options parse(int argc, wchar_t** argv) {
    Options result;
    for (int i = 1; i < argc; ++i) {
        const std::wstring argument = argv[i];
        if (argument == L"--run") result.run = true;
        else if (argument == L"--application-name" && i + 1 < argc) result.applicationName = utf8(argv[++i]);
        else if (argument == L"--seconds" && i + 1 < argc) {
            const std::wstring value = argv[++i];
            std::size_t used{}; result.seconds = std::stod(value, &used);
            if (used != value.size()) throw std::runtime_error("Invalid seconds argument");
        }
        else if (argument == L"--output" && i + 1 < argc) result.output = argv[++i];
        else throw std::runtime_error("Unknown or incomplete option");
    }
    if (!result.run || result.applicationName.empty() || result.output.empty())
        throw std::runtime_error("Required: --run --application-name <exact name> --seconds <1..60> --output <new JSON file>");
    if (!std::isfinite(result.seconds) || result.seconds < 1 || result.seconds > 60)
        throw std::runtime_error("Observation duration must be between 1 and 60 seconds");
    result.output = std::filesystem::absolute(result.output);
    if (std::filesystem::exists(result.output)) throw std::runtime_error("Output already exists; use a new result filename");
    if (!std::filesystem::is_directory(result.output.parent_path())) throw std::runtime_error("Output parent directory must already exist");
    return result;
}
Json appJson(const am::ApplicationInfo& app) {
    return {{"name", app.name}, {"processId", app.processId}, {"executablePath", app.executablePath}, {"hasAudioSessionAtDiscovery", app.hasAudioSession}};
}
Json pathJson(const am::PathState& state) {
    return {{"state", state.state}, {"message", state.message}, {"endpointId", state.endpointId},
        {"sampleRate", state.rate}, {"channels", state.channels}, {"reconnects", state.reconnects},
        {"queueMs", state.queueMs}, {"overrunEvents", state.overruns}, {"underrunEvents", state.underruns}};
}
std::string utcNow() {
    SYSTEMTIME time{}; GetSystemTime(&time);
    char text[32]{};
    std::snprintf(text, sizeof(text), "%04u-%02u-%02uT%02u:%02u:%02uZ", time.wYear, time.wMonth,
        time.wDay, time.wHour, time.wMinute, time.wSecond);
    return text;
}
void save(const Options& options, const Json& report) {
    // CREATE_NEW also prevents a race from overwriting a file created after
    // argument validation. Only JSON aggregate statistics are written.
    const HANDLE file = CreateFileW(options.output.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("Could not create new observation report");
    const auto text = report.dump(2) + "\n";
    DWORD written{};
    const BOOL okay = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    CloseHandle(file);
    if (!okay || written != text.size()) throw std::runtime_error("Could not write complete observation report");
}
int observe(const Options& options) {
    Json report{{"tool", "VineMix.ApplicationObserve"}, {"startedUtc", utcNow()},
        {"requestedApplicationName", options.applicationName}, {"requestedSeconds", options.seconds},
        {"mode", "process-loopback statistics only"}, {"audioFilesWritten", false},
        {"outputStreamsOpened", false}, {"microphoneStreamsOpened", false}, {"settingsChanged", false},
        {"originalPlaybackChanged", false}, {"processCaptureAvailable", am::processCaptureAvailable()}};
    std::vector<am::ApplicationInfo> matches;
    for (const auto& application : am::enumerateAudioApplications())
        if (application.name == options.applicationName) matches.push_back(application);
    report["exactMatches"] = Json::array();
    for (const auto& match : matches) report["exactMatches"].push_back(appJson(match));
    if (matches.size() != 1) {
        report["outcome"] = matches.empty() ? "application_not_found" : "ambiguous_application";
        report["captureAttempted"] = false;
        report["detail"] = "Exactly one case-sensitive discovered application name is required; no fallback target is permitted.";
        save(options, report);
        std::cout << report["outcome"].get<std::string>() << "; exact matches=" << matches.size() << "; no stream opened\n";
        return 2;
    }
    if (!report["processCaptureAvailable"].get<bool>()) {
        report["outcome"] = "process_capture_unsupported"; report["captureAttempted"] = false;
        save(options, report); return 3;
    }
    const auto selected = matches.front();
    report["selectedApplication"] = appJson(selected);
    auto queue = std::make_shared<am::FrameQueue>(16384);
    am::ProcessCaptureWorker worker(queue);
    std::array<am::Frame, 2048> frames{};
    std::uint64_t consumed{}, nonfinite{}, signalFrames{}, readyPolls{}, polls{};
    double peakLeft{}, peakRight{}, energyLeft{}, energyRight{};
    std::size_t maximumQueue{};
    std::map<std::string, std::uint64_t> statusPolls;
    Json transitions = Json::array();
    std::string previousState, previousMessage;
    std::optional<double> firstReadyMs, firstFrameMs;
    const auto start = Clock::now();
    const auto deadline = start + std::chrono::duration<double>(options.seconds);
    worker.configure({selected.processId, selected.executablePath, selected.name});
    report["captureAttempted"] = true;
    while (Clock::now() < deadline) {
        const auto milliseconds = std::chrono::duration<double, std::milli>(Clock::now() - start).count();
        const auto state = worker.status();
        ++polls; ++statusPolls[state.state];
        if (worker.ready()) { ++readyPolls; if (!firstReadyMs) firstReadyMs = milliseconds; }
        if (state.state != previousState || state.message != previousMessage) {
            transitions.push_back({{"elapsedMs", milliseconds}, {"state", state.state}, {"message", state.message}, {"endpointId", state.endpointId}});
            previousState = state.state; previousMessage = state.message;
        }
        const auto available = queue->size();
        maximumQueue = std::max(maximumQueue, available);
        // A producer-requested flush may reduce this snapshot before pop. That
        // affects queue telemetry only; never block or generate replacement data.
        const auto count = queue->pop(std::span(frames).first(std::min(available, frames.size())));
        if (count && !firstFrameMs) firstFrameMs = milliseconds;
        consumed += count;
        for (std::size_t i = 0; i < count; ++i) {
            const double left = frames[i].left, right = frames[i].right;
            if (!std::isfinite(left) || !std::isfinite(right)) { ++nonfinite; continue; }
            peakLeft = std::max(peakLeft, std::abs(left)); peakRight = std::max(peakRight, std::abs(right));
            energyLeft += left * left; energyRight += right * right;
            if (std::abs(left) > 1e-6 || std::abs(right) > 1e-6) ++signalFrames;
        }
        std::fill(frames.begin(), frames.begin() + count, am::Frame{});
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    const auto finalState = worker.status();
    worker.stop(); // Releases only this helper's process-loopback client.
    report["finishedUtc"] = utcNow();
    report["elapsedSecondsIncludingStop"] = std::chrono::duration<double>(Clock::now() - start).count();
    report["workerStopped"] = !worker.ready();
    report["pollCount"] = polls; report["readyPollCount"] = readyPolls;
    report["statusPollCounts"] = statusPolls; report["transitions"] = transitions;
    report["lastObservedState"] = pathJson(finalState);
    report["firstReadyMs"] = firstReadyMs ? Json(*firstReadyMs) : Json(nullptr);
    report["firstFrameMs"] = firstFrameMs ? Json(*firstFrameMs) : Json(nullptr);
    report["framesObserved"] = consumed; report["framesContainingNonfiniteSamples"] = nonfinite;
    report["framesAboveOneMillionthAmplitude"] = signalFrames;
    report["peak"] = {{"left", peakLeft}, {"right", peakRight}};
    const auto finiteFrames = consumed - nonfinite;
    report["rms"] = {{"left", finiteFrames ? std::sqrt(energyLeft / finiteFrames) : 0},
        {"right", finiteFrames ? std::sqrt(energyRight / finiteFrames) : 0}};
    report["maximumObservedQueueFrames"] = maximumQueue;
    report["queueDroppedFrames"] = queue->droppedFrames();
    report["queueOverrunEvents"] = queue->overrunEvents();
    report["queueUnderrunEvents"] = queue->underrunEvents();
    report["detail"] = "Frame counts include filter startup and adaptive resampling; this is a capture-presence check, not a latency or sample-clock measurement. Silence alone does not prove failure.";
    int result{};
    if (nonfinite) { report["outcome"] = "nonfinite_samples_observed"; result = 4; }
    else if (readyPolls == 0 || consumed == 0) { report["outcome"] = "capture_not_observed"; result = 5; }
    else if (signalFrames == 0) { report["outcome"] = "capture_observed_silent"; result = 6; }
    else { report["outcome"] = "capture_observed_with_signal"; result = 0; }
    save(options, report);
    std::cout << report["outcome"].get<std::string>() << "; exact PID=" << selected.processId << "; frames=" << consumed
        << "; nonfinite=" << nonfinite << "; peak L/R=" << peakLeft << '/' << peakRight << "; helper stopped\n";
    return result;
}
} // namespace
int wmain(int argc, wchar_t** argv) {
    SetPriorityClass(GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS);
    try { return observe(parse(argc, argv)); }
    catch (const std::exception& error) { std::cerr << "Observation helper: " << error.what() << '\n'; return 1; }
}
