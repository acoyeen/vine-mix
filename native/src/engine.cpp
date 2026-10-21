#include "am/engine.hpp"
#include "am/audio_timing.hpp"
#include "am/mix_clock.hpp"
#include "am/process_capture.hpp"
#include "am/spectrum.hpp"
#include "am/version.hpp"
#include <windows.h>
#include <avrt.h>
#include <algorithm>
#include <fstream>
#include <iomanip>
#include <limits>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>

namespace am {
namespace {
void raisePeak(std::atomic<float>& target, float value) noexcept {
    float previous = target.load(std::memory_order_relaxed);
    while (previous < value && !target.compare_exchange_weak(previous, value, std::memory_order_relaxed)) {}
}
bool assigned(const DeviceBinding& binding) { return !binding.id.empty() || !binding.stableId.empty(); }
bool sameDevice(const DeviceBinding& a, const DeviceBinding& b) noexcept {
    return a.id == b.id && a.stableId == b.stableId && a.channel == b.channel;
}
bool sameProcess(const ProcessBinding& a, const ProcessBinding& b) noexcept {
    return a.processId == b.processId && a.executablePath == b.executablePath;
}
}
struct Engine::Port {
    GraphNode identity;
    std::shared_ptr<FrameQueue> queue = std::make_shared<FrameQueue>(9600);
    CaptureBufferReader reader; // Mixer-thread consumer state, retained with the shared source.
    std::unique_ptr<AudioWorker> device;
    std::unique_ptr<ProcessCaptureWorker> app;
    bool activated{};
    Port(const GraphNode& node, GraphKind kind, DeviceCatalog& catalog) : identity(node) {
        if (kind == GraphKind::appInput) app = std::make_unique<ProcessCaptureWorker>(queue);
        else device = std::make_unique<AudioWorker>(isSource(kind), kind == GraphKind::microphone, queue, catalog,
            kind == GraphKind::virtualInput ? "vbCable" : kind == GraphKind::virtualOutput ? "vacLite"
                : kind == GraphKind::microphone ? "" : "physical");
    }
    bool matches(const GraphNode& node) const noexcept {
        return identity.kind == node.kind
            && sameDevice(identity.device, node.device) && sameProcess(identity.app, node.app);
    }
    void activate() {
        if (activated) return;
        if (device) device->configure(identity.device);
        if (app) app->configure(identity.app);
        activated = true;
    }
    bool ready() const noexcept { return device ? device->ready() : app->ready(); }
    PathState status() const { return device ? device->status() : app->status(); }
    void retry() noexcept { if (device) device->retry(); else app->retry(); }
    void stop() noexcept { if (device) device->stop(); else app->stop(); }
};
struct Engine::Meter {
    std::atomic<float> peakLeft{}, peakRight{}, gainReductionDb{};
    std::atomic<float> detectorDb{-96};
    std::atomic<std::uint64_t> dynamicsFrame{};
    std::atomic<bool> clipped{};
};
struct Engine::Runtime {
    std::uint64_t generation{};
    GraphPlan plan;
    GraphProcessor processor;
    std::array<std::shared_ptr<Port>, maximumGraphNodes> ports;
    std::array<std::shared_ptr<Meter>, maximumGraphNodes> meters;
    std::array<std::shared_ptr<LiveSpectrum>, maximumGraphNodes> spectra;
};

Engine::Engine(std::filesystem::path settings)
    : store_(std::move(settings)), configuration_(store_.load()), warning_(store_.warning()) {
    logPath_ = store_.path().parent_path() / L"engine.log";
    // Frame counts restart at zero with the process. Seed visual continuity
    // identities from the system monotonic counter so a reconnected UI never
    // mistakes a new engine's spectrum for an older frame of the prior one.
    LARGE_INTEGER spectrumEpoch{}; QueryPerformanceCounter(&spectrumEpoch);
    spectrumRevision_.store(static_cast<std::uint64_t>(spectrumEpoch.QuadPart), std::memory_order_relaxed);
    stopEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stopEvent_) throw std::runtime_error("Cannot create the audio engine stop event.");
    endpoints_ = catalog_.enumerate();
}
Engine::~Engine() { stop(); if (stopEvent_) CloseHandle(stopEvent_); }
void Engine::start() {
    if (started_.exchange(true)) return;
    try {
        stopping_.store(false); ResetEvent(stopEvent_);
        { std::lock_guard lock(configurationMutex_); runtimes_.reserve(1);
          if (store_.writable()) publish(prepare(configuration_, compileGraph(configuration_.graph))); }
        mixThread_ = std::thread(&Engine::mixLoop, this);
        maintenanceThread_ = std::thread(&Engine::maintenanceLoop, this);
        writeLog("Canvas engine started. Device assignments are explicit; Windows defaults are unchanged.");
    } catch (...) {
        // A worker/thread creation failure must not leave a half-started
        // engine alive or make a later start silently do nothing.
        stop();
        throw;
    }
}
void Engine::requestStop() noexcept { stopping_.store(true); if (stopEvent_) SetEvent(stopEvent_); }
bool Engine::stopping() const noexcept { return stopping_.load(); }
void Engine::stop() {
    requestStop();
    if (mixThread_.joinable()) mixThread_.join();
    if (maintenanceThread_.joinable()) maintenanceThread_.join();
    std::lock_guard lock(configurationMutex_);
    published_.store(nullptr, std::memory_order_release);
    runtimes_.clear(); // shared ports stop and join here, never on the mixer thread
    started_.store(false);
}
std::vector<Endpoint> Engine::endpoints() { std::lock_guard lock(endpointsMutex_); return endpoints_; }
void Engine::validateEndpoints(const Configuration& config) {
    const auto devices = endpoints();
    for (const auto& node : config.graph.nodes) {
        if (!assigned(node.device)) continue;
        const bool capture = node.kind == "microphone" || node.kind == "virtualInput";
        const bool render = node.kind == "output" || node.kind == "virtualOutput";
        if (!capture && !render) continue;
        const std::string_view direction = capture ? "capture" : "render";
        const auto exact = std::find_if(devices.begin(), devices.end(), [&](const Endpoint& endpoint) {
            return !node.device.id.empty() && endpoint.id == node.device.id && endpoint.direction == direction;
        });
        auto match = exact != devices.end() && exact->state == "Active" ? exact : devices.end();
        // Match AudioWorker::resolve: an exact active ID wins even when a
        // stable ID has aliases; otherwise accept one active stable match.
        // Ambiguous stable identities remain saved and unresolved, rather than
        // validating or rejecting whichever alias happened to enumerate first.
        bool ambiguous{};
        if (match == devices.end() && !node.device.stableId.empty()) {
            for (auto candidate = devices.begin(); candidate != devices.end(); ++candidate) {
                if (candidate->direction != direction || candidate->state != "Active"
                    || candidate->stableId != node.device.stableId) continue;
                if (match != devices.end()) { ambiguous = true; break; }
                match = candidate;
            }
        }
        if (ambiguous) continue;
        if (match == devices.end()) match = exact;
        if (match == devices.end()) {
            if (!node.device.id.empty() && std::any_of(devices.begin(), devices.end(), [&](const Endpoint& endpoint) {
                return endpoint.id == node.device.id && endpoint.direction != direction;
            })) throw std::runtime_error("The device has the wrong audio direction for this node.");
            continue;
        }
        if (node.kind == "output" && match->isVirtual)
            throw std::runtime_error("Choose physical hardware for a listening output node.");
        if (node.kind == "virtualInput" && match->transport != "vbCable")
            throw std::runtime_error("Choose the base VB-CABLE receive endpoint for a virtual input.");
        if (node.kind == "virtualOutput" && match->transport != "vacLite")
            throw std::runtime_error("Choose the VAC Lite send endpoint for a virtual microphone output.");
        if (node.kind == "microphone" && match->channels && node.device.channel >= static_cast<int>(match->channels))
            throw std::runtime_error("The selected microphone channel is not available.");
    }
}
std::unique_ptr<Engine::Runtime> Engine::prepare(const Configuration& config, GraphPlan plan) {
    auto runtime = std::make_unique<Runtime>();
    runtime->plan = std::move(plan);
    runtime->processor.prepare(runtime->plan);
    runtime->generation = ++nextGeneration_;
    auto* previous = published_.load(std::memory_order_acquire);
    for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
        const auto& node = config.graph.nodes[i];
        if (previous) for (std::size_t j = 0; j < previous->plan.nodeCount; ++j) {
            if (previous->plan.nodes[j].id != node.id) continue;
            runtime->meters[i] = previous->meters[j];
            if (runtime->plan.nodes[i].kind == GraphKind::eq && previous->plan.nodes[j].kind == GraphKind::eq)
                runtime->spectra[i] = previous->spectra[j];
            break;
        }
        if (!runtime->meters[i]) runtime->meters[i] = std::make_shared<Meter>();
        const auto kind = runtime->plan.nodes[i].kind;
        if (kind == GraphKind::eq && !runtime->spectra[i])
            runtime->spectra[i] = std::make_shared<LiveSpectrum>(spectrumRevision_.fetch_add(1, std::memory_order_relaxed) + 1);
        // A fresh load (or one edit adding several inputs) has no retained port
        // for the second copy yet. Share the capture already prepared in this
        // runtime; the mixer reads each unique Port once and fans out its block.
        // Per-node meters, gain, mute, Mono and downstream DSP stay independent.
        // matches retains legacy capture-channel selection as part of identity.
        if (isSource(kind)) for (std::size_t j = 0; j < i; ++j)
            if (runtime->ports[j] && runtime->ports[j]->matches(node)) { runtime->ports[i] = runtime->ports[j]; break; }
        // A rapid A -> B -> A assignment can revisit a still-live older plan.
        // Reuse its matching endpoint worker instead of opening the hardware
        // twice. The same rule safely handles deleting/recreating a node.
        if (isSource(kind) || isSink(kind)) for (auto retained = runtimes_.rbegin(); retained != runtimes_.rend() && !runtime->ports[i]; ++retained)
            for (const auto& port : (*retained)->ports) if (port && port->matches(node)) { runtime->ports[i] = port; break; }
        if ((isSource(kind) || isSink(kind)) && !runtime->ports[i]) runtime->ports[i] = std::make_shared<Port>(node, kind, catalog_);
        if (runtime->ports[i]) runtime->ports[i]->activate();
    }
    return runtime;
}
void Engine::publish(std::unique_ptr<Runtime> runtime) {
    auto* pointer = runtime.get();
    runtimes_.push_back(std::move(runtime));
    published_.store(pointer, std::memory_order_release);
}
void Engine::retire() {
    const auto active = activeGeneration_.load(std::memory_order_acquire);
    std::erase_if(runtimes_, [&](const auto& runtime) { return runtime->generation < active; });
}
void Engine::configure(Configuration config, std::uint64_t expectedRevision) {
    std::lock_guard lock(configurationMutex_);
    if (!store_.writable()) throw std::runtime_error("Choose workspace recovery before changing the configuration.");
    if (expectedRevision != configuration_.revision) throw std::runtime_error("Configuration revision conflict. Refresh and retry your change.");
    if (configuration_.revision == std::numeric_limits<std::uint64_t>::max()) throw std::runtime_error("The configuration revision cannot be advanced.");
    retire();
    if (runtimes_.size() >= 128) throw std::runtime_error("The audio engine is not adopting changes. Wait briefly or restart the engine.");
    config.revision = configuration_.revision + 1;
    validateConfiguration(config); validateEndpoints(config);
    auto plan = compileGraph(config.graph);
    auto* current = published_.load(std::memory_order_acquire);
    bool audioUnchanged = current && current->plan == plan;
    if (audioUnchanged) for (std::size_t i = 0; i < plan.nodeCount; ++i)
        if (current->ports[i] && !current->ports[i]->matches(config.graph.nodes[i])) { audioUnchanged = false; break; }
    std::unique_ptr<Runtime> runtime;
    if (!audioUnchanged) {
        runtime = prepare(config, std::move(plan));
        runtimes_.reserve(runtimes_.size() + 1);
    }
    store_.save(config);
    configuration_ = std::move(config);
    // Canvas positions/names and reset anchors are persisted normally, but do
    // not rebuild denoiser FFT plans/reverb delays or copy their histories at
    // the next audio boundary. Sound and device changes still publish a plan.
    if (runtime) publish(std::move(runtime));
}
void Engine::retry() {
    catalog_.refresh();
    std::lock_guard lock(configurationMutex_);
    std::unordered_set<Port*> visited;
    for (const auto& runtime : runtimes_) for (const auto& port : runtime->ports)
        if (port && visited.insert(port.get()).second) port->retry();
}
void Engine::recover(const std::string& action) {
    std::lock_guard lock(configurationMutex_);
    // Reserve before committing recovery so publication cannot allocate after
    // opening ports or persisting the user's explicit choice.
    runtimes_.reserve(runtimes_.size() + 1);
    auto recovered = store_.recover(action);
    configuration_ = std::move(recovered);
    warning_ = store_.warning();
    if (!store_.writable() || !started_.load()) return;
    try { publish(prepare(configuration_, compileGraph(configuration_.graph))); }
    catch (...) {
        store_.pauseForRecovery("The workspace was recovered, but audio could not start. Audio and saving remain paused. Retry to reconnect safely.");
        warning_ = store_.warning();
        throw;
    }
}
Json Engine::snapshot() {
    std::lock_guard lock(configurationMutex_);
    EngineStatus status; status.audioFrames = audioFrames_.load(std::memory_order_relaxed);
    if (auto* runtime = published_.load(std::memory_order_acquire)) {
        status.nodes.reserve(runtime->plan.nodeCount);
        GraphChannels captureChannels{};
        for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
            const auto& node = runtime->plan.nodes[i];
            EngineStatus::Node value;
            value.id = node.id;
            value.peakLeft = runtime->meters[i]->peakLeft.exchange(0, std::memory_order_relaxed);
            value.peakRight = runtime->meters[i]->peakRight.exchange(0, std::memory_order_relaxed);
            value.peak = std::max(value.peakLeft, value.peakRight);
            value.clipped = runtime->meters[i]->clipped.exchange(false, std::memory_order_relaxed);
            value.dynamicsFrame = runtime->meters[i]->dynamicsFrame.load(std::memory_order_acquire);
            value.gainReductionDb = runtime->meters[i]->gainReductionDb.exchange(0, std::memory_order_relaxed);
            value.detectorDb = runtime->meters[i]->detectorDb.exchange(-96, std::memory_order_relaxed);
            bool dynamicsAudible = !node.bypass && !node.muted;
            if (node.controlEdge >= 0) {
                const auto& controlEdge = runtime->plan.edges[static_cast<std::size_t>(node.controlEdge)];
                if (!controlEdge.muted && runtime->plan.nodes[controlEdge.from].muted) dynamicsAudible = false;
            }
            if (!dynamicsAudible) { value.gainReductionDb = 0; value.detectorDb = -96; }
            if (runtime->spectra[i]) {
                const auto& spectrum = runtime->spectra[i]->read();
                value.spectrumFrame = spectrum.frame;
                value.spectrumRevision = spectrum.revision;
                if (spectrum.frame) value.spectrumDb.assign(spectrum.db.begin(), spectrum.db.end());
            }
            if (runtime->ports[i]) value.path = runtime->ports[i]->status();
            else { value.path.state = "Running"; value.path.message = node.kind == GraphKind::control ? "Shared volume control" : "Processing audio"; }
            captureChannels[i] = static_cast<int>(std::min(value.path.channels, 2u));
            if (node.id == "mic1") { status.sourcePeaks[0] = value.peak; status.inputs[0] = value.path; }
            if (node.id == "mic2") { status.sourcePeaks[1] = value.peak; status.inputs[1] = value.path; }
            if (node.id == "app-input") { status.sourcePeaks[2] = value.peak; status.inputs[2] = value.path; }
            if (node.id == "main-output-1") status.outputs[0] = value.path;
            if (node.id == "main-output-2") status.outputs[1] = value.path;
            if (node.id == "virtual-output") status.outputs[2] = value.path;
            if (node.id == "mix-a") { status.peakA = value.peak; status.clipA = value.clipped; }
            if (node.id == "mix-b") { status.peakB = value.peak; status.clipB = value.clipped; }
            status.nodes.push_back(std::move(value));
        }
        const auto channels = graphSignalChannels(runtime->plan, captureChannels);
        for (std::size_t i = 0; i < status.nodes.size(); ++i) status.nodes[i].channels = channels[i];
    }
    std::lock_guard endpointLock(endpointsMutex_);
    std::wstring executable(32768, L'\0');
    executable.resize(GetModuleFileNameW(nullptr, executable.data(), static_cast<DWORD>(executable.size())));
    return Json{{"configuration", configuration_}, {"endpoints", endpoints_}, {"applications", applications_},
        {"enginePid", GetCurrentProcessId()}, {"executablePath", narrow(executable)}, {"engineProtocolVersion", 1},
        {"engineVersion", releaseVersion}, {"displayVersion", releaseDisplayVersion},
        {"physicalSettingsPath", narrow(store_.physicalPath().wstring())},
        {"appCaptureAvailable", processCaptureAvailable()}, {"status", status}, {"settingsPath", narrow(store_.path().wstring())},
        {"logPath", narrow(logPath_.wstring())}, {"warning", warning_}, {"settingsState", store_.loadState()},
        {"backupPath", narrow(store_.path().wstring()) + ".bak"}};
}

