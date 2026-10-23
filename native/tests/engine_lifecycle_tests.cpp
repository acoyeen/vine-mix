// Runs the actual Engine publication, mixing, persistence and retirement code
// against synthetic workers. windows_audio.cpp/process_capture.cpp are NEVER
// linked into this executable, so it cannot open real audio endpoints.
#include "am/engine.hpp"
#include "am/control_server.hpp"
#include "am/engine_instance.hpp"
#include "am/process_capture.hpp"
#include <windows.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <stdexcept>
extern "C" std::uint64_t am_speex_memory_operations(void);

namespace test {
std::atomic<int> created{}, live{}, duplicates{}, render1{}, render2{}, renderB{};
std::atomic<float> heard1{}, heard2{}, heardB{};
std::atomic<bool> output1Available{true}, output2Available{true};
std::atomic<bool> failNextConfiguration{};
std::atomic<int> workerWakeMilliseconds{5};
std::mutex portsMutex;
std::map<std::string, int> openPorts;
// Set only before constructing a fixture engine, and cleared after destruction.
// The catalog remains immutable while its maintenance thread can read it.
std::vector<am::Endpoint> catalogEndpoints;
std::map<std::string, std::string> configuredTransports;
void check(bool condition, const char* reason) { if (!condition) throw std::runtime_error(reason); }
void wait(int milliseconds) { std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds)); }
void refillCapture(am::FrameQueue& queue, std::span<const am::Frame> frames) {
    // Constant synthetic capture has no independent hardware clock/drift
    // controller. Keep 40 ms ready so coarse thread wakes cannot turn the DC
    // fixture into alternating signal and silence. Work and storage are bounded.
    constexpr std::size_t leadBlocks = 8;
    for (std::size_t block = 0; block < leadBlocks && queue.size() < leadBlocks * am::quantumFrames; ++block)
        if (queue.push(frames) != frames.size()) break;
}
template<class Predicate> bool eventually(Predicate predicate) {
    for (int attempt = 0; attempt < 100; ++attempt) { if (predicate()) return true; wait(10); }
    return predicate();
}
}
namespace am {
struct DeviceCatalog::Impl {};
DeviceCatalog::DeviceCatalog() : impl_(std::make_unique<Impl>()) {}
DeviceCatalog::~DeviceCatalog() = default;
std::vector<Endpoint> DeviceCatalog::enumerate() const { return test::catalogEndpoints; }
std::uint64_t DeviceCatalog::generation() const noexcept { return 0; }
void DeviceCatalog::refresh() noexcept {}
struct AudioWorker::Impl {
    bool capture;
    std::shared_ptr<FrameQueue> queue;
    std::string id;
    std::string requiredTransport;
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
                if (ready && capture) {
                    frames.fill({current == "virtual-in" ? .2f : .3f, .1f});
                    test::refillCapture(*queue, frames);
                }
                if (ready && !capture) {
                    // A hardware callback consumes all frames available for
                    // its elapsed device time. Drain a bounded queue here too:
                    // one pop per Sleep would accumulate stale output when a
                    // VM schedules this worker less often than the engine.
                    for (std::size_t block = 0; block < queue->capacity() / am::quantumFrames && queue->size(); ++block) {
                        const auto count = queue->pop(frames);
                        if (!count) break;
                        const float heard = frames[count - 1].left;
                        if (current == "out1") { test::heard1.store(heard); ++test::render1; }
                        if (current == "out2") { test::heard2.store(heard); ++test::render2; }
                        if (current == "virtual-out") { test::heardB.store(heard); ++test::renderB; }
                    }
                }
                test::wait(test::workerWakeMilliseconds.load());
            }
        });
    }
    ~Impl() {
        stopped.store(true); if (thread.joinable()) thread.join();
        std::lock_guard lock(test::portsMutex); if (!id.empty()) --test::openPorts[id]; --test::live;
    }
};
AudioWorker::AudioWorker(bool capture, bool, std::shared_ptr<FrameQueue> queue, DeviceCatalog&, std::string transport)
    : impl_(std::make_unique<Impl>(capture, std::move(queue))) { impl_->requiredTransport = std::move(transport); }
