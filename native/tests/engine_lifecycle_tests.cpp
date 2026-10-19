// Runs the actual Engine publication, mixing, persistence and retirement code
// against synthetic workers. windows_audio.cpp/process_capture.cpp are NEVER
// linked into this executable, so it cannot open real audio endpoints.
#include "am/engine.hpp"
#include "am/process_capture.hpp"
#include <windows.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iostream>
#include <map>
#include <stdexcept>
extern "C" std::uint64_t am_speex_memory_operations(void);

namespace test {
std::atomic<int> created{}, live{}, duplicates{}, render1{}, render2{}, renderB{};
std::atomic<float> heard1{}, heard2{}, heardB{};
std::atomic<bool> output1Available{true}, output2Available{true};
std::atomic<bool> failNextConfiguration{};
std::mutex portsMutex;
std::map<std::string, int> openPorts;
void check(bool condition, const char* reason) { if (!condition) throw std::runtime_error(reason); }
void wait(int milliseconds) { std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds)); }
template<class Predicate> bool eventually(Predicate predicate) {
    for (int attempt = 0; attempt < 100; ++attempt) { if (predicate()) return true; wait(10); }
    return predicate();
}
}
namespace am {
struct DeviceCatalog::Impl {};
DeviceCatalog::DeviceCatalog() : impl_(std::make_unique<Impl>()) {}
DeviceCatalog::~DeviceCatalog() = default;
std::vector<Endpoint> DeviceCatalog::enumerate() const { return {}; }
std::uint64_t DeviceCatalog::generation() const noexcept { return 0; }
void DeviceCatalog::refresh() noexcept {}
struct AudioWorker::Impl {
    bool capture;
    std::shared_ptr<FrameQueue> queue;
    std::string id;
    std::mutex mutex;
    std::atomic<bool> stopped{};
    std::thread thread;
    Impl(bool input, std::shared_ptr<FrameQueue> frames) : capture(input), queue(std::move(frames)) {
        ++test::created; ++test::live;
        thread = std::thread([this] {
            std::array<Frame, quantumFrames> frames;
            while (!stopped.load()) {
                std::string current; { std::lock_guard lock(mutex); current = id; }
                const bool ready = !current.empty() && (current != "out1" || test::output1Available.load())
                    && (current != "out2" || test::output2Available.load());
                if (ready && capture) { frames.fill({current == "virtual-in" ? .2f : .3f, .1f}); queue->push(frames); }
                if (ready && !capture) {
                    frames.fill({}); queue->pop(frames);
                    if (current == "out1") { ++test::render1; test::heard1.store(frames.back().left); }
                    if (current == "out2") { ++test::render2; test::heard2.store(frames.back().left); }
                    if (current == "virtual-out") { ++test::renderB; test::heardB.store(frames.back().left); }
                }
                test::wait(5);
            }
        });
    }
    ~Impl() {
        stopped.store(true); if (thread.joinable()) thread.join();
        std::lock_guard lock(test::portsMutex); if (!id.empty()) --test::openPorts[id]; --test::live;
    }
};
AudioWorker::AudioWorker(bool capture, bool, std::shared_ptr<FrameQueue> queue, DeviceCatalog&, std::string)
    : impl_(std::make_unique<Impl>(capture, std::move(queue))) {}
AudioWorker::~AudioWorker() = default;
void AudioWorker::configure(DeviceBinding binding) {
    if (test::failNextConfiguration.exchange(false)) throw std::runtime_error("Synthetic worker initialization failure");
    std::lock_guard lock(impl_->mutex); std::lock_guard portsLock(test::portsMutex);
    if (impl_->id == binding.id) return;
    if (!impl_->id.empty()) --test::openPorts[impl_->id];
    impl_->id = binding.id;
    if (!impl_->id.empty() && ++test::openPorts[impl_->id] > 1) ++test::duplicates;
}
void AudioWorker::retry() noexcept { impl_->queue->requestClear(); }
bool AudioWorker::ready() const noexcept {
    std::lock_guard lock(impl_->mutex);
    return !impl_->id.empty() && (impl_->id != "out1" || test::output1Available.load()) && (impl_->id != "out2" || test::output2Available.load());
}
PathState AudioWorker::status() const { PathState path; path.state = ready() ? "Running" : "Waiting"; path.channels = 2; return path; }
void AudioWorker::stop() noexcept { impl_->stopped.store(true); }
struct ProcessCaptureWorker::Impl {
    std::shared_ptr<FrameQueue> queue;
    std::string identity;
    std::mutex mutex;
    std::atomic<bool> stopped{};
    std::thread thread;
    explicit Impl(std::shared_ptr<FrameQueue> frames) : queue(std::move(frames)) {
        ++test::created; ++test::live;
        thread = std::thread([this] {
            std::array<Frame, quantumFrames> frames; frames.fill({.4f, .15f});
            while (!stopped.load()) {
                bool active; { std::lock_guard lock(mutex); active = !identity.empty(); }
                if (active) queue->push(frames);
                test::wait(5);
            }
        });
    }
    ~Impl() {
        stopped.store(true); if (thread.joinable()) thread.join();
        std::lock_guard lock(test::portsMutex);
        if (!identity.empty()) --test::openPorts[identity];
        --test::live;
    }
};
ProcessCaptureWorker::ProcessCaptureWorker(std::shared_ptr<FrameQueue> queue) : impl_(std::make_unique<Impl>(std::move(queue))) {}
ProcessCaptureWorker::~ProcessCaptureWorker() = default;
void ProcessCaptureWorker::configure(ProcessBinding binding) {
    if (test::failNextConfiguration.exchange(false)) throw std::runtime_error("Synthetic app initialization failure");
    std::lock_guard lock(impl_->mutex); std::lock_guard portsLock(test::portsMutex);
    const auto identity = binding.executablePath.empty() ? std::string{} : "app:" + std::to_string(binding.processId) + ":" + binding.executablePath;
    if (impl_->identity == identity) return;
    if (!impl_->identity.empty()) --test::openPorts[impl_->identity];
    impl_->identity = identity;
    if (!identity.empty() && ++test::openPorts[identity] > 1) ++test::duplicates;
}
void ProcessCaptureWorker::retry() noexcept { impl_->queue->requestClear(); }
bool ProcessCaptureWorker::ready() const noexcept { std::lock_guard lock(impl_->mutex); return !impl_->identity.empty(); }
PathState ProcessCaptureWorker::status() const { PathState path; path.state = ready() ? "Running" : "Waiting"; path.channels = 2; return path; }
void ProcessCaptureWorker::stop() noexcept { impl_->stopped.store(true); }
bool processCaptureAvailable() noexcept { return true; }
std::vector<ApplicationInfo> enumerateAudioApplications() { return {}; }
}

