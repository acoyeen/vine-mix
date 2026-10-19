#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include "am/process_capture.hpp"
#include "am/dsp.hpp"
#include <windows.h>
#include <tlhelp32.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audiopolicy.h>
#include <avrt.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstring>
#include <map>
#include <mutex>
#include <optional>
#include <set>
#include <span>
#include <sstream>
#include <stdexcept>
#include <string_view>
#include <thread>
#include <utility>

namespace am {
namespace {
template<class T> class ComPtr {
public:
    ~ComPtr() { if (value_) value_->Release(); }
    ComPtr() = default;
    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;
    T* get() const noexcept { return value_; }
    T* operator->() const noexcept { return value_; }
    T** put() noexcept { if (value_) value_->Release(); value_ = nullptr; return &value_; }
    void** out() noexcept { return reinterpret_cast<void**>(put()); }
    T* release() noexcept { return std::exchange(value_, nullptr); }
private:
    T* value_{};
};
class Handle {
public:
    explicit Handle(HANDLE value = nullptr) : value_(value) {}
    ~Handle() { if (valid()) CloseHandle(value_); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    HANDLE get() const noexcept { return value_; }
    bool valid() const noexcept { return value_ && value_ != INVALID_HANDLE_VALUE; }
private:
    HANDLE value_{};
};
struct Apartment {
    HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    ~Apartment() { if (SUCCEEDED(result)) CoUninitialize(); }
};

std::wstring wide(std::string_view input) {
    if (input.empty()) return {};
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, input.data(), static_cast<int>(input.size()), nullptr, 0);
    if (!length) return {};
    std::wstring result(static_cast<std::size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, input.data(), static_cast<int>(input.size()), result.data(), length);
    return result;
}
std::string utf8(std::wstring_view input) {
    if (input.empty()) return {};
    const int length = WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<std::size_t>(length), '\0');
    if (length) WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.size()), result.data(), length, nullptr, nullptr);
    return result;
}
bool samePath(std::wstring_view a, std::wstring_view b) {
    return !a.empty() && !b.empty() && CompareStringOrdinal(a.data(), static_cast<int>(a.size()), b.data(), static_cast<int>(b.size()), TRUE) == CSTR_EQUAL;
}
std::wstring imagePath(HANDLE process) {
    std::wstring path(32768, L'\0');
    DWORD size = static_cast<DWORD>(path.size());
    if (!QueryFullProcessImageNameW(process, 0, path.data(), &size)) return {};
    path.resize(size);
    return path;
}
std::wstring imagePath(DWORD pid) {
    Handle process{OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid)};
    return process.valid() ? imagePath(process.get()) : std::wstring{};
}
bool parentPredatesChild(DWORD parentId, DWORD childId) {
    Handle parent{OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, parentId)};
    Handle child{OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, childId)};
    FILETIME parentCreated{}, childCreated{}, ignored1{}, ignored2{}, ignored3{};
    return parent.valid() && child.valid() &&
        GetProcessTimes(parent.get(), &parentCreated, &ignored1, &ignored2, &ignored3) &&
        GetProcessTimes(child.get(), &childCreated, &ignored1, &ignored2, &ignored3) &&
        CompareFileTime(&parentCreated, &childCreated) <= 0;
}
std::wstring basename(std::wstring_view path) {
    return std::wstring(path.substr(path.find_last_of(L"\\/") + 1));
}
bool isMixer(const std::wstring& path) {
    const auto name = basename(path);
    return samePath(name, L"AudioMixer.Engine.exe") || samePath(name, L"AudioMixer.exe") || samePath(name, L"AudioMixer.Desktop.exe");
}