AudioWorker::~AudioWorker() = default;
void AudioWorker::configure(DeviceBinding binding) {
    if (test::failNextConfiguration.exchange(false)) throw std::runtime_error("Synthetic worker initialization failure");
    std::lock_guard lock(impl_->mutex); std::lock_guard portsLock(test::portsMutex);
    if (impl_->id == binding.id) return;
    if (!impl_->id.empty()) --test::openPorts[impl_->id];
    impl_->id = binding.id;
    if (!impl_->id.empty()) test::configuredTransports[impl_->id] = impl_->requiredTransport;
    if (!impl_->id.empty() && ++test::openPorts[impl_->id] > 1) ++test::duplicates;
}
void AudioWorker::retry() noexcept { impl_->queue->requestClear(); }
bool AudioWorker::ready() const noexcept {
    std::lock_guard lock(impl_->mutex);
    return !impl_->id.empty() && (impl_->id != "out1" || test::output1Available.load()) && (impl_->id != "out2" || test::output2Available.load());
}
PathState AudioWorker::status() const { PathState path; path.state = ready() ? "Running" : "Waiting"; path.channels = 2; return path; }
std::uint32_t AudioWorker::periodFrames() const noexcept { return quantumFrames; }
std::uint32_t AudioWorker::targetQueueFrames() const noexcept { return quantumFrames + quantumFrames / 2; }
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
                if (active) test::refillCapture(*queue, frames);
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
void streamedMicrophones(const std::filesystem::path& directory) {
    using namespace am;
    check(live == 0 && catalogEndpoints.empty(), "Streamed-input fixture must start without another catalog or worker");
    const auto path = directory / "streamed-input-settings.json";
    auto endpoint = [](std::string id, std::string direction, std::string transport, int formFactor) {
        Endpoint value;
        value.id = std::move(id); value.stableId = "stable-" + value.id; value.name = "Example streamed microphone";
        value.direction = std::move(direction); value.state = "Active"; value.transport = std::move(transport);
        value.isVirtual = value.transport != "physical"; value.formFactor = formFactor;
        value.channels = 2; value.rate = 48000;
        return value;
    };
    catalogEndpoints = {endpoint("example-streamed", "capture", "otherVirtual", 0),
        endpoint("example-unknown-driver", "capture", "physical", -1),
        endpoint("example-cable", "capture", "vbCable", 2),
        endpoint("example-streamed-playback", "render", "otherVirtual", 0),
        endpoint("example-vac-output", "render", "vacLite", 1)};
    auto alias = endpoint("example-stable-alias", "capture", "otherVirtual", 0);
    alias.stableId = "stable-example-streamed"; alias.channels = 1;
    catalogEndpoints.insert(catalogEndpoints.begin(), alias);
    auto old = endpoint("example-disabled-old", "capture", "otherVirtual", 0);
    old.state = "Disabled"; old.channels = 1; old.stableId = "stable-example-returned";
    catalogEndpoints.push_back(old);
    catalogEndpoints.push_back(endpoint("example-returned", "capture", "otherVirtual", 0));
    {
        Engine engine(path); engine.start();
        auto config = defaultConfiguration(); config.graph.nodes.clear(); config.graph.edges.clear();
        GraphNode microphone; microphone.id = "example-input"; microphone.kind = "microphone";
        config.graph.nodes = {microphone};
        for (const auto* id : {"example-streamed", "example-unknown-driver", "example-cable"}) {
            config.graph.nodes[0].device = {id, std::string("stable-") + id, "Example chosen microphone", -1};
            engine.configure(config, config.revision); ++config.revision;
            const auto persisted = engine.snapshot()["configuration"]["graph"]["nodes"][0]["device"];
            check(persisted["id"] == id && persisted["stableId"] == std::string("stable-") + id,
                "Actual engine validation accepts Windows capture from any vendor and persists the explicit exact/stable identities");
            std::lock_guard lock(portsMutex);
            check(configuredTransports.at(id).empty(),
                "Generic microphone workers must not retain a physical-driver transport restriction after validation");
        }
        config.graph.nodes[0].device = {"example-streamed", "stable-example-streamed", "Saved exact input", 1};
        engine.configure(config, config.revision); ++config.revision;
        check(engine.snapshot()["configuration"]["graph"]["nodes"][0]["device"]["id"] == "example-streamed",
            "Exact active capture identity wins over an earlier stable alias with an incompatible channel count");
        config.graph.nodes[0].device = {"example-disabled-old", "stable-example-returned", "Saved returning input", 1};
        engine.configure(config, config.revision); ++config.revision;
        check(engine.snapshot()["configuration"]["graph"]["nodes"][0]["device"]["id"] == "example-disabled-old",
            "A unique active stable replacement supplies validation metadata without overwriting the inactive saved exact ID");
        config.graph.nodes[0].device = {"", "stable-example-streamed", "Saved ambiguous input", 7};
        engine.configure(config, config.revision); ++config.revision;
        check(engine.snapshot()["configuration"]["graph"]["nodes"][0]["device"]["stableId"] == "stable-example-streamed",
            "Ambiguous stable-only capture retains the saved choice unresolved instead of validating an arbitrary alias");
        auto rejects = [&](std::string kind, std::string id, int channel = -1) {
            auto invalid = config; invalid.graph.nodes[0].kind = std::move(kind);
            invalid.graph.nodes[0].device = {std::move(id), "", "Example incompatible device", channel};
            bool rejected = false;
            try { engine.configure(invalid, config.revision); } catch (const std::exception&) { rejected = true; }
            check(rejected && engine.snapshot()["configuration"]["revision"] == config.revision,
                "Generic input support preserves direction, channel and dedicated cable validation without committing failed edits");
        };
        rejects("microphone", "example-streamed-playback");
        rejects("microphone", "example-streamed", 2);
        rejects("virtualInput", "example-streamed");
        rejects("virtualOutput", "example-streamed-playback");
        rejects("output", "example-streamed");
        rejects("output", "example-vac-output");
        config.graph.nodes[0].kind = "virtualOutput";
        config.graph.nodes[0].device = {"example-vac-output", "", "Example virtual output", -1};
        engine.configure(config, config.revision); ++config.revision;
        {
            std::lock_guard lock(portsMutex);
            check(configuredTransports.at("example-vac-output") == "vacLite",
                "Dedicated virtual microphone output retains its VAC transport and direct-render policy");
        }
        engine.stop(); check(live == 0, "Streamed-input fixture releases every synthetic worker");
    }
    catalogEndpoints.clear();
    for (const auto* suffix : {"", ".bak", ".new", ".established"}) std::filesystem::remove(path.string() + suffix);
    std::cout << "PASS streamed inputs: actual engine accepts generic Windows capture across vendor families, preserves exact/stable assignments and keeps direction/channel/cable restrictions; synthetic workers only\n";
}

