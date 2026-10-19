// Explicit observation of an existing renderer and its virtual microphone.
// This executable never renders, saves audio, changes settings, or opens a
// physical capture endpoint. It is deliberately not an automated test.
#include "am/windows_audio.hpp"
#include "am/dsp.hpp"
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <nlohmann/json.hpp>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <iostream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

namespace {
using Json = nlohmann::json;
using Clock = std::chrono::steady_clock;
template<class T> class ComPtr {
public:
    ~ComPtr() { if (value_) value_->Release(); }
    ComPtr() = default;
    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;
    T* get() const { return value_; }
    T* operator->() const { return value_; }
    T** put() { if (value_) value_->Release(); value_ = nullptr; return &value_; }
    void** out() { return reinterpret_cast<void**>(put()); }
private:
    T* value_{};
};
struct Handle {
    HANDLE value{};
    explicit Handle(HANDLE handle) : value(handle) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct Apartment {
    HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    ~Apartment() { if (SUCCEEDED(result)) CoUninitialize(); }
};
void check(HRESULT result, const char* operation) {
    if (FAILED(result)) {
        std::ostringstream message;
        message << operation << " failed (0x" << std::hex << static_cast<unsigned long>(result) << ')';
        throw std::runtime_error(message.str());
    }
}
std::string utf8(const std::wstring& value) {
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size == 0 && !value.empty()) throw std::runtime_error("Invalid Unicode argument");
    std::string result(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}
struct Options {
    bool run{}, system{};
    DWORD pid{};
    std::string captureId;
    double seconds = 5;
};
Options parse(int argc, wchar_t** argv) {
    Options result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring argument = argv[index];
        if (argument == L"--run") result.run = true;
        else if (argument == L"--system") result.system = true;
        else if (argument == L"--capture-id" && index + 1 < argc) result.captureId = utf8(argv[++index]);
        else if (argument == L"--observe" && index + 1 < argc) {
            const std::wstring value = argv[++index]; std::size_t used{};
            const auto pid = std::stoull(value, &used);
            if (used != value.size() || pid == 0 || pid > MAXDWORD || value.front() == L'-')
                throw std::runtime_error("Invalid explicit process ID");
            result.pid = static_cast<DWORD>(pid);
        } else if (argument == L"--seconds" && index + 1 < argc) {
            const std::wstring value = argv[++index]; std::size_t used{};
            result.seconds = std::stod(value, &used);
            if (used != value.size()) throw std::runtime_error("Invalid observation duration");
        } else throw std::runtime_error("Unknown or incomplete option");
    }
    if (!result.run || !result.pid || result.captureId.empty())
        throw std::runtime_error("Required: --run --observe <PID> --capture-id <exact virtual capture ID> [--seconds <1..20>] [--system]");
    if (!std::isfinite(result.seconds) || result.seconds < 1 || result.seconds > 20)
        throw std::runtime_error("Observation duration must be between 1 and 20 seconds");
    return result;
}

struct Statistics {
    std::uint64_t frames{}, signalFrames{}, nonfinite{}, discontinuities{}, silentFrames{}, packets{}, emptyPolls{};
    double peak{}, energy{};
    void add(float left, float right) {
        ++frames;
        if (!std::isfinite(left) || !std::isfinite(right)) { ++nonfinite; return; }
        const double magnitude = std::max(std::abs(static_cast<double>(left)), std::abs(static_cast<double>(right)));
        peak = std::max(peak, magnitude);
        energy += (static_cast<double>(left) * left + static_cast<double>(right) * right) * .5;
        if (magnitude > 1e-6) ++signalFrames;
    }
    double rms() const { return frames > nonfinite ? std::sqrt(energy / static_cast<double>(frames - nonfinite)) : 0; }
    Json json() const {
        return {{"frames", frames}, {"framesAboveOneMillionthAmplitude", signalFrames}, {"nonfiniteFrames", nonfinite},
            {"silentFlagFrames", silentFrames}, {"discontinuities", discontinuities},
            {"packets", packets}, {"emptyPolls", emptyPolls}, {"peak", peak}, {"rms", rms()}};
    }
};

// SDK ABI is declared here because the bundled MinGW omits the activation
// parameter header. This is the same ABI as production process capture.
struct ActivationParams { INT type = 1; DWORD pid{}; INT mode{}; };
static_assert(sizeof(ActivationParams) == 12);
struct ActivationState {
    Handle completed{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    ActivationParams params;
    PROPVARIANT variant{};
    HRESULT result = E_PENDING;
    ComPtr<IAudioClient> client;
    ActivationState(DWORD pid, bool exclude) {
        params.pid = pid; params.mode = exclude ? 1 : 0;
        variant.vt = VT_BLOB; variant.blob.cbSize = sizeof(params);
        variant.blob.pBlobData = reinterpret_cast<BYTE*>(&params);
    }
};
class ActivationHandler final : public IActivateAudioInterfaceCompletionHandler, public IAgileObject {
public:
    explicit ActivationHandler(std::shared_ptr<ActivationState> state) : state_(std::move(state)) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (IsEqualIID(iid, __uuidof(IUnknown)) || IsEqualIID(iid, __uuidof(IActivateAudioInterfaceCompletionHandler)))
            *result = static_cast<IActivateAudioInterfaceCompletionHandler*>(this);
        else if (IsEqualIID(iid, __uuidof(IAgileObject))) *result = static_cast<IAgileObject*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return references_.fetch_add(1) + 1; }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto count = references_.fetch_sub(1) - 1;
        if (!count) delete this;
        return count;
    }
    HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override {
        HRESULT activated = E_FAIL; ComPtr<IUnknown> value;
        auto result = operation->GetActivateResult(&activated, value.put());
        if (SUCCEEDED(result)) result = activated;
        if (SUCCEEDED(result)) result = value.get() ? value->QueryInterface(__uuidof(IAudioClient), state_->client.out()) : E_POINTER;
        state_->result = result; SetEvent(state_->completed.value); return S_OK;
    }
private:
    std::atomic<ULONG> references_{1};
    std::shared_ptr<ActivationState> state_;
};
class ProcessLoopback {
public:
    ProcessLoopback(DWORD pid, bool exclude) : state_(std::make_shared<ActivationState>(pid, exclude)) {
        using Activate = HRESULT(WINAPI*)(LPCWSTR, REFIID, PROPVARIANT*, IActivateAudioInterfaceCompletionHandler*, IActivateAudioInterfaceAsyncOperation**);
        // Keep the module loaded if a callback outlives the bounded wait.
        static const HMODULE module = LoadLibraryExW(L"Mmdevapi.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        const auto activate = module ? reinterpret_cast<Activate>(GetProcAddress(module, "ActivateAudioInterfaceAsync")) : nullptr;
        if (!activate || !state_->completed.value) throw std::runtime_error("Process capture API unavailable");
        ComPtr<IActivateAudioInterfaceCompletionHandler> handler;
        *handler.put() = new ActivationHandler(state_);
        ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
        check(activate(L"VAD\\Process_Loopback", __uuidof(IAudioClient), &state_->variant, handler.get(), operation.put()), "Activate process capture");
        if (WaitForSingleObject(state_->completed.value, 5000) != WAIT_OBJECT_0)
            throw std::runtime_error("Process capture activation timed out");
        check(state_->result, "Complete process capture activation");
        WAVEFORMATEX format{};
        format.wFormatTag = WAVE_FORMAT_IEEE_FLOAT; format.nChannels = 2;
        format.nSamplesPerSec = am::sampleRate; format.wBitsPerSample = 32;
        format.nBlockAlign = sizeof(am::Frame); format.nAvgBytesPerSec = am::sampleRate * sizeof(am::Frame);
        check(state_->client->Initialize(AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
                AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            0, 0, &format, nullptr), "Initialize process capture");
        if (!audioEvent_.value) throw std::runtime_error("Could not allocate process capture event");
        check(state_->client->SetEventHandle(audioEvent_.value), "Set process capture event");
        check(state_->client->GetService(__uuidof(IAudioCaptureClient), capture_.out()), "Get process capture service");
        check(state_->client->Start(), "Start process capture");
        started_ = true;
    }
    ~ProcessLoopback() { if (started_) state_->client->Stop(); }
    void poll(Statistics& statistics) {
        // Bound each poll even if another process continuously renders.
        for (unsigned packetIndex = 0; packetIndex < 128; ++packetIndex) {
            UINT32 packet{}; check(capture_->GetNextPacketSize(&packet), "Read process packet size");
            if (!packet) { if (!packetIndex) ++statistics.emptyPolls; break; }
            BYTE* bytes{}; UINT32 count{}; DWORD flags{};
            check(capture_->GetBuffer(&bytes, &count, &flags, nullptr, nullptr), "Read process packet");
            ++statistics.packets;
            if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) ++statistics.discontinuities;
            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) statistics.silentFrames += count;
            for (UINT32 frame = 0; frame < count; ++frame) {
                am::Frame sample{};
                if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && bytes)
                    std::memcpy(&sample, bytes + frame * sizeof(am::Frame), sizeof(sample));
                statistics.add(sample.left, sample.right);
            }
            check(capture_->ReleaseBuffer(count), "Release process packet");
        }
    }