struct ProcessEntry { DWORD parent{}; std::wstring filename; };
using ProcessTable = std::map<DWORD, ProcessEntry>;
ProcessTable processTable() {
    ProcessTable result;
    Handle snapshot{CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)};
    if (!snapshot.valid()) return result;
    DWORD thisSession{};
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &thisSession)) return result;
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    if (!Process32FirstW(snapshot.get(), &entry)) return result;
    do {
        DWORD session{};
        if (entry.th32ProcessID && ProcessIdToSessionId(entry.th32ProcessID, &session) && session == thisSession)
            result.emplace(entry.th32ProcessID, ProcessEntry{entry.th32ParentProcessID, entry.szExeFile});
    } while (Process32NextW(snapshot.get(), &entry));
    return result;
}
bool containsMixerProcess(DWORD root, const ProcessTable& table) {
    DWORD current = GetCurrentProcessId();
    std::set<DWORD> visited;
    while (current && visited.insert(current).second) {
        if (current == root) return true;
        const auto entry = table.find(current);
        if (entry == table.end()) break;
        current = entry->second.parent;
    }
    return false;
}
DWORD sameApplicationRoot(DWORD pid, const std::wstring& path, const ProcessTable& table) {
    std::set<DWORD> visited;
    while (visited.insert(pid).second) {
        const auto child = table.find(pid);
        if (child == table.end() || child->second.parent == pid) break;
        const auto parent = table.find(child->second.parent);
        if (parent == table.end() || !samePath(parent->second.filename, basename(path))) break;
        // Toolhelp retains a numerical parent PID even after that parent exits.
        // Never treat a newer process reusing the PID as the original parent.
        if (!parentPredatesChild(parent->first, pid)) break;
        if (!samePath(imagePath(parent->first), path)) break;
        pid = parent->first;
    }
    return pid;
}

struct Candidate { std::wstring title; bool audio{}; };
BOOL CALLBACK collectWindow(HWND window, LPARAM context) {
    if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER)) return TRUE;
    const int length = GetWindowTextLengthW(window);
    if (length <= 0) return TRUE;
    DWORD pid{}; GetWindowThreadProcessId(window, &pid);
    std::wstring title(static_cast<std::size_t>(length) + 1, L'\0');
    const int copied = GetWindowTextW(window, title.data(), length + 1);
    title.resize(static_cast<std::size_t>(std::max(0, copied)));
    if (!title.empty()) (*reinterpret_cast<std::map<DWORD, Candidate>*>(context))[pid].title = std::move(title);
    return TRUE;
}
void collectAudioSessions(std::map<DWORD, Candidate>& candidates) {
    ComPtr<IMMDeviceEnumerator> enumerator;
    if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), enumerator.out()))) return;
    ComPtr<IMMDeviceCollection> endpoints;
    if (FAILED(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, endpoints.put()))) return;
    UINT count{}; if (FAILED(endpoints->GetCount(&count))) return;
    for (UINT i = 0; i < count; ++i) {
        ComPtr<IMMDevice> endpoint;
        ComPtr<IAudioSessionManager2> manager;
        ComPtr<IAudioSessionEnumerator> sessions;
        if (FAILED(endpoints->Item(i, endpoint.put())) ||
            FAILED(endpoint->Activate(__uuidof(IAudioSessionManager2), CLSCTX_ALL, nullptr, manager.out())) ||
            FAILED(manager->GetSessionEnumerator(sessions.put()))) continue;
        int sessionCount{}; if (FAILED(sessions->GetCount(&sessionCount))) continue;
        for (int j = 0; j < sessionCount; ++j) {
            ComPtr<IAudioSessionControl> session;
            ComPtr<IAudioSessionControl2> session2;
            DWORD pid{};
            if (SUCCEEDED(sessions->GetSession(j, session.put())) &&
                SUCCEEDED(session->QueryInterface(__uuidof(IAudioSessionControl2), session2.out())) &&
                session2->IsSystemSoundsSession() != S_OK && SUCCEEDED(session2->GetProcessId(&pid)) && pid)
                candidates[pid].audio = true;
        }
    }
}