void streamedOutputs(const std::filesystem::path& directory) {
    using namespace am;
    check(live == 0 && catalogEndpoints.empty(), "Streamed-output fixture must start without another catalog or worker");
    const auto path = directory / "streamed-output-settings.json";
    auto endpoint = [](std::string id, std::string direction, std::string transport, int formFactor) {
        Endpoint value;
        value.id = std::move(id); value.stableId = "stable-" + value.id; value.name = "Example audio endpoint";
        value.direction = std::move(direction); value.state = "Active"; value.transport = std::move(transport);
        value.isVirtual = value.transport != "physical"; value.formFactor = formFactor; value.channels = 2; value.rate = 48000;
        return value;
    };
    catalogEndpoints = {endpoint("example-input", "capture", "otherVirtual", 0),
        endpoint("out1", "render", "otherVirtual", 0), endpoint("out2", "render", "physical", 3),
        endpoint("example-vac", "render", "vacLite", 2), endpoint("example-vb", "render", "vbCable", 2)};
    auto config = defaultConfiguration(); config.graph.nodes.clear(); config.graph.edges.clear();
    GraphNode input; input.id = "source"; input.kind = "microphone";
    input.device = {"example-input", "stable-example-input", "Example streamed microphone", -1};
    GraphNode output; output.id = "streamed-output"; output.kind = "output"; output.gainDb = -6.0206f;
    output.device = {"out1", "stable-out1", "Example streamed headset", -1};
    GraphNode local; local.id = "local-output"; local.kind = "output";
    local.device = {"out2", "stable-out2", "Example headphones", -1};
    config.graph.nodes = {input, output, local};
    config.graph.edges = {{"streamed-route", input.id, output.id}, {"local-route", input.id, local.id}};
    const auto initialDuplicates = duplicates.load();
    output1Available = output2Available = true;
    {
        const int previousRender1 = render1, previousRender2 = render2;
        Engine engine(path); engine.start();
        engine.configure(config, config.revision); ++config.revision;
        check(eventually([&] { return render1 > previousRender1 && render2 > previousRender2
                && std::abs(heard1.load() - .15f) < .003f && std::abs(heard2.load() - .3f) < .003f; }),
            "Actual engine renders the streamed input to both the selected streamed output and independent physical output at their own levels");
        {
            std::lock_guard lock(portsMutex);
            check(configuredTransports.at("out1") == "listening" && configuredTransports.at("out2") == "listening",
                "Both ordinary and streamed listening outputs use the listening policy rather than a physical-driver restriction");
        }
        const int workers = created;
        config.graph.nodes[1].muted = true;
        engine.configure(config, config.revision); ++config.revision;
        check(eventually([&] { return std::abs(heard1.load()) < .0001f && std::abs(heard2.load() - .3f) < .003f; }) && created == workers,
            "Muting the streamed output silences only its rendered queue and keeps existing workers");
        config.graph.nodes[1].muted = false;
        engine.configure(config, config.revision); ++config.revision;
        check(eventually([&] { return std::abs(heard1.load() - .15f) < .003f; }), "Unmuting restores the streamed route");
        output1Available = false; wait(30);
        const int stoppedWrites = render1, healthyWrites = render2;
        wait(60);
        check(render1 == stoppedWrites && render2 > healthyWrites && created == workers,
            "Streamed output loss stops its renderer while the other output continues without worker recreation");
        config.graph.nodes[1].gainDb = -12.0412f;
        engine.configure(config, config.revision); ++config.revision;
        output1Available = true;
        check(eventually([&] { return render1 > stoppedWrites && std::abs(heard1.load() - .075f) < .003f
                && std::abs(heard2.load() - .3f) < .003f; }),
            "The returning streamed renderer receives its current route level while the other path is unchanged");
        for (const auto* id : {"example-input", "example-vac", "example-vb"}) {
            auto invalid = config; invalid.graph.nodes[1].device = {id, "", "Example incompatible destination", -1};
            bool rejected{}; try { engine.configure(invalid, config.revision); } catch (const std::exception&) { rejected = true; }
            check(rejected && engine.snapshot()["configuration"]["revision"] == config.revision,
                "Listening output still rejects capture and dedicated routing cable endpoints without changing the live graph");
        }
        auto duplicate = config; duplicate.graph.nodes[2].device = config.graph.nodes[1].device;
        bool rejected{}; try { engine.configure(duplicate, config.revision); } catch (const std::exception&) { rejected = true; }
        check(rejected && duplicates == initialDuplicates, "A streamed render endpoint retains single-output ownership");
        check(Json(ConfigurationStore(path).load()) == Json(config),
            "The actual persisted workspace contains streamed input/output identities, routes, gain and independent output state");
        engine.stop(); check(live == 0, "Streamed-output stop releases every synthetic worker");
    }
    {
        const int previousRender1 = render1, previousRender2 = render2;
        Engine reopened(path); reopened.start();
        check(reopened.snapshot()["configuration"] == Json(config), "Full reopen restores the saved streamed output assignment and graph");
        check(eventually([&] { return render1 > previousRender1 && render2 > previousRender2
                && std::abs(heard1.load() - .075f) < .003f && std::abs(heard2.load() - .3f) < .003f; }),
            "Reopened streamed and physical routes render their saved levels");
        reopened.stop(); check(live == 0 && duplicates == initialDuplicates, "Reopening streamed playback leaks no workers or duplicate device owners");
    }
    catalogEndpoints.clear();
    for (const auto* suffix : {"", ".bak", ".new", ".established"}) std::filesystem::remove(path.string() + suffix);
    std::cout << "PASS streamed outputs: real engine graph rendering through synthetic workers, level/mute isolation, loss/return, duplicate/cable rejection, persistence and full reopen; no live endpoints opened\n";
}