void Engine::mixLoop() {
    DWORD taskIndex{}; HANDLE task = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
    HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, 0x00000002, TIMER_ALL_ACCESS);
    if (!timer) timer = CreateWaitableTimerW(nullptr, FALSE, nullptr);
    if (!timer) { if (task) AvRevertMmThreadCharacteristics(task); requestStop(); return; }
    LARGE_INTEGER frequency{}, now{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&now);
    const auto ticksPerBlock = frequency.QuadPart * static_cast<LONGLONG>(quantumFrames) / sampleRate;
    MixClock clock(ticksPerBlock, now.QuadPart);
    auto previous = now.QuadPart;
    HANDLE waits[] = {stopEvent_, timer};
    Runtime* active{}; Runtime* fading{}; std::uint32_t fadeRemaining{};
    std::array<std::array<Frame, quantumFrames>, maximumGraphNodes * 2> input{};
    std::array<Port*, maximumGraphNodes * 2> inputPorts{}, outputPorts{};
    std::array<Frame, quantumFrames> mixedOutput{};
    while (!stopping()) {
        QueryPerformanceCounter(&now);
        const bool resumed = now.QuadPart - previous > frequency.QuadPart / 4;
        previous = now.QuadPart;
        auto* desired = published_.load(std::memory_order_acquire);
        if (!fading && desired && desired != active) {
            if (active) {
                desired->processor.inherit(active->processor, active->plan, desired->plan);
                bool changed = !sameGraphTopology(active->plan, desired->plan);
                // Device/channel/application changes keep the same graph IDs,
                // but the previous source must still fade out locally.
                if (!changed) for (std::size_t i = 0; i < desired->plan.nodeCount; ++i)
                    for (std::size_t j = 0; j < active->plan.nodeCount; ++j)
                        if (desired->plan.nodes[i].id == active->plan.nodes[j].id
                            && desired->ports[i].get() != active->ports[j].get()) changed = true;
                if (changed) { fading = active; fadeRemaining = 480; }
            }
            active = desired;
        }
        if (resumed) {
            // Only the active graph publishes spectrum windows. Shared
            // analyzers must be cleared once even during a routing crossfade.
            if (active) for (const auto& spectrum : active->spectra) if (spectrum)
                spectrum->reset(spectrumRevision_.fetch_add(1, std::memory_order_relaxed) + 1);
            for (auto* runtime : {active, fading}) if (runtime) {
                runtime->processor.reset();
                for (const auto& meter : runtime->meters) if (meter) {
                    meter->gainReductionDb.store(0, std::memory_order_relaxed);
                    meter->detectorDb.store(-96, std::memory_order_relaxed);
                    meter->dynamicsFrame.store(0, std::memory_order_release);
                }
                for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) if (auto* port = runtime->ports[i].get()) {
                    if (isSource(runtime->plan.nodes[i].kind)) { port->queue->clear(); port->reader.reset(); }
                    else port->queue->requestClear();
                }
            }
            clock.reset(now.QuadPart); resumeRequested_.store(true, std::memory_order_release);
        }
        std::size_t captured{}, rendered{};
        for (auto* runtime : {active, fading}) if (runtime) {
            GraphProcessor::Inputs views{};
            for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
                auto* port = runtime->ports[i].get();
                if (!port || !isSource(runtime->plan.nodes[i].kind)) continue;
                std::size_t slot{}; while (slot < captured && inputPorts[slot] != port) ++slot;
                if (slot == captured) {
                    inputPorts[captured++] = port; input[slot].fill({});
                    if (port->device) {
                        // Acquire readiness before reading its published period;
                        // function argument evaluation order cannot enforce it.
                        const bool ready = port->ready();
                        const auto period = port->device->periodFrames();
                        const auto reserve = port->device->targetQueueFrames();
                        port->reader.read(*port->queue, input[slot], ready, period, reserve);
                    }
                    else if (port->ready()) {
                        // Process loopback retains its separate delivery policy.
                        if (port->queue->size() > sampleRate / 10) port->queue->clear();
                        port->queue->pop(input[slot]);
                    } else port->queue->clear();
                }
                views[i] = input[slot];
            }
            runtime->processor.process(runtime->plan, views);
            if (runtime == active) for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
                const auto& meter = runtime->processor.meter(i);
                raisePeak(runtime->meters[i]->peakLeft, meter.peakLeft);
                raisePeak(runtime->meters[i]->peakRight, meter.peakRight);
                if (meter.clipped) runtime->meters[i]->clipped.store(true, std::memory_order_relaxed);
                const auto& node = runtime->plan.nodes[i];
                if (node.kind == GraphKind::compressor || node.kind == GraphKind::limiter || node.kind == GraphKind::ducking
                    || node.kind == GraphKind::expander || node.kind == GraphKind::gate || node.kind == GraphKind::deEsser || node.kind == GraphKind::plosive) {
                    // Hold brief consonant peaks between UI polls, then consume
                    // them once. Never accumulate a historical maximum.
                    raisePeak(runtime->meters[i]->gainReductionDb, meter.gainReductionDb);
                    raisePeak(runtime->meters[i]->detectorDb, meter.detectorDb);
                    runtime->meters[i]->dynamicsFrame.store(audioFrames_.load(std::memory_order_relaxed) + quantumFrames, std::memory_order_release);
                }
                if (runtime->spectra[i]) runtime->spectra[i]->push(runtime->processor.output(i),
                    audioFrames_.load(std::memory_order_relaxed) + quantumFrames);
            }
        }
        // Capture is read once per unique worker, then independently fanned out.
        // Topology edits crossfade old/new live graphs for 10 ms; a sink receives
        // one queue write even when both graphs refer to the same worker.
        for (auto* runtime : {active, fading}) if (runtime) for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
            auto* port = runtime->ports[i].get();
            if (!port || !isSink(runtime->plan.nodes[i].kind)) continue;
            if (std::find(outputPorts.begin(), outputPorts.begin() + rendered, port) != outputPorts.begin() + rendered) continue;
            outputPorts[rendered++] = port;
            if (!port->ready()) { if (!port->queue->clearPending()) port->queue->requestClear(); continue; }
            std::span<const Frame> current, old;
            if (active) for (std::size_t n = 0; n < active->plan.nodeCount; ++n)
                if (active->ports[n].get() == port) { current = active->processor.output(n); break; }
            if (fading) for (std::size_t n = 0; n < fading->plan.nodeCount; ++n)
                if (fading->ports[n].get() == port) { old = fading->processor.output(n); break; }
            for (std::size_t frame = 0; frame < quantumFrames; ++frame) {
                const float weight = fading ? 1.f - static_cast<float>(fadeRemaining - static_cast<std::uint32_t>(frame) - 1) / 480.f : 1.f;
                const auto a = current.empty() ? Frame{} : current[frame];
                const auto b = old.empty() ? Frame{} : old[frame];
                mixedOutput[frame] = {a.left * weight + b.left * (1 - weight), a.right * weight + b.right * (1 - weight)};
            }
            if (port->queue->size() > sampleRate / 10) port->queue->requestClear();
            port->queue->push(mixedOutput);
        }
        if (fading) { fadeRemaining -= static_cast<std::uint32_t>(quantumFrames); if (fadeRemaining == 0) fading = nullptr; }
        if (active && !fading) activeGeneration_.store(active->generation, std::memory_order_release);
        audioFrames_.fetch_add(quantumFrames, std::memory_order_relaxed);
        QueryPerformanceCounter(&now);
        const auto next = clock.advance(now.QuadPart);
        if (next <= now.QuadPart) continue;
        LARGE_INTEGER due{}; due.QuadPart = -std::max<LONGLONG>(1, (next - now.QuadPart) * 10000000 / frequency.QuadPart);
        if (!SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE)) { requestStop(); break; }
        if (WaitForMultipleObjects(2, waits, FALSE, INFINITE) != WAIT_OBJECT_0 + 1) break;
    }
    CancelWaitableTimer(timer); CloseHandle(timer); if (task) AvRevertMmThreadCharacteristics(task);
}
void Engine::maintenanceLoop() {
    std::unordered_map<std::string, std::string> lastStates;
    while (!stopping()) {
        if (resumeRequested_.exchange(false)) retry();
        try {
            auto devices = catalog_.enumerate(); auto applications = enumerateAudioApplications();
            { std::lock_guard lock(endpointsMutex_); endpoints_ = std::move(devices); applications_ = std::move(applications); }
            std::lock_guard lock(configurationMutex_);
            retire();
            if (auto* runtime = published_.load(std::memory_order_acquire)) {
                std::erase_if(lastStates, [&](const auto& entry) {
                    for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) if (runtime->plan.nodes[i].id == entry.first) return false;
                    return true;
                });
                for (std::size_t i = 0; i < runtime->plan.nodeCount; ++i) {
                    if (!runtime->ports[i]) continue;
                    const auto state = runtime->ports[i]->status();
                    const auto summary = state.state + ": " + state.message;
                    const auto& id = runtime->plan.nodes[i].id;
                    if (lastStates[id] != summary) { writeLog("Node " + id + " " + summary); lastStates[id] = summary; }
                }
            }
        } catch (const std::exception& error) { writeLog(std::string("Device refresh: ") + error.what()); }
        if (WaitForSingleObject(stopEvent_, 1000) == WAIT_OBJECT_0) break;
    }
}
void Engine::writeLog(const std::string& message) {
    std::lock_guard lock(logMutex_);
    try {
        if (const auto parent = logPath_.parent_path(); !parent.empty())
            std::filesystem::create_directories(parent);
        if (std::filesystem::exists(logPath_) && std::filesystem::file_size(logPath_) > 2 * 1024 * 1024) {
            auto previous = logPath_; previous += L".previous";
            std::error_code ignored; std::filesystem::remove(previous, ignored); std::filesystem::rename(logPath_, previous, ignored);
        }
        SYSTEMTIME time{}; GetLocalTime(&time); std::ofstream log(logPath_, std::ios::binary | std::ios::app);
        log << std::setfill('0') << std::setw(4) << time.wYear << '-' << std::setw(2) << time.wMonth << '-'
            << std::setw(2) << time.wDay << ' ' << std::setw(2) << time.wHour << ':' << std::setw(2) << time.wMinute
            << ':' << std::setw(2) << time.wSecond << ' ' << message << '\n';
    } catch (...) { /* Logging must never stop routing. */ }
}
}