using ActivateFunction = HRESULT(WINAPI*)(LPCWSTR, REFIID, PROPVARIANT*, IActivateAudioInterfaceCompletionHandler*, IActivateAudioInterfaceAsyncOperation**);
ActivateFunction activationFunction() noexcept {
    // The module stays loaded for asynchronous callbacks, including callbacks
    // that arrive after a cancelled worker has released its local operation.
    static const HMODULE module = LoadLibraryExW(L"Mmdevapi.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    static const auto function = module ? reinterpret_cast<ActivateFunction>(GetProcAddress(module, "ActivateAudioInterfaceAsync")) : nullptr;
    return function;
}
bool supportedWindows() noexcept {
    using VersionFunction = LONG(WINAPI*)(OSVERSIONINFOW*);
    const auto module = GetModuleHandleW(L"ntdll.dll");
    const auto function = module ? reinterpret_cast<VersionFunction>(GetProcAddress(module, "RtlGetVersion")) : nullptr;
    OSVERSIONINFOW version{}; version.dwOSVersionInfoSize = sizeof(version);
    return function && function(&version) == 0 && (version.dwMajorVersion > 10 ||
        (version.dwMajorVersion == 10 && version.dwBuildNumber >= 20348));
}

// Public Windows SDK ABI, declared locally because the bundled MinGW headers
// lack audioclientactivationparams.h. Values/layout match Microsoft's SDK.
struct ProcessActivationParams {
    INT activationType = 1; // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
    DWORD targetProcessId{};
    INT processLoopbackMode = 0; // INCLUDE_TARGET_PROCESS_TREE
};
static_assert(sizeof(ProcessActivationParams) == 12);
struct ActivationState {
    Handle completed{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    ProcessActivationParams params;
    PROPVARIANT variant{};
    HRESULT result = E_PENDING;
    ComPtr<IAudioClient> client;
    explicit ActivationState(DWORD pid) {
        params.targetProcessId = pid;
        variant.vt = VT_BLOB;
        variant.blob.cbSize = sizeof(params);
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
        HRESULT activated = E_FAIL;
        ComPtr<IUnknown> value;
        auto result = operation->GetActivateResult(&activated, value.put());
        if (SUCCEEDED(result)) result = activated;
        if (SUCCEEDED(result)) result = value.get() ? value->QueryInterface(__uuidof(IAudioClient), state_->client.out()) : E_POINTER;
        state_->result = result;
        SetEvent(state_->completed.get());
        return S_OK;
    }
private:
    std::atomic<ULONG> references_{1};
    std::shared_ptr<ActivationState> state_;
};

std::string failureMessage(HRESULT result) {
    if (result == E_ACCESSDENIED) return "Windows denied app audio capture";
    if (result == AUDCLNT_E_SERVICE_NOT_RUNNING) return "Windows Audio service unavailable";
    if (result == AUDCLNT_E_UNSUPPORTED_FORMAT) return "App capture format unavailable";
    if (result == HRESULT_FROM_WIN32(ERROR_TIMEOUT)) return "Windows app capture did not respond; use Retry audio";
    std::ostringstream message; message << "App capture unavailable (0x" << std::hex << static_cast<unsigned long>(result) << ")";
    return message.str();
}
}

bool processCaptureAvailable() noexcept { return supportedWindows() && activationFunction(); }
std::vector<ApplicationInfo> enumerateAudioApplications() {
    Apartment apartment;
    const auto table = processTable();
    std::map<DWORD, Candidate> candidates;
    EnumWindows(collectWindow, reinterpret_cast<LPARAM>(&candidates));
    if (SUCCEEDED(apartment.result)) collectAudioSessions(candidates);
    std::map<DWORD, ApplicationInfo> roots;
    for (const auto& [pid, candidate] : candidates) {
        if (!table.contains(pid)) continue;
        const auto path = imagePath(pid);
        if (path.empty() || isMixer(path)) continue;
        const auto root = sameApplicationRoot(pid, path, table);
        // Capturing a launcher ancestor would include our engine's renders in
        // its process tree and allow an external feedback path.
        if (containsMixerProcess(root, table)) continue;
        auto& info = roots[root];
        info.processId = root;
        info.executablePath = utf8(path);
        // Keep labels stable as a browser tab or song title changes.
        auto name = basename(path);
        if (name.size() > 4 && samePath(name.substr(name.size() - 4), L".exe")) name.resize(name.size() - 4);
        info.name = utf8(name);
        info.hasAudioSession = info.hasAudioSession || candidate.audio;
    }
    std::vector<ApplicationInfo> result;
    result.reserve(roots.size());
    for (auto& [pid, info] : roots) result.push_back(std::move(info));
    std::sort(result.begin(), result.end(), [](const auto& a, const auto& b) {
        if (a.hasAudioSession != b.hasAudioSession) return a.hasAudioSession > b.hasAudioSession;
        if (a.name != b.name) return a.name < b.name;
        return a.processId < b.processId;
    });
    return result;
}

struct ProcessCaptureWorker::Impl {
    std::shared_ptr<FrameQueue> queue;
    Handle stopEvent{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    Handle controlEvent{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    mutable std::mutex mutex;
    ProcessBinding desired;
    PathState path;
    std::atomic<std::uint64_t> serial{};
    std::atomic<bool> stopped{}, running{};
    std::thread thread;

    explicit Impl(std::shared_ptr<FrameQueue> frames) : queue(std::move(frames)) {
        if (!queue || !stopEvent.valid() || !controlEvent.valid())
            throw std::runtime_error("Could not create application capture worker");
        thread = std::thread([this] { run(); });
    }
    ~Impl() { stop(); }
    void stop() noexcept {
        running.store(false, std::memory_order_release);
        if (!stopped.exchange(true)) { SetEvent(stopEvent.get()); SetEvent(controlEvent.get()); }
        if (thread.joinable()) thread.join();
    }
    void state(std::string status, std::string message = {}, DWORD pid = 0) {
        running.store(status == "Running", std::memory_order_release);
        std::lock_guard lock(mutex);
        path.state = std::move(status); path.message = std::move(message);
        path.endpointId = pid ? "process:" + std::to_string(pid) : std::string{};
        path.channels = pid ? 2 : 0; path.rate = pid ? sampleRate : 0;
    }
    std::optional<DWORD> resolve(const ProcessBinding& selection, bool& ambiguous) {
        const auto path = wide(selection.executablePath);
        if (path.empty() || isMixer(path)) return {};
        const auto table = processTable();
        if (selection.processId && table.contains(selection.processId) && samePath(imagePath(selection.processId), path)) {
            const auto root = sameApplicationRoot(selection.processId, path, table);
            return containsMixerProcess(root, table) ? std::optional<DWORD>{} : root;
        }
        std::set<DWORD> roots;
        for (const auto& [pid, entry] : table) {
            if (samePath(entry.filename, basename(path)) && samePath(imagePath(pid), path)) {
                const auto root = sameApplicationRoot(pid, path, table);
                if (!containsMixerProcess(root, table)) roots.insert(root);
            }
        }
        ambiguous = roots.size() > 1;
        if (roots.size() == 1) return *roots.begin();
        return {};
    }
    HRESULT capture(DWORD pid, const ProcessBinding& selection, std::uint64_t selectedSerial) {
        Handle process{OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid)};
        if (!process.valid()) return HRESULT_FROM_WIN32(GetLastError());
        if (!samePath(imagePath(process.get()), wide(selection.executablePath))) return S_FALSE;
        auto activation = std::make_shared<ActivationState>(pid);
        if (!activation->completed.valid()) return HRESULT_FROM_WIN32(GetLastError());
        ComPtr<IActivateAudioInterfaceCompletionHandler> handler;
        *handler.put() = new ActivationHandler(activation);
        ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
        const auto activate = activationFunction();
        if (!activate) return E_NOTIMPL;
        auto result = activate(L"VAD\\Process_Loopback", __uuidof(IAudioClient), &activation->variant, handler.get(), operation.put());
        if (FAILED(result)) return result;
        const HANDLE activationEvents[]{stopEvent.get(), controlEvent.get(), process.get(), activation->completed.get()};
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
        for (;;) {
            const DWORD wait = WaitForMultipleObjects(4, activationEvents, FALSE, 250);
            if (wait == WAIT_OBJECT_0 || wait == WAIT_OBJECT_0 + 2 || serial.load(std::memory_order_acquire) != selectedSerial) return S_FALSE;
            if (wait == WAIT_OBJECT_0 + 3) break;
            if (wait == WAIT_FAILED) return HRESULT_FROM_WIN32(GetLastError());
            if (std::chrono::steady_clock::now() >= deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        }
        if (FAILED(activation->result)) return activation->result;
        auto* client = activation->client.get();
        if (!client) return E_POINTER;
        WAVEFORMATEX format{};
        format.wFormatTag = WAVE_FORMAT_IEEE_FLOAT;
        format.nChannels = 2; format.nSamplesPerSec = sampleRate; format.wBitsPerSample = 32;
        format.nBlockAlign = 8; format.nAvgBytesPerSec = sampleRate * 8;
        result = client->Initialize(AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            0, 0, &format, nullptr);
        if (FAILED(result)) return result;
        Handle audioEvent{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
        if (!audioEvent.valid()) return HRESULT_FROM_WIN32(GetLastError());
        if (FAILED(result = client->SetEventHandle(audioEvent.get()))) return result;
        ComPtr<IAudioCaptureClient> captureClient;
        if (FAILED(result = client->GetService(__uuidof(IAudioCaptureClient), captureClient.out()))) return result;
        std::array<Frame, 4096> input{}, output{};
        AdaptiveResampler resampler(sampleRate, sampleRate);
        DriftController drift;
        DWORD taskIndex{};
        const auto task = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex);
        struct RevertTask { HANDLE task; ~RevertTask() { if (task) AvRevertMmThreadCharacteristics(task); } } revert{task};
        if (FAILED(result = client->Start())) return result;
        struct StopClient { IAudioClient* client; ~StopClient() { client->Stop(); } } stop{client};
        queue->requestClear();
        { std::lock_guard lock(mutex); ++path.reconnects; }
        state("Running", "Capturing app audio; original playback remains unchanged", pid);
        const HANDLE events[]{stopEvent.get(), controlEvent.get(), process.get(), audioEvent.get()};
        auto lastPacket = std::chrono::steady_clock::now();
        std::uint32_t fade{};
        bool suspended{};
        while (!stopped.load(std::memory_order_relaxed)) {
            const DWORD wait = WaitForMultipleObjects(4, events, FALSE, 250);
            if (wait == WAIT_OBJECT_0 || wait == WAIT_OBJECT_0 + 2 || serial.load(std::memory_order_acquire) != selectedSerial) return S_FALSE;
            if (wait == WAIT_FAILED) return HRESULT_FROM_WIN32(GetLastError());
            const auto now = std::chrono::steady_clock::now();
            // An app can stop rendering without exiting. Silence is healthy;
            // do not repeatedly reopen a paused app. Discard stale filter state.
            if (now - lastPacket > std::chrono::milliseconds(250) && !suspended) {
                resampler.reset(); drift.reset(); queue->requestClear(); fade = 0; suspended = true;
            }
            UINT32 packet{};
            if (FAILED(result = captureClient->GetNextPacketSize(&packet))) return result;
            while (packet) {
                BYTE* bytes{}; UINT32 count{}; DWORD flags{};
                if (FAILED(result = captureClient->GetBuffer(&bytes, &count, &flags, nullptr, nullptr))) return result;
                const double elapsed = std::clamp(std::chrono::duration<double>(now - lastPacket).count(), 0.001, 0.25);
                if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) {
                    resampler.reset(); drift.reset(); queue->requestClear(); fade = 0;
                }
                lastPacket = now; suspended = false;
                const auto correction = drift.update(static_cast<double>(queue->size()), sampleRate * 0.015, elapsed);
                std::size_t offset{};
                while (offset < count) {
                    const auto frames = std::min<std::size_t>(input.size(), count - offset);
                    for (std::size_t i = 0; i < frames; ++i) {
                        Frame sample{};
                        if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && bytes) std::memcpy(&sample, bytes + (offset + i) * sizeof(Frame), sizeof(Frame));
                        const float gain = static_cast<float>(std::min(++fade, sampleRate / 100)) / (sampleRate / 100);
                        // Keep the bounded fade counter from wrapping on a long soak.
                        fade = std::min(fade, sampleRate / 100);
                        input[i] = {std::isfinite(sample.left) ? sample.left * gain : 0.0f,
                                    std::isfinite(sample.right) ? sample.right * gain : 0.0f};
                    }
                    if (resampler.push(std::span<const Frame>(input.data(), frames)) != frames) {
                        resampler.reset(); queue->requestClear(); fade = 0;
                    }
                    for (;;) {
                        const auto generated = resampler.produce(output, correction);
                        if (resampler.lastError()) { captureClient->ReleaseBuffer(count); return E_FAIL; }
                        if (!generated) break;
                        if (queue->push(std::span<const Frame>(output.data(), generated)) != generated) queue->requestClear();
                        if (generated < output.size()) break;
                    }
                    offset += frames;
                }
                if (FAILED(result = captureClient->ReleaseBuffer(count))) return result;
                if (FAILED(result = captureClient->GetNextPacketSize(&packet))) return result;
                if (stopped.load(std::memory_order_relaxed) || serial.load(std::memory_order_acquire) != selectedSerial) return S_FALSE;
            }
        }
        return S_FALSE;
    }
    void run() {
        Apartment apartment;
        const HANDLE events[]{stopEvent.get(), controlEvent.get()};
        unsigned failures{};
        std::uint64_t previousSerial{};
        while (!stopped.load(std::memory_order_relaxed)) {
            ProcessBinding selection;
            std::uint64_t selectedSerial{};
            { std::lock_guard lock(mutex); selection = desired; selectedSerial = serial.load(std::memory_order_acquire); }
            if (selectedSerial != previousSerial) { failures = 0; previousSerial = selectedSerial; }
            DWORD delay = 1000;
            if (selection.executablePath.empty()) {
                state("Unassigned", "Choose a running application");
                delay = INFINITE;
            }
            else if (!processCaptureAvailable()) { state("Unsupported", "App capture requires Windows build 20348 or later"); delay = INFINITE; }
            else if (FAILED(apartment.result)) { state("Error", "Windows audio initialization failed"); delay = INFINITE; }
            else {
                bool ambiguous{};
                const auto pid = resolve(selection, ambiguous);
                if (!pid) state("Waiting", ambiguous ? "Several instances are running; select the app again" : "Waiting for application");
                else {
                    state("Opening", "Opening app audio", *pid);
                    HRESULT result;
                    try { result = capture(*pid, selection, selectedSerial); }
                    catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; }
                    catch (const std::exception&) { result = E_FAIL; }
                    running.store(false, std::memory_order_release); queue->requestClear();
                    if (stopped.load(std::memory_order_relaxed)) break;
                    if (result == S_FALSE) { failures = 0; continue; }
                    state("Error", failureMessage(result), *pid);
                    // Do not accumulate unresolved async activation requests if
                    // Windows never calls back. A deliberate retry can reopen it.
                    delay = result == HRESULT_FROM_WIN32(ERROR_TIMEOUT) ? INFINITE :
                        std::array<DWORD, 4>{250, 1000, 2000, 5000}[std::min(failures++, 3u)];
                }
            }
            if (WaitForMultipleObjects(2, events, FALSE, delay) == WAIT_OBJECT_0) break;
        }
    }
};