private:
    std::shared_ptr<ActivationState> state_;
    ComPtr<IAudioCaptureClient> capture_;
    Handle audioEvent_{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    bool started_{};
};

Json pathJson(const am::PathState& path) {
    return {{"state", path.state}, {"message", path.message}, {"endpointId", path.endpointId},
        {"sampleRate", path.rate}, {"channels", path.channels}, {"reconnects", path.reconnects}};
}
void drain(am::FrameQueue& queue, Statistics& statistics) {
    std::array<am::Frame, 2048> buffer{};
    for (unsigned iteration = 0; iteration < 32; ++iteration) {
        const auto available = std::min(queue.size(), buffer.size());
        if (!available) break;
        const auto count = queue.pop(std::span(buffer).first(available));
        for (std::size_t index = 0; index < count; ++index) statistics.add(buffer[index].left, buffer[index].right);
        std::fill(buffer.begin(), buffer.begin() + count, am::Frame{});
    }
}
int observe(const Options& options) {
    Apartment apartment; check(apartment.result, "Initialize COM");
    // Holding a process handle prevents us from silently switching to a reused
    // process ID. Every observation iteration also verifies it is still alive.
    Handle target{OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, options.pid)};
    if (!target.value) throw std::runtime_error("Explicit target process cannot be opened");
    std::wstring path(32768, L'\0'); DWORD size = static_cast<DWORD>(path.size());
    if (!QueryFullProcessImageNameW(target.value, 0, path.data(), &size)) throw std::runtime_error("Cannot identify explicit target process");
    path.resize(size);
    am::DeviceCatalog catalog;
    am::Endpoint endpoint;
    for (const auto& candidate : catalog.enumerate()) if (candidate.id == options.captureId) endpoint = candidate;
    if (endpoint.id.empty() || endpoint.direction != "capture" || endpoint.state != "Active" || !endpoint.isVirtual ||
        (endpoint.transport != "vacLite" && endpoint.transport != "vbCable"))
        throw std::runtime_error("Capture ID must identify an active VAC or VB-CABLE capture endpoint; physical and unknown transports are refused");
    Json report{{"tool", "AudioMixer.CaptureIsolationProbe"}, {"pid", options.pid}, {"executablePath", utf8(path)},
        {"captureEndpoint", {{"id", endpoint.id}, {"name", endpoint.name}, {"transport", endpoint.transport}}},
        {"seconds", options.seconds}, {"audioFilesWritten", false}, {"outputStreamsOpened", false},
        {"settingsChanged", false}, {"isolationProven", false}};
    auto queue = std::make_shared<am::FrameQueue>(32768);
    am::AudioWorker cable(true, false, queue, catalog);
    // Exact endpoint only: omit stable ID so a replacement device is not used.
    cable.configure({endpoint.id, {}, endpoint.name, 0});
    ProcessLoopback process(options.pid, false);
    std::unique_ptr<ProcessLoopback> system;
    if (options.system) {
        try { system = std::make_unique<ProcessLoopback>(4, true); }
        catch (const std::exception& error) { report["systemCaptureError"] = error.what(); }
    }
    // Drain startup buffers while the selected cable becomes ready, then
    // settle for one second. Measurements never include startup transients.
    const auto readinessDeadline = Clock::now() + std::chrono::seconds(5);
    Statistics discarded;
    while (!cable.ready() && Clock::now() < readinessDeadline) {
        drain(*queue, discarded); process.poll(discarded);
        if (system) system->poll(discarded);
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    if (!cable.ready()) throw std::runtime_error("Virtual capture did not become ready: " + cable.status().message);
    const auto start = Clock::now() + std::chrono::seconds(1);
    const auto finish = start + std::chrono::duration<double>(options.seconds);
    Statistics cableStats, processStats, systemStats;
    std::uint64_t polls{}, cableReadyPolls{};
    while (Clock::now() < finish) {
        if (WaitForSingleObject(target.value, 0) != WAIT_TIMEOUT) throw std::runtime_error("Target process exited during observation");
        const bool measuring = Clock::now() >= start;
        drain(*queue, measuring ? cableStats : discarded);
        process.poll(measuring ? processStats : discarded);
        if (system) system->poll(measuring ? systemStats : discarded);
        if (measuring) { ++polls; if (cable.ready()) ++cableReadyPolls; }
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    report["cableState"] = pathJson(cable.status());
    report["cable"] = cableStats.json(); report["processLoopback"] = processStats.json();
    report["processCaptureStarted"] = true; report["systemCaptureStarted"] = !!system;
    if (system) report["systemExcludingPid4"] = systemStats.json();
    report["polls"] = polls; report["cableReadyPolls"] = cableReadyPolls;
    report["cableQueueDroppedFrames"] = queue->droppedFrames();
    cable.stop();
    int result = 0;
    if (cableStats.nonfinite || processStats.nonfinite || systemStats.nonfinite) {
        report["outcome"] = "invalid_samples"; result = 4;
    } else if (!polls || cableReadyPolls != polls || !cableStats.frames || queue->droppedFrames()) {
        report["outcome"] = "incomplete_observation"; result = 5;
    } else if (cableStats.rms() < 0.00003 || cableStats.signalFrames < am::sampleRate / 10) {
        report["outcome"] = "insufficient_signal"; result = 6;
    } else if (!processStats.frames) {
        report["outcome"] = "cable_signal_with_no_process_packets"; result = 7;
    } else if (processStats.peak <= 1e-7) {
        report["outcome"] = "cable_signal_with_process_silence";
    } else {
        report["outcome"] = "cable_and_process_signal";
    }
    report["detail"] = "Observation only. Process silence with cable signal is evidence to compare against a shared-mode baseline, not proof of Discord isolation. Other outputs of the target process can contribute to process capture; system capture can contain unrelated audio. No audio samples are retained.";
    std::cout << report.dump() << '\n';
    return result;
}
} // namespace
int wmain(int argc, wchar_t** argv) {
    try { return observe(parse(argc, argv)); }
    catch (const std::exception& error) {
        std::cout << Json{{"tool", "AudioMixer.CaptureIsolationProbe"}, {"outcome", "error"}, {"detail", error.what()}}.dump() << '\n';
        return 1;
    }
}