namespace test {
void relativeSettings(const std::filesystem::path& directory) {
    using namespace am;
    const auto fixture = directory / "relative-settings";
    std::filesystem::create_directory(fixture);
    {
        struct RestoreDirectory {
            std::filesystem::path previous = std::filesystem::current_path();
            ~RestoreDirectory() {
                std::error_code ignored;
                std::filesystem::current_path(previous, ignored);
            }
        } restore;
        std::filesystem::current_path(fixture);
        Engine engine("settings.json");
        engine.start();
        auto config = defaultConfiguration();
        config.graph.nodes[0].name = "Relative settings microphone";
        engine.configure(config, 0);
        engine.stop();
        check(ConfigurationStore("settings.json").load().revision == 1,
            "The actual engine must commit a bare relative settings filename");
        check(std::filesystem::exists("engine.log") && std::filesystem::file_size("engine.log") > 0,
            "The actual engine must retain diagnostic logging beside relative settings");
        check(live == 0, "Relative settings fixture leaked synthetic workers");
    }
    for (const auto& name : {"settings.json", "settings.json.bak", "settings.json.new", "engine.log", "engine.log.previous"})
        std::filesystem::remove(fixture / name);
    std::filesystem::remove(fixture);
}
void duplicateSources(const std::filesystem::path& path) {
    using namespace am;
    check(live == 0, "Shared-source fixture must start without workers from another engine");
    const int beforeCreated = created, beforeDuplicates = duplicates;
    auto config = defaultConfiguration(); config.graph.nodes.clear(); config.graph.edges.clear();
    const auto makeNode = [](const std::string& id, const std::string& kind) {
        GraphNode node; node.id = node.name = id; node.kind = kind; node.device.channel = -1; return node;
    };
    const std::array<std::string, 6> sources{"mic-one", "mic-two", "virtual-one", "virtual-two", "app-one", "app-two"};
    const std::array<std::string, 6> sinks{"out1", "out2", "out-virtual-one", "out-virtual-two", "out-app-one", "virtual-out"};
    for (std::size_t i = 0; i < sources.size(); ++i) {
        auto source = makeNode(sources[i], i < 2 ? "microphone" : i < 4 ? "virtualInput" : "appInput");
        if (i < 4) source.device.id = i < 2 ? "mic-a" : "virtual-in";
        else source.app = {100, "C:\\Synthetic\\player.exe", i == 4 ? "Player" : "Independent copy"};
        if (i % 2) source.gainDb = -6.0206f;
        if (i == 1) source.mono = true;
        config.graph.nodes.push_back(source);
    }
    for (std::size_t i = 0; i < sinks.size(); ++i) {
        auto output = makeNode(sinks[i], i == 5 ? "virtualOutput" : "output"); output.device.id = sinks[i];
        config.graph.nodes.push_back(output);
        config.graph.edges.push_back({"route-" + sources[i], sources[i], sinks[i]});
    }
    ConfigurationStore(path).save(config);
    const auto hasPeak = [](const Json& snapshot, const std::string& id, double left, double right) {
        for (const auto& node : snapshot["status"]["nodes"]) if (node["id"] == id)
            return std::abs(node["peakLeft"].get<double>() - left) < .003
                && std::abs(node["peakRight"].get<double>() - right) < .003;
        return false;
    };
    {
        Engine engine(path); engine.start();
        check(created == beforeCreated + 9 && live == 9,
            "Fresh duplicate-source load must create three shared capture workers and six unique sinks");
        check(duplicates == beforeDuplicates, "Fresh duplicate-source load opened a capture identity more than once");
        check(eventually([&] {
            const auto snapshot = engine.snapshot();
            return hasPeak(snapshot, "mic-one", .3, .1) && hasPeak(snapshot, "mic-two", .1, .1)
                && hasPeak(snapshot, "virtual-one", .2, .1) && hasPeak(snapshot, "virtual-two", .1, .05)
                && hasPeak(snapshot, "app-one", .4, .15) && hasPeak(snapshot, "app-two", .2, .075)
                && hasPeak(snapshot, "out1", .3, .1) && hasPeak(snapshot, "out2", .1, .1)
                && hasPeak(snapshot, "out-virtual-one", .2, .1) && hasPeak(snapshot, "out-virtual-two", .1, .05)
                && hasPeak(snapshot, "out-app-one", .4, .15) && hasPeak(snapshot, "virtual-out", .2, .075);
        }), "Shared capture must deliver the full block to both nodes with independent gain, Mono and output routes");
        config.graph.nodes[0].muted = true; config.graph.nodes[1].gainDb = -12.0412f;
        engine.configure(config, config.revision); ++config.revision;
        engine.snapshot(); wait(40);
        check(eventually([&] {
            const auto snapshot = engine.snapshot();
            return hasPeak(snapshot, "mic-one", 0, 0) && hasPeak(snapshot, "out1", 0, 0)
                && hasPeak(snapshot, "mic-two", .05, .05) && hasPeak(snapshot, "out2", .05, .05)
                && hasPeak(snapshot, "virtual-two", .1, .05) && hasPeak(snapshot, "app-two", .2, .075);
        }), "Muting one shared microphone and changing its sibling gain must not change other nodes or captures");
        check(created == beforeCreated + 9, "Shared-source sound controls reopened capture workers");
        config.graph.nodes.erase(config.graph.nodes.begin());
        std::erase_if(config.graph.edges, [](const auto& edge) { return edge.from == "mic-one"; });
        engine.configure(config, config.revision); ++config.revision;
        check(eventually([&] { return hasPeak(engine.snapshot(), "out2", .05, .05); }),
            "Deleting one source copy interrupted the remaining shared microphone route");
        auto restored = makeNode("mic-readded", "microphone"); restored.device.id = "mic-a";
        config.graph.nodes.push_back(restored); config.graph.edges.push_back({"route-restored", "mic-readded", "out1"});
        engine.configure(config, config.revision); ++config.revision;
        check(created == beforeCreated + 9, "Adding a source copy must reuse the existing capture port");
        check(eventually([&] { return hasPeak(engine.snapshot(), "out1", .3, .1); }), "Recreated source copy did not receive shared audio");
        config.graph.nodes[0].device.id = "mic-b";
        engine.configure(config, config.revision); ++config.revision;
        check(created == beforeCreated + 10, "Rebinding one source copy should open only its new capture");
        check(eventually([&] {
            const auto snapshot = engine.snapshot();
            return hasPeak(snapshot, "out1", .3, .1) && hasPeak(snapshot, "out2", .05, .05);
        }), "Rebinding a source copy interrupted its still-shared original capture");
        config.graph.nodes[0].device.id = "mic-a";
        engine.configure(config, config.revision); ++config.revision;
        check(created == beforeCreated + 10 && duplicates == beforeDuplicates,
            "Returning a source copy to its shared capture must reuse the original worker");
        auto invalid = config; invalid.graph.nodes.push_back(config.graph.nodes[5]); invalid.graph.nodes.back().id = "duplicate-sink";
        bool rejected = false;
        try { engine.configure(invalid, config.revision); } catch (const std::exception&) { rejected = true; }
        check(rejected && engine.snapshot()["configuration"]["revision"] == config.revision,
            "Duplicate sinks must remain rejected without changing the committed graph");
        engine.stop(); check(live == 0, "Shared-source shutdown leaked or double-owned a worker");
    }
    const int beforeRestart = created;
    {
        Engine reopened(path); reopened.start();
        check(created == beforeRestart + 9 && duplicates == beforeDuplicates,
            "Reloading saved duplicate sources must reconstruct one worker per capture identity");
        check(eventually([&] {
            const auto snapshot = reopened.snapshot();
            return hasPeak(snapshot, "out1", .3, .1) && hasPeak(snapshot, "out2", .05, .05)
                && hasPeak(snapshot, "app-two", .2, .075) && hasPeak(snapshot, "virtual-two", .1, .05);
        }), "Independent shared-source settings and routes did not survive restart");
        reopened.stop(); check(live == 0, "Restarted shared-source fixture leaked workers");
    }
}
}
int main() {
    using namespace am;
    try {
        const auto directory = std::filesystem::temp_directory_path() / (L"AudioMixer-OfflineEngine-" + std::to_wstring(GetCurrentProcessId()));
        test::check(!std::filesystem::exists(directory), "Test directory already exists");
        std::filesystem::create_directories(directory);
        const auto path = directory / "settings.json";
        Configuration config = defaultConfiguration();
        config.graph.nodes[0].device.id = "mic-a";
        config.graph.nodes[2].device.id = "virtual-in";
        config.graph.nodes[5].device.id = "out1";
        config.graph.nodes[6].device.id = "out2";
        config.graph.nodes[7].device.id = "virtual-out";
        config.graph.edges[1].muted = false;
        GraphNode voiceColor; voiceColor.id = "voice-color"; voiceColor.kind = "voiceColor";
        const auto voiceColorIndex = config.graph.nodes.size();
        config.graph.nodes.push_back(voiceColor);
        config.graph.edges.push_back({"voice-color-input", "mic1", "voice-color"});
        GraphNode expansion; expansion.id = "expansion"; expansion.kind = "expander";
        const auto expansionIndex = config.graph.nodes.size(); config.graph.nodes.push_back(expansion);
        config.graph.edges.push_back({"expansion-input", "mic1", "expansion"});
        GraphNode delay; delay.id = "alignment"; delay.kind = "alignmentDelay";
        const auto delayIndex = config.graph.nodes.size(); config.graph.nodes.push_back(delay);
        config.graph.edges.push_back({"alignment-input", "expansion", "alignment"});
        GraphNode ducking; ducking.id = "ducking"; ducking.kind = "ducking";
        const auto duckingIndex = config.graph.nodes.size(); config.graph.nodes.push_back(ducking);
        config.graph.edges.push_back({"ducking-input", "mic1", "ducking"});
        config.graph.edges.push_back({"ducking-trigger", "alignment", "ducking", "sidechain"});
        GraphNode limiter; limiter.id = "limiter"; limiter.kind = "limiter"; limiter.compressor.ceilingDb = -12;
        const auto limiterIndex = config.graph.nodes.size(); config.graph.nodes.push_back(limiter);
        config.graph.edges.push_back({"limiter-input", "mic1", "limiter"});
        GraphNode dynamics; dynamics.id = "dynamics"; dynamics.kind = "compressor";
        dynamics.dynamicsUiVersion = 1;
        dynamics.compressor = {-30, 6, 10, 250, 0, "compress", "external", -12};
        const auto dynamicsIndex = config.graph.nodes.size(); config.graph.nodes.push_back(dynamics);
        config.graph.edges.push_back({"dynamics-input", "mic1", "dynamics"});
        config.graph.edges.push_back({"dynamics-key", "alignment", "dynamics", "sidechain"});
        GraphNode layoutDenoiser; layoutDenoiser.id = "layout-denoiser"; layoutDenoiser.kind = "denoiser";
        config.graph.nodes.push_back(layoutDenoiser);
        GraphNode spectrumEq; spectrumEq.id = "spectrum-eq"; spectrumEq.kind = "eq";
        config.graph.nodes.push_back(spectrumEq);
        config.graph.edges.push_back({"spectrum-input", "mic1", "spectrum-eq"});
        ConfigurationStore(path).save(config);
        std::uint64_t previousEngineSpectrumRevision{};
        {
            Engine engine(path); engine.start(); test::wait(150);
            const auto initial = engine.snapshot();
            test::check(initial["status"]["audioFrames"].get<std::uint64_t>() > 0, "Audio clock stalled");
            test::check(test::render1 > 10 && test::render2 > 10 && test::renderB > 10, "Initial synthetic fanout failed");
            const auto& mic = initial["status"]["nodes"][0];
            test::check(mic["channels"] == 2 && mic["peakLeft"].get<float>() > .29f
                && mic["peakRight"].get<float>() > .09f && mic["peakRight"].get<float>() < .11f,
                "Engine snapshot must retain genuine independent left/right peaks and stereo metadata");
            test::check(mic["peak"] == mic["peakLeft"], "Legacy snapshot peak must equal larger channel");
            const auto initialSpectrum = initial["status"]["nodes"].back();
            test::check(initialSpectrum["id"] == "spectrum-eq" && initialSpectrum["spectrumDb"].size() == 128
                && initialSpectrum["spectrumFrame"].get<std::uint64_t>() > 0, "Engine snapshot must include measured EQ spectrum bins and frame identity");
            test::check(mic["spectrumDb"].empty() && mic["spectrumFrame"] == 0,
                "Non-EQ nodes must not invent spectrum telemetry");
            const auto spectrumRevision = initialSpectrum["spectrumRevision"].get<std::uint64_t>();
            previousEngineSpectrumRevision = spectrumRevision;
            const int existingWorkers = test::created;
            const auto layoutMemory = am_speex_memory_operations();
            for (int i = 0; i < 20; ++i) {
                config.graph.nodes[0].x += 1; config.graph.nodes[0].name = "Microphone " + std::to_string(i);
                config.graph.nodes[0].device.name = "Updated device label " + std::to_string(i);
                config.graph.nodes.back().eqBands[0].resetFrequencyHz = 80 + i;
                config.graph.panX += 1; config.graph.panY -= 1;
                engine.configure(config, config.revision); ++config.revision;
            }
            test::check(am_speex_memory_operations() == layoutMemory,
                "Canvas-only saves must not rebuild denoiser FFT allocations or copy a replacement DSP graph");
            const auto layoutSnapshot = engine.snapshot();
            test::check(layoutSnapshot["configuration"]["revision"] == config.revision
                && layoutSnapshot["configuration"]["graph"]["nodes"][0]["name"] == config.graph.nodes[0].name,
                "Canvas-only changes still commit their revision and latest labels");
            test::check(layoutSnapshot["status"]["nodes"].back()["spectrumRevision"] == spectrumRevision,
                "Canvas-only saves preserve live spectrum continuity");
            // A committed setting and a nonzero meter do not prove that the
            // audio changes. Temporarily route the existing synthetic output
            // through Voice Color and observe both its DSP meter and the
            // samples consumed by the output worker. No real endpoint exists.
            auto colorOutput = std::find_if(config.graph.edges.begin(), config.graph.edges.end(),
                [](const auto& edge) { return edge.id == "main-output-1-in"; });
            test::check(colorOutput != config.graph.edges.end(), "Synthetic Voice Color output route is missing");
            const auto originalColorOutput = *colorOutput;
            colorOutput->from = "voice-color";
            const auto observeColor = [&](double tone, float expected, const char* reason) {
                const auto priorFrames = engine.snapshot()["status"]["audioFrames"].get<std::uint64_t>();
                config.graph.nodes[voiceColorIndex].voiceColor = {tone, 0};
                config.graph.nodes[voiceColorIndex].bypass = false;
                engine.configure(config, config.revision); ++config.revision;
                int stable{};
                float measured{};
                test::check(test::eventually([&] {
                    const auto snapshot = engine.snapshot();
                    // Clear historical peak accumulation while the runtime,
                    // filter ramps and queued output settle. Require several
                    // independent matching observations, not one lucky frame.
                    if (snapshot["status"]["audioFrames"].get<std::uint64_t>() < priorFrames + sampleRate / 8) return false;
                    measured = snapshot["status"]["nodes"][voiceColorIndex]["peakLeft"].get<float>();
                    const float heard = test::heard1.load();
                    const bool matched = std::abs(measured - expected) < expected * .025f
                        && std::abs(heard - expected) < expected * .025f
                        && snapshot["configuration"]["revision"].get<std::uint64_t>() == config.revision;
                    stable = matched ? stable + 1 : 0;
                    return stable >= 3;
                }), reason);
                return measured;
            };
            const float neutralColor = observeColor(0, .3f,
                "Neutral Voice Color must reach the actual synthetic renderer at the unchanged source level");
            // At Natural, the deliberate endpoints have +6 dB and -12 dB DC
            // gain respectively: these independent expectations reject a
            // visually moving pad whose published DSP remains neutral/stale.
            const float warmColor = observeColor(-1, .3f * 1.995262315f,
                "Publishing Warm must change both Voice Color output and rendered samples to the +6 dB endpoint");
            const float brightColor = observeColor(1, .3f * .251188643f,
                "Publishing Bright must change both Voice Color output and rendered samples to the -12 dB endpoint");
            const float resetColor = observeColor(0, .3f,
                "Returning Voice Color to neutral must restore the actual rendered source level after inherited DSP state");
            test::check(warmColor > neutralColor * 1.9f && brightColor < neutralColor * .28f
                && warmColor > brightColor * 7.5f && std::abs(resetColor - neutralColor) < .01f,
                "Live Voice Color endpoints must produce a substantial reversible output change, not merely persist coordinates");
            test::check(test::created == existingWorkers,
                "Voice Color output changes must retain existing synthetic audio workers");
            *colorOutput = originalColorOutput;
            engine.configure(config, config.revision); ++config.revision;
            std::cout << "Live Voice Color rendered levels: neutral=" << neutralColor << "; Warm=" << warmColor
                << "; Bright=" << brightColor << "; reset=" << resetColor << '\n';
            const auto beforeColorFrames = initial["status"]["audioFrames"].get<std::uint64_t>();
            for (int i = 0; i < 32; ++i) {
                config.graph.nodes[voiceColorIndex].voiceColor = {(i % 3 - 1) * .75, (i % 4) / 3.};
                config.graph.nodes[voiceColorIndex].bypass = i % 7 == 0;
                engine.configure(config, config.revision); ++config.revision; test::wait(7);
            }
            const auto colorSnapshot = engine.snapshot();
            test::check(test::created == existingWorkers && colorSnapshot["status"]["audioFrames"].get<std::uint64_t>() > beforeColorFrames,
                "Voice Color live parameter/bypass commits keep the hardware workers and mixer timeline running");
            test::check(colorSnapshot["status"]["nodes"][voiceColorIndex]["path"]["state"] == "Running"
                && colorSnapshot["status"]["nodes"][voiceColorIndex]["peak"].get<float>() > 0,
                "Voice Color participates in the actual engine graph and publishes live meters");
            const auto colorSaved = ConfigurationStore(path).load();
            test::check(colorSaved.graph.nodes[voiceColorIndex].voiceColor == config.graph.nodes[voiceColorIndex].voiceColor
                && colorSaved.graph.nodes[voiceColorIndex].bypass == config.graph.nodes[voiceColorIndex].bypass,
                "Actual engine publication persists both Voice Color axes and bypass in the isolated fixture store");
            for (int i = 0; i < 32; ++i) {
                config.graph.nodes[expansionIndex].expander.mode = i % 2 ? "gate" : "expander";
                config.graph.nodes[expansionIndex].expander.thresholdDb = -40 + i % 12;
                config.graph.nodes[delayIndex].alignmentDelay.samples = static_cast<std::uint32_t>(i * 48);
                config.graph.nodes[dynamicsIndex].compressor.mode = i % 2 ? "compress" : "limit";
                config.graph.nodes[duckingIndex].ducking.amountDb = 12 + i % 12;
                config.graph.nodes[limiterIndex].compressor.ceilingDb = -6 - i % 12;
                engine.configure(config, config.revision); ++config.revision; test::wait(7);
            }
            test::wait(100);
            const auto additions = engine.snapshot();
            test::check(test::created == existingWorkers && additions["status"]["audioFrames"].get<std::uint64_t>() > colorSnapshot["status"]["audioFrames"].get<std::uint64_t>(),
                "Ducking, Limit, Gate and Alignment live commits preserve healthy workers and advancing timeline");
            for (const auto index : {expansionIndex, delayIndex, dynamicsIndex, duckingIndex, limiterIndex})
                test::check(additions["status"]["nodes"][index]["path"]["state"] == "Running" && additions["status"]["nodes"][index]["peak"].get<float>() > 0,
                    "Every new processing path runs and meters in the actual engine");
            test::check(additions["status"]["nodes"][expansionIndex]["dynamicsFrame"].get<std::uint64_t>() > 0
                && additions["status"]["nodes"][dynamicsIndex]["dynamicsFrame"].get<std::uint64_t>() > 0,
                "Gate and external detector publish current dynamics telemetry");
            for (const auto index : {duckingIndex, limiterIndex})
                test::check(additions["status"]["nodes"][index]["dynamicsFrame"].get<std::uint64_t>() > 0
                    && additions["status"]["nodes"][index]["gainReductionDb"].get<float>() > 0,
                    "Dedicated Ducking and Limiter publish real attenuation and current telemetry");
            const auto additionsSaved = ConfigurationStore(path).load();
            test::check(additionsSaved.graph.nodes[expansionIndex].expander == config.graph.nodes[expansionIndex].expander
                && additionsSaved.graph.nodes[delayIndex].alignmentDelay == config.graph.nodes[delayIndex].alignmentDelay
                && Json(additionsSaved.graph.nodes[dynamicsIndex].compressor) == Json(config.graph.nodes[dynamicsIndex].compressor)
                && additionsSaved.graph.edges[additionsSaved.graph.edges.size()-2].kind == "sidechain",
                "Actual engine commits persist dynamics, exact delay and detector cables in private settings");
            test::check(additionsSaved.graph.nodes[duckingIndex].ducking == config.graph.nodes[duckingIndex].ducking
                && additionsSaved.graph.nodes[limiterIndex].compressor.ceilingDb == config.graph.nodes[limiterIndex].compressor.ceilingDb
                && additionsSaved.graph.nodes[dynamicsIndex].dynamicsUiVersion == 1
                && additions["configuration"]["graph"]["nodes"][dynamicsIndex]["dynamicsUiVersion"] == 1,
                "Engine persistence and snapshots retain dedicated dynamics settings and the interface version");
            std::atomic<bool> observing{true}, observerFailed{};
            std::thread observer([&] {
                while (observing.load()) {
                    try { const auto value = engine.snapshot(); if (!value["status"]["nodes"].is_array()) observerFailed = true; }
                    catch (...) { observerFailed = true; }
                    test::wait(1);
                }
            });
            try {
                for (int i = 0; i < 180; ++i) {
                    config.graph.nodes[0].x = i; config.graph.nodes[3].gainDb = static_cast<float>(-(i % 12));
                    config.graph.nodes[0].collapsed = (i % 2) != 0;
                    config.graph.nodes[0].mono = (i % 2) != 0;
                    if (i % 2 == 0) {
                        GraphNode extra; extra.id = "transient"; extra.kind = "gain"; extra.gainDb = -3;
                        config.graph.nodes.push_back(extra); config.graph.edges.push_back({"transient-in", "mic1", "transient"});
                    } else { config.graph.nodes.pop_back(); config.graph.edges.pop_back(); }
                    engine.configure(config, config.revision); ++config.revision;
                    test::wait(2);
                }
            } catch (...) { observing = false; observer.join(); throw; }
            observing = false; observer.join();
            test::check(!observerFailed, "Concurrent snapshot failed");
            test::check(test::created == existingWorkers, "Layout/gain/Mono/topology edits reopened healthy device workers");
            test::wait(40); engine.snapshot(); test::wait(30);
            const auto monoSnapshot = engine.snapshot(); const auto& monoMic = monoSnapshot["status"]["nodes"][0];
            test::check(monoMic["channels"] == 1 && monoMic["peakLeft"].get<float>() > .19f
                && monoMic["peakLeft"].get<float>() < .21f && monoMic["peakRight"].get<float>() > .19f
                && monoMic["peakRight"].get<float>() < .21f, "Engine publishes averaged microphone Mono meters without reopening its worker");
            // Fast assignment reversals must reuse older retained endpoint workers.
            for (int i = 0; i < 30; ++i) {
                config.graph.nodes[0].device.id = i % 2 == 0 ? "mic-b" : "mic-a";
                engine.configure(config, config.revision); ++config.revision;
            }
            test::check(test::duplicates == 0, "Rapid rebinding opened an endpoint twice");
            const int previousB = test::renderB, previousA2 = test::render2;
            test::output1Available = false;
            test::check(test::eventually([&] { return test::renderB > previousB + 8 && test::render2 > previousA2 + 8; }), "Lost output interrupted healthy siblings");
            const int continuedB = test::renderB; test::output2Available = false;
            test::check(test::eventually([&] { return test::renderB > continuedB + 8; }), "B stopped with both listening outputs absent");
            const int absentA1 = test::render1, absentA2 = test::render2;
            test::output1Available = true; test::output2Available = true;
            test::check(test::eventually([&] { return test::render1 > absentA1 + 8 && test::render2 > absentA2 + 8; }), "Reconnected sinks did not rejoin");
            test::wait(1100); // allow maintenance to retire obsolete plans and workers
            test::check(test::live == existingWorkers, "Retired device worker leaked");
            const auto final = engine.snapshot();
            test::check(final["configuration"]["revision"].get<std::uint64_t>() == config.revision, "Committed revision lost");
            const auto spectrum = final["status"]["nodes"].back();
            test::check(spectrum["spectrumRevision"] == spectrumRevision
                && spectrum["spectrumFrame"].get<std::uint64_t>() > initialSpectrum["spectrumFrame"].get<std::uint64_t>(),
                "Routine graph edits must preserve analyzer continuity while measured frames advance");
            for (const auto& bin : spectrum["spectrumDb"])
                test::check(std::isfinite(bin.get<float>()) && bin.get<float>() >= -90 && bin.get<float>() <= 0,
                    "Published spectrum values must remain finite and in range");
            config.graph.nodes.pop_back(); config.graph.edges.pop_back();
            engine.configure(config, config.revision); ++config.revision;
            config.graph.nodes.push_back(spectrumEq);
            config.graph.edges.push_back({"spectrum-input", "mic1", "spectrum-eq"});
            engine.configure(config, config.revision); ++config.revision;
            test::check(engine.snapshot()["status"]["nodes"].back()["spectrumRevision"] != spectrumRevision,
                "Deleting and recreating an EQ must never reuse its stale spectrum continuity token");
            engine.stop(); test::check(test::live == 0, "Engine stop leaked worker threads");
        }
        {
            Engine replacement(path); replacement.start();
            const auto restarted = replacement.snapshot()["status"]["nodes"].back();
            test::check(restarted["spectrumRevision"].get<std::uint64_t>() != previousEngineSpectrumRevision,
                "A restarted engine must give its EQ a new spectrum continuity token before priming");
            replacement.stop(); test::check(test::live == 0, "Replacement engine stop leaked worker threads");
            test::failNextConfiguration = true;
            bool rejected = false;
            try { replacement.start(); } catch (const std::exception&) { rejected = true; }
            test::check(rejected && test::live == 0 && replacement.stopping(),
                "Failed startup must close partially constructed workers and remain retryable");
            const auto oldFrames = replacement.snapshot()["status"]["audioFrames"].get<std::uint64_t>();
            replacement.start();
            test::check(test::eventually([&] {
                return replacement.snapshot()["status"]["audioFrames"].get<std::uint64_t>() > oldFrames + quantumFrames;
            }), "The same engine object can start after a stop and a failed startup");
            replacement.stop(); test::check(test::live == 0, "Retried engine stop leaked worker threads");
        }
        const auto sharedPath = directory / "shared-settings.json";
        test::duplicateSources(sharedPath);
        test::relativeSettings(directory);
        for (const auto& fixture : {path, sharedPath})
            for (const auto& suffix : {"", ".bak", ".new"}) std::filesystem::remove(fixture.string() + suffix);
        std::filesystem::remove(directory / "engine.log"); std::filesystem::remove(directory / "engine.log.previous");
        std::filesystem::remove(directory);
        std::cout << "PASS: actual engine survived 301 graph commits, Core Dynamics and measured Voice Color output changes/live persistence without worker restart, canvas-only saves without DSP rebuilds, concurrent snapshots, measured EQ spectrum lifecycle, topology fades, rapid device rebinding, isolated output loss/rejoin, retirement, restart and failed-start cleanup; fresh and saved duplicate microphone/virtual/app sources share capture with independent controls/routes while output uniqueness remains enforced. Synthetic worker stubs only.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