ProcessCaptureWorker::ProcessCaptureWorker(std::shared_ptr<FrameQueue> queue) : impl_(std::make_unique<Impl>(std::move(queue))) {}
ProcessCaptureWorker::~ProcessCaptureWorker() = default;
void ProcessCaptureWorker::configure(ProcessBinding binding) {
    {
        std::lock_guard lock(impl_->mutex);
        const auto& old = impl_->desired;
        if (old.processId == binding.processId && old.executablePath == binding.executablePath && old.name == binding.name) return;
        impl_->desired = std::move(binding); impl_->serial.fetch_add(1, std::memory_order_release);
    }
    SetEvent(impl_->controlEvent.get());
}
void ProcessCaptureWorker::retry() noexcept { impl_->serial.fetch_add(1, std::memory_order_release); SetEvent(impl_->controlEvent.get()); }
bool ProcessCaptureWorker::ready() const noexcept { return impl_->running.load(std::memory_order_acquire); }
PathState ProcessCaptureWorker::status() const {
    std::lock_guard lock(impl_->mutex);
    auto path = impl_->path;
    path.queueMs = static_cast<double>(impl_->queue->size()) * 1000 / sampleRate;
    path.underruns = impl_->queue->underrunEvents(); path.overruns = impl_->queue->overrunEvents();
    return path;
}
void ProcessCaptureWorker::stop() noexcept { impl_->stop(); }
}