void defaultSettings(const std::filesystem::path& directory) {
    using namespace am;
    check(live == 0, "Default-settings fixture must start without another engine's workers");
    const auto fixture = directory / L"default local app data";
    const auto application = fixture / L"VineMix";
    const auto working = directory / L"unrelated working directory";
    const auto path = application / L"settings.json";
    std::filesystem::create_directories(application);
    std::filesystem::create_directory(working);
    auto config = defaultConfiguration();
    config.revision = 6371;
    config.graph.nodes.clear(); config.graph.edges.clear();
    GraphNode microphone; microphone.id = "saved-source"; microphone.kind = "microphone";
    microphone.name = "Saved custom microphone"; microphone.x = -137.25; microphone.y = 283.75;
    microphone.device = {"synthetic-microphone", "synthetic-stable-id", "Saved device", -1};
    microphone.mono = true;
    GraphNode gain; gain.id = "saved-gain"; gain.kind = "gain"; gain.name = "Saved gain";
    gain.gainDb = -7.5f; gain.x = 421.25; gain.y = 283.75;
    GraphNode output; output.id = "saved-output"; output.kind = "output"; output.name = "Saved output";
    output.muted = true; output.x = 987.5; output.y = 283.75;
    config.graph.nodes = {microphone, gain, output};
    config.graph.edges = {{"saved-input-route", microphone.id, gain.id}, {"saved-output-route", gain.id, output.id}};
    config.graph.panX = -125.5; config.graph.panY = 91.25; config.graph.zoom = .65;
    ConfigurationStore(path).save(config);
    auto decoy = defaultConfiguration(); decoy.revision = 9000;
    ConfigurationStore(working / L"settings.json").save(decoy);
    {
        // Only this synthetic executable's environment and current directory
        // change. Restore both even when an assertion or engine startup fails.
        struct RestoreLaunchContext {
            std::filesystem::path previousDirectory = std::filesystem::current_path();
            std::wstring previousLocalAppData;
            bool hadLocalAppData{};
            RestoreLaunchContext() {
                const DWORD required = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
                hadLocalAppData = required != 0;
                if (!required) {
                    check(GetLastError() == ERROR_ENVVAR_NOT_FOUND, "Read the previous test-process LOCALAPPDATA state");
                    return;
                }
                previousLocalAppData.resize(required);
                check(GetEnvironmentVariableW(L"LOCALAPPDATA", previousLocalAppData.data(), required) == required - 1,
                    "Read the previous test-process LOCALAPPDATA value");
                previousLocalAppData.resize(required - 1);
            }
            ~RestoreLaunchContext() {
                SetEnvironmentVariableW(L"LOCALAPPDATA", hadLocalAppData ? previousLocalAppData.c_str() : nullptr);
                std::error_code ignored;
                std::filesystem::current_path(previousDirectory, ignored);
            }
        } restore;
        check(SetEnvironmentVariableW(L"LOCALAPPDATA", fixture.c_str()) != FALSE,
            "Redirect default settings into a new test-only application-data directory");
        std::filesystem::current_path(working);
        check(applicationDataDirectory() == application, "Default application data must resolve to the test-only absolute directory");
        const auto verify = [&](Engine& engine) {
            const auto snapshot = engine.snapshot();
            check(snapshot.at("configuration") == Json(config),
                "A default engine must restore the exact custom v2 graph, revision, controls, device and camera");
            check(snapshot.at("warning") == "", "Valid default-path settings must load without a recovery warning");
            check(snapshot.at("settingsPath") == narrow(path.wstring())
                && snapshot.at("logPath") == narrow((application / L"engine.log").wstring()),
                "Default settings and logging must use the application-data folder independently of the working directory");
        };
        {
            Engine implicit;
            Engine explicitPath(path);
            verify(implicit); verify(explicitPath);
            check(live == 0, "Reading either startup configuration must not create audio workers");
            implicit.start(); verify(implicit);
            config.graph.nodes[1].gainDb = -11.25f;
            config.graph.nodes[0].x = -211.75;
            implicit.configure(config, config.revision); ++config.revision;
            verify(implicit);
            implicit.stop();
            check(live == 0, "Quitting the default-path engine must release all synthetic workers");
        }
        // A full engine reconstruction, from a second launch directory, must
        // load the last committed custom workspace rather than fresh defaults.
        std::filesystem::current_path(directory);
        {
            Engine reopened;
            verify(reopened);
            reopened.start(); verify(reopened);
            reopened.stop();
            check(live == 0, "Reopening the default-path engine must not leak synthetic workers");
        }
        check(Json(ConfigurationStore(path).load()) == Json(config), "Full quit and reopen must retain the last custom settings on disk");
        check(ConfigurationStore(working / L"settings.json").load().revision == decoy.revision,
            "Default-path startup must not read or overwrite the working-directory settings decoy");
        check(!std::filesystem::exists(directory / L"VineMix") && !std::filesystem::exists(working / L"VineMix"),
            "Default-path startup must not create a settings folder relative to either launch directory");
    }
    for (const auto& name : {L"settings.json", L"settings.json.bak", L"settings.json.new", L"settings.json.established", L"engine.log", L"engine.log.previous"})
        std::filesystem::remove(application / name);
    std::filesystem::remove(working / L"settings.json");
    std::filesystem::remove(working / L"settings.json.established");
    std::filesystem::remove(working); std::filesystem::remove(application); std::filesystem::remove(fixture);
}
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
    for (const auto& name : {"settings.json", "settings.json.bak", "settings.json.new", "settings.json.established", "engine.log", "engine.log.previous"})
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
void ownershipLocks() {
    using namespace am;
    const auto identity = L"OfflineVerification-" + std::to_wstring(GetCurrentProcessId());
    const auto rejectsInstance = [&] {
        bool rejected = false;
        try { EngineInstance competing(identity); } catch (const std::exception&) { rejected = true; }
        check(rejected, "Either existing product lock must reject another engine before settings are opened");
    };
    for (const auto* product : {L"AudioMixer", L"VineMix"}) {
        const auto name = L"Local\\" + std::wstring(product) + L".Engine." + identity;
        HANDLE legacy = CreateMutexW(nullptr, TRUE, name.c_str());
        check(legacy != nullptr, "Create private synthetic product lock");
        rejectsInstance(); ReleaseMutex(legacy); CloseHandle(legacy);
        EngineInstance available(identity); rejectsInstance();
    }
    EngineInstance available(identity); // Rejected acquisition must not leak a lock.
}
void controlProtocol(const std::filesystem::path& directory) {
    using namespace am;
    const auto path = directory / "protocol-settings.json";
    const auto read = [&] {
        std::ifstream stream(path, std::ios::binary); std::ostringstream bytes; bytes << stream.rdbuf(); return bytes.str();
    };
    {
        Engine engine(path); engine.start();
        ControlServer server(engine);
        // Parse wire-format JSON so positive numbers also exercise nlohmann's
        // unsigned representation, as real desktop requests do.
        auto request = Json::parse(Json{{"protocolVersion", 1}, {"requestId", "private-fixture"},
            {"command", "configure"}, {"expectedRevision", 0}, {"configuration", defaultConfiguration()}}.dump());
        const auto accepted = server.handle(request);
        check(accepted.value("ok", false) && accepted["snapshot"]["configuration"]["revision"] == 1,
            "An unsigned integer protocol version and revision commit through the actual command dispatcher");
        request["expectedRevision"] = 1;
        const auto committed = read();
        const auto rejectsUnchanged = [&](const Json& malformed) {
            const auto response = server.handle(malformed);
            check(!response.value("ok", true) && response.contains("error") && !response.contains("errorCode")
                && engine.snapshot()["configuration"]["revision"] == 1 && read() == committed,
                "Malformed control input returns a protocol error without changing revision or saved bytes");
        };
        for (const auto& version : std::array<Json, 8>{Json(1.5), Json(1.0), Json(std::uint64_t{4294967297}),
            Json(-1), Json(0), Json("1"), Json(true), Json(nullptr)}) {
            auto malformed = request; malformed["protocolVersion"] = version; rejectsUnchanged(malformed);
        }
        for (const auto& revision : std::array<Json, 7>{Json(-1), Json(1.5), Json(1.0), Json("1"), Json(true), Json(nullptr), Json(1e30)}) {
            auto malformed = request; malformed["expectedRevision"] = revision; rejectsUnchanged(malformed);
        }
        for (const auto& malformed : std::array<Json, 5>{Json(nullptr), Json(1), Json("request"), Json::array(), Json::object()})
            rejectsUnchanged(malformed);
        auto invalidSchema = request; invalidSchema["configuration"]["version"] = 999;
        rejectsUnchanged(invalidSchema);
        auto invalidGraph = request; invalidGraph["configuration"]["graph"]["nodes"][0]["gainDb"] = 13;
        rejectsUnchanged(invalidGraph);
        Configuration oversized; migrateLegacyGraph(oversized);
        for (auto& node : oversized.graph.nodes) node.app.executablePath = std::string(32768, 'x');
        auto invalidSize = request; invalidSize["configuration"] = oversized;
        rejectsUnchanged(invalidSize);
        engine.stop();
        check(live == 0, "Control verification closes all synthetic workers");
    }
    for (const auto* suffix : {"", ".bak", ".new", ".established"}) std::filesystem::remove(path.string() + suffix);
    std::cout << "PASS control: exact integer protocol/revision validation; 23 malformed requests preserve saved bytes/revision and remain permanent errors; direct dispatch with synthetic workers and no pipe\n";
}
void persistenceProtocol(const std::filesystem::path& directory) {
    using namespace am;
    const auto path = directory / "persistence-settings.json";
    const auto backup = std::filesystem::path(path.wstring() + L".bak");
    const auto read = [](const std::filesystem::path& file) {
        std::ifstream stream(file, std::ios::binary); std::ostringstream bytes; bytes << stream.rdbuf(); return bytes.str();
    };
    auto initial = defaultConfiguration(); initial.revision = 7;
    ConfigurationStore store(path); store.save(initial); ++initial.revision; store.save(initial);
    const auto committed = read(path), priorBackup = read(backup);
    {
        Engine engine(path); engine.start(); ControlServer server(engine);
        auto edited = initial; edited.graph.nodes[0].x += 32;
        const auto request = Json::parse(Json{{"protocolVersion", 1}, {"requestId", "private-persistence-fixture"},
            {"command", "configure"}, {"expectedRevision", initial.revision}, {"configuration", edited}}.dump());
        const auto previous = engine.snapshot()["configuration"];
        const int workers = created;
        for (const bool lockPrimary : {false, true}) {
            const auto lockedPath = lockPrimary ? path : std::filesystem::path(path.wstring() + L".new");
            struct FileLock { HANDLE value; ~FileLock() { if (value != INVALID_HANDLE_VALUE) CloseHandle(value); } } locked{
                CreateFileW(lockedPath.c_str(), lockPrimary ? GENERIC_READ : GENERIC_READ | GENERIC_WRITE,
                    lockPrimary ? FILE_SHARE_READ : 0, nullptr, lockPrimary ? OPEN_EXISTING : OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr)};
            check(locked.value != INVALID_HANDLE_VALUE, "Lock only the temporary persistence fixture");
            const auto response = server.handle(request);
            check(!response.value("ok", true) && response.value("errorCode", "") == "settings_write_failed"
                && response.contains("error"), "Staging/replacement I/O failure returns its explicit retryable error class");
            check(engine.snapshot()["configuration"] == previous && read(path) == committed && read(backup) == priorBackup
                && created == workers, "Rejected persistence preserves committed engine state, settings, backup and healthy synthetic workers");
        }
        const auto retried = server.handle(request);
        check(retried.value("ok", false) && !retried.contains("errorCode")
            && retried["snapshot"]["configuration"]["revision"] == initial.revision + 1
            && retried["snapshot"]["configuration"]["graph"]["nodes"][0]["x"] == edited.graph.nodes[0].x
            && ConfigurationStore(path).load().revision == initial.revision + 1 && read(backup) == committed,
            "The identical request succeeds after releasing the file lock, advances one revision and retains the previous commit");
        engine.stop(); check(live == 0, "Persistence verification closes every synthetic worker");
    }
    for (const auto* suffix : {"", ".bak", ".new", ".established"}) std::filesystem::remove(path.string() + suffix);
    std::cout << "PASS persistence: locked staging and replacement preserve config/revision/backup; typed I/O response and identical-request retry; synthetic workers only\n";
}
void recovery(const std::filesystem::path& directory) {
    using namespace am;
    check(live == 0, "Recovery fixture must start without workers");
    const auto root = directory / "recovery";
    std::filesystem::create_directory(root);
    const auto path = root / "settings.json";
    const auto nested = root / "not-created" / "nested" / "settings.json";
    const auto checkUncreatedPath = [&](const std::filesystem::path& candidate) {
        const auto physical = ConfigurationStore(candidate).physicalPath();
        const auto physicalNested = physical.parent_path();
        const auto physicalMissing = physicalNested.parent_path();
        // TEMP can use a short name, different case or a redirected location.
        // Compare the opened ancestor's identity, never its logical spelling.
        check(physical.filename() == "settings.json" && physicalNested.filename() == "nested"
            && physicalMissing.filename() == "not-created"
            && std::filesystem::equivalent(physicalMissing.parent_path(), root)
            && !std::filesystem::exists(nested.parent_path()) && !std::filesystem::exists(root / "not-created"),
            "A new nested settings path resolves through its physical ancestor without creating directories");
    };
    checkUncreatedPath(nested);
    const auto caseAlias = root.parent_path() / "ReCoVeRy";
    if (std::filesystem::exists(caseAlias))
        checkUncreatedPath(caseAlias / "not-created" / "nested" / "settings.json");
    const auto write = [&](const auto& file, const auto& text) {
        std::ofstream stream(file, std::ios::binary); stream << text;
        check(static_cast<bool>(stream), "Write isolated recovery fixture");
    };
    const auto read = [](const auto& file) {
        std::ifstream stream(file, std::ios::binary);
        return std::string(std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>());
    };
    const auto rejects = [](auto action) {
        bool rejected = false; try { action(); } catch (const std::exception&) { rejected = true; }
        check(rejected, "Unsafe recovery/configuration operation must be rejected");
    };
    Configuration saved; migrateLegacyGraph(saved); saved.revision = 11;
    saved.graph.nodes[0].device.id = "recovery-mic";
    ConfigurationStore store(path); store.save(saved); ++saved.revision; store.save(saved);
    const auto backup = read(path.string() + ".bak");
    write(path, "broken original");
    {
        const int before = created;
        Engine blocked(path); blocked.start();
        check(blocked.snapshot()["settingsState"] == "backupAvailable" && created == before && live == 0
            && blocked.snapshot()["status"]["nodes"].empty(), "Backup preview starts zero synthetic audio ports");
        const auto blockedResponse = ControlServer(blocked).handle(Json{{"protocolVersion", 1}, {"command", "configure"},
            {"expectedRevision", 11}, {"configuration", saved}});
        check(!blockedResponse.value("ok", true) && !blockedResponse.contains("errorCode"),
            "A pending recovery choice remains a permanent rejection instead of triggering automatic persistence retries");
        rejects([&] { blocked.configure(saved, 11); });
        blocked.retry(); blocked.recover("retry");
        check(created == before && read(path) == "broken original" && read(path.string() + ".bak") == backup,
            "Recovery retry and rejected configuration leave audio closed and preserve originals");
        failNextConfiguration = true;
        rejects([&] { blocked.recover("restoreBackup"); });
        check(live == 0 && blocked.snapshot()["settingsState"] == "recoveryRequired"
            && blocked.snapshot()["status"]["nodes"].empty(), "Recovery worker failure closes every partial port and keeps saving paused");
        const int afterFailure = created;
        rejects([&] { blocked.configure(saved, 11); });
        check(created == afterFailure, "Configuration remains blocked after a recovered workspace fails audio startup");
        blocked.recover("retry");
        const auto snapshot = blocked.snapshot();
        check(snapshot["settingsState"] == "loaded" && snapshot["warning"] == "" && live > 0
            && snapshot["configuration"]["revision"] == 11, "Retry after failed recovery can load the committed backup and start audio safely");
        check(snapshot["enginePid"] == GetCurrentProcessId() && snapshot["engineProtocolVersion"] == 1
            && !snapshot["executablePath"].get<std::string>().empty()
            && std::filesystem::equivalent(std::filesystem::path(widen(snapshot["physicalSettingsPath"].get<std::string>())), path),
            "Snapshot identifies its process and actual opened settings file");
        blocked.stop(); check(live == 0, "Recovered engine shutdown closes every synthetic worker");
    }
    write(path, "broken fresh original"); write(path.string() + ".bak", "broken fresh backup");
    write(path.string() + ".new", "orphaned newer staging bytes");
    {
        const int before = created;
        Engine blocked(path); blocked.start();
        check(blocked.snapshot()["settingsState"] == "recoveryRequired" && created == before && live == 0,
            "Two corrupt copies do not start ports or adopt defaults");
        rejects([&] { blocked.configure(defaultConfiguration(), 0); });
        blocked.recover("startFresh");
        check(blocked.snapshot()["settingsState"] == "loaded"
            && Json(ConfigurationStore(path).load().graph) == Json(defaultConfiguration().graph),
            "Explicit start-fresh persists the exact conservative starter graph");
        blocked.stop();
    }
    auto future = Json(saved); future["version"] = 999; write(path, future.dump());
    {
        const int before = created;
        Engine blocked(path); blocked.start();
        rejects([&] { blocked.recover("startFresh"); });
        rejects([&] { blocked.recover("restoreBackup"); });
        rejects([&] { blocked.configure(saved, 0); });
        check(blocked.snapshot()["settingsState"] == "unsupported" && created == before && live == 0
            && read(path) == future.dump(), "Newer schema blocks writes, destructive recovery and every audio port");
        blocked.stop();
    }
    std::filesystem::remove(path); std::filesystem::remove(path.string() + ".bak");
    {
        const int before = created;
        Engine missing(path); missing.start();
        check(missing.snapshot()["settingsState"] == "recoveryRequired" && created == before,
            "Established workspace with both files missing cannot silently become a new workspace");
        missing.stop();
    }
    std::filesystem::remove(path.string() + ".established");
    write(path.string() + ".new", "interrupted first commit");
    {
        const int before = created;
        Engine orphaned(path); orphaned.start();
        rejects([&] { orphaned.configure(defaultConfiguration(), 0); });
        check(orphaned.snapshot()["settingsState"] == "recoveryRequired" && created == before
            && read(path.string() + ".new") == "interrupted first commit",
            "Orphaned first-commit staging files cannot be mistaken for a new workspace or overwritten");
        orphaned.stop();
    }
    // Remove only this fixture's known files and explicitly inspected recovery
    // artifacts. Directory removal fails if any unexpected content remains.
    bool preservedBackup = false, preservedFresh = false;
    for (const auto& entry : std::filesystem::directory_iterator(root)) {
        const auto name = entry.path().filename().string();
        if (entry.is_directory() && name.starts_with("settings.json.recovery-")) {
            const auto original = read(entry.path() / "settings.json");
            const auto originalBackup = read(entry.path() / "settings.json.bak");
            preservedBackup |= original == "broken original" && originalBackup == backup;
            preservedFresh |= original == "broken fresh original" && originalBackup == "broken fresh backup"
                && read(entry.path() / "settings.json.new") == "orphaned newer staging bytes";
            for (const auto* file : {"settings.json", "settings.json.bak", "settings.json.established", "settings.json.new", "settings.json.v1.bak"})
                std::filesystem::remove(entry.path() / file);
            std::filesystem::remove(entry.path());
        } else if (name.starts_with("settings.json.corrupt-")) std::filesystem::remove(entry.path());
    }
    check(preservedBackup && preservedFresh, "Both explicit recovery choices preserve the exact original and backup bytes together");
    for (const auto* file : {"settings.json", "settings.json.bak", "settings.json.new", "settings.json.established", "engine.log", "engine.log.previous"})
        std::filesystem::remove(root / file);
    std::filesystem::remove(root);
    std::cout << "PASS recovery: backup preview, corrupt/missing established workspace and future schema open no ports; configuration is blocked; preserved recovery, worker-failure cleanup and retry; dual legacy/current engine locks use private test names\n";
}
}
int main() {
    using namespace am;
    try {
        const auto directory = std::filesystem::temp_directory_path() / (L"AudioMixer-OfflineEngine-" + std::to_wstring(GetCurrentProcessId()));
        test::check(!std::filesystem::exists(directory), "Test directory already exists");
        std::filesystem::create_directories(directory);
        const auto path = directory / "settings.json";
        Configuration config; migrateLegacyGraph(config);
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
        GraphNode gate; gate.id = "gate"; gate.kind = "gate"; gate.expander.mode = "gate";
        const auto gateIndex = config.graph.nodes.size(); config.graph.nodes.push_back(gate);
        config.graph.edges.push_back({"gate-input", "mic1", "gate"});
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
        GraphNode layoutDenoiser; layoutDenoiser.id = "layout-denoiser"; layoutDenoiser.kind = "denoiser";
        config.graph.nodes.push_back(layoutDenoiser);
        GraphNode spectrumEq; spectrumEq.id = "spectrum-eq"; spectrumEq.kind = "eq";
        config.graph.nodes.push_back(spectrumEq);
        config.graph.edges.push_back({"spectrum-input", "mic1", "spectrum-eq"});
        ConfigurationStore(path).save(config);
        std::uint64_t previousEngineSpectrumRevision{};
        {
            Engine engine(path); engine.start();
            // A loaded machine can delay new synthetic worker threads beyond
            // one fixed Sleep. Wait for actual fanout, retaining a hard bound.
            const bool fanoutReady = test::eventually([&] {
                // Enough consumed quanta also primes the 4096-frame analyzer.
                return test::render1 > 20 && test::render2 > 20 && test::renderB > 20;
            });
            const auto initial = engine.snapshot();
            test::check(initial["status"]["audioFrames"].get<std::uint64_t>() > 0, "Audio clock stalled");
            test::check(fanoutReady, "Initial synthetic fanout failed");
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
            // Exercise coarse capture AND renderer wakes independently of the
            // engine's 5 ms timer, as can occur on a loaded Windows VM.
            test::workerWakeMilliseconds = 20;
            const auto observeColor = [&](double tone, float expected, const char* reason) {
                const auto priorFrames = engine.snapshot()["status"]["audioFrames"].get<std::uint64_t>();
                config.graph.nodes[voiceColorIndex].voiceColor = {tone, 0};
                config.graph.nodes[voiceColorIndex].bypass = false;
                engine.configure(config, config.revision); ++config.revision;
                int stable{};
                int rendered = test::render1.load(), observedRender = rendered;
                std::uint64_t observedFrames = priorFrames, currentFrames = priorFrames;
                float measured{}, heard{};
                const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
                while (stable < 3 && std::chrono::steady_clock::now() < deadline) {
                    test::wait(10);
                    rendered = test::render1.load();
                    if (rendered == observedRender) continue;
                    const auto snapshot = engine.snapshot();
                    // Clear historical peak accumulation while the runtime,
                    // filter ramps and queued output settle. Count only
                    // observations with fresh engine AND consumed output
                    // blocks; repeated polls of a stale heard value do not pass.
                    currentFrames = snapshot["status"]["audioFrames"].get<std::uint64_t>();
                    observedRender = rendered;
                    if (currentFrames <= observedFrames) continue;
                    observedFrames = currentFrames;
                    if (currentFrames < priorFrames + sampleRate / 8) continue;
                    measured = snapshot["status"]["nodes"][voiceColorIndex]["peakLeft"].get<float>();
                    heard = test::heard1.load();
                    const bool matched = std::abs(measured - expected) < expected * .025f
                        && std::abs(heard - expected) < expected * .025f
                        && snapshot["configuration"]["revision"].get<std::uint64_t>() == config.revision;
                    stable = matched ? stable + 1 : 0;
                }
                const auto failure = std::string(reason) + "; expected=" + std::to_string(expected)
                    + ", measured=" + std::to_string(measured) + ", heard=" + std::to_string(heard)
                    + ", stable=" + std::to_string(stable) + ", audioFrames=" + std::to_string(currentFrames)
                    + ", renderedBlocks=" + std::to_string(rendered);
                test::check(stable >= 3, failure.c_str());
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
            test::workerWakeMilliseconds = 5;
            std::cout << "Live Voice Color rendered levels (20 ms synthetic worker wakes): neutral=" << neutralColor << "; Warm=" << warmColor
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
                config.graph.nodes[gateIndex].expander.thresholdDb = -40 + i % 12;
                config.graph.nodes[delayIndex].alignmentDelay.samples = static_cast<std::uint32_t>(i * 48);
                config.graph.nodes[dynamicsIndex].compressor.mode = i % 2 ? "compress" : "limit";
                config.graph.nodes[duckingIndex].ducking.amountDb = 12 + i % 12;
                config.graph.nodes[duckingIndex].ducking.mode = i % 2 ? "dynamic" : "static";
                config.graph.nodes[duckingIndex].ducking.ratio = 2 + i % 8;
                config.graph.nodes[limiterIndex].compressor.ceilingDb = -6 - i % 12;
                engine.configure(config, config.revision); ++config.revision; test::wait(7);
            }
            test::wait(100);
            const auto additions = engine.snapshot();
            test::check(test::created == existingWorkers && additions["status"]["audioFrames"].get<std::uint64_t>() > colorSnapshot["status"]["audioFrames"].get<std::uint64_t>(),
                "Ducking, Limit, Gate and Alignment live commits preserve healthy workers and advancing timeline");
            for (const auto index : {expansionIndex, gateIndex, delayIndex, dynamicsIndex, duckingIndex, limiterIndex})
                test::check(additions["status"]["nodes"][index]["path"]["state"] == "Running" && additions["status"]["nodes"][index]["peak"].get<float>() > 0,
                    "Every new processing path runs and meters in the actual engine");
            test::check(additions["status"]["nodes"][expansionIndex]["dynamicsFrame"].get<std::uint64_t>() > 0
                && additions["status"]["nodes"][gateIndex]["dynamicsFrame"].get<std::uint64_t>() > 0
                && additions["status"]["nodes"][dynamicsIndex]["dynamicsFrame"].get<std::uint64_t>() > 0,
                "Dedicated Gate, legacy gate-mode Expander and own-signal Compressor publish current dynamics telemetry");
            for (const auto index : {duckingIndex, limiterIndex})
                test::check(additions["status"]["nodes"][index]["dynamicsFrame"].get<std::uint64_t>() > 0
                    && additions["status"]["nodes"][index]["gainReductionDb"].get<float>() > 0,
                    "Dedicated Ducking and Limiter publish real attenuation and current telemetry");
            const auto additionsSaved = ConfigurationStore(path).load();
            test::check(additionsSaved.graph.nodes[expansionIndex].expander == config.graph.nodes[expansionIndex].expander
                && additionsSaved.graph.nodes[gateIndex].kind == "gate"
                && additionsSaved.graph.nodes[gateIndex].expander == config.graph.nodes[gateIndex].expander
                && additionsSaved.graph.nodes[delayIndex].alignmentDelay == config.graph.nodes[delayIndex].alignmentDelay
                && Json(additionsSaved.graph.nodes[dynamicsIndex].compressor) == Json(config.graph.nodes[dynamicsIndex].compressor)
                && std::any_of(additionsSaved.graph.edges.begin(), additionsSaved.graph.edges.end(), [](const auto& edge) {
                    return edge.id == "ducking-trigger" && edge.kind == "sidechain"; }),
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
        test::defaultSettings(directory);
        test::relativeSettings(directory);
        test::ownershipLocks();
        test::recovery(directory);
        test::streamedMicrophones(directory);
        test::streamedOutputs(directory);
        test::controlProtocol(directory);
        test::persistenceProtocol(directory);
        for (const auto& fixture : {path, sharedPath})
            for (const auto& suffix : {"", ".bak", ".new", ".established"}) std::filesystem::remove(fixture.string() + suffix);
        std::filesystem::remove(directory / "engine.log"); std::filesystem::remove(directory / "engine.log.previous");
        std::filesystem::remove(directory);
        std::cout << "PASS: actual engine survived 301 graph commits, Core Dynamics and measured Voice Color output changes/live persistence without worker restart, canvas-only saves without DSP rebuilds, concurrent snapshots, measured EQ spectrum lifecycle, topology fades, rapid device rebinding, isolated output loss/rejoin, retirement, restart and failed-start cleanup; fresh and saved duplicate microphone/virtual/app sources share capture with independent controls/routes while output uniqueness remains enforced; implicit default-path startup and full quit/reopen preserve a custom v2 workspace independently of launch directory. Synthetic worker stubs only.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
