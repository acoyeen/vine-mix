#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include "am/windows_audio.hpp"
#include "am/windows_audio_detail.hpp"
#include "am/dsp.hpp"
#include "am/audio_timing.hpp"
#include "am/stream_stability.hpp"

#include <windows.h>
#include <initguid.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audiopolicy.h>
#include <avrt.h>
#include <propsys.h>
#include <propvarutil.h>
#include <ksmedia.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cctype>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstring>
#include <limits>
#include <mutex>
#include <optional>
#include <span>
#include <sstream>
#include <stdexcept>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace am {
namespace {
template<class T> class ComPtr {
public:
    ComPtr() = default;
    ~ComPtr() { reset(); }
    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;
    ComPtr(ComPtr&& other) noexcept : value_(std::exchange(other.value_, nullptr)) {}
    ComPtr& operator=(ComPtr&& other) noexcept {
        if (this != &other) { reset(); value_ = std::exchange(other.value_, nullptr); }
        return *this;
    }
    T* get() const noexcept { return value_; }
    T* operator->() const noexcept { return value_; }
    explicit operator bool() const noexcept { return value_ != nullptr; }
    T** put() noexcept { reset(); return &value_; }
    void** out() noexcept { return reinterpret_cast<void**>(put()); }
    void reset() noexcept { if (value_) { value_->Release(); value_ = nullptr; } }
    T* detach() noexcept { return std::exchange(value_, nullptr); }
private:
    T* value_{};
};

class Handle {
public:
    explicit Handle(HANDLE value = nullptr) : value_(value) {}
    ~Handle() { if (value_ && value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    HANDLE get() const noexcept { return value_; }
private:
    HANDLE value_{};
};

struct ComApartment {
    HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    ~ComApartment() { if (SUCCEEDED(result)) CoUninitialize(); }
};

// Property keys are defined here to avoid relying on SDK-specific property-key
// libraries. These are the documented Windows device/endpoint property keys.
constexpr PROPERTYKEY friendlyNameKey{{0xa45c254e,0xdf1c,0x4efd,{0x80,0x20,0x67,0xd1,0x46,0xa8,0x50,0xe0}},14};
constexpr PROPERTYKEY descriptionKey{{0xa45c254e,0xdf1c,0x4efd,{0x80,0x20,0x67,0xd1,0x46,0xa8,0x50,0xe0}},2};
constexpr PROPERTYKEY interfaceNameKey{{0x026e516e,0xb814,0x414b,{0x83,0xcd,0x85,0x6d,0x6f,0xef,0x48,0x22}},2};
constexpr PROPERTYKEY formFactorKey{{0x1da5d803,0xd492,0x4edd,{0x8c,0x23,0xe0,0xc0,0xff,0xee,0x7f,0x0e}},0};
constexpr GUID pcmSubtype{0x00000001,0x0000,0x0010,{0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71}};
constexpr GUID floatSubtype{0x00000003,0x0000,0x0010,{0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71}};

std::string utf8(std::wstring_view value) {
    if (value.empty()) return {};
    const int count = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<std::size_t>(count), '\0');
    if (count) WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), count, nullptr, nullptr);
    return result;
}

std::wstring wide(std::string_view value) {
    if (value.empty()) return {};
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring result(static_cast<std::size_t>(count), L'\0');
    if (count) MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), count);
    return result;
}

std::string property(IPropertyStore* properties, const PROPERTYKEY& key, bool* succeeded = nullptr) {
    PROPVARIANT value{};
    PropVariantInit(&value);
    std::string result;
    const auto status = properties->GetValue(key, &value);
    if (SUCCEEDED(status) && value.vt == VT_LPWSTR && value.pwszVal)
        result = utf8(value.pwszVal);
    if (succeeded) *succeeded = SUCCEEDED(status) && !result.empty();
    PropVariantClear(&value);
    return result;
}

std::optional<int> endpointFormFactor(IPropertyStore* properties) {
    PROPVARIANT value{};
    PropVariantInit(&value);
    std::optional<int> result;
    // PKEY_AudioEndpoint_FormFactor is a documented VT_UI4. Reject malformed
    // values instead of inventing a category that could hide a usable device.
    if (SUCCEEDED(properties->GetValue(formFactorKey, &value)) && value.vt == VT_UI4 &&
            value.ulVal < static_cast<ULONG>(EndpointFormFactor_enum_count))
        result = static_cast<int>(value.ulVal);
    PropVariantClear(&value);
    return result;
}

bool virtualName(std::string description) {
    std::transform(description.begin(), description.end(), description.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    constexpr std::array<std::string_view, 12> markers{
        "virtual", "voicemeeter", "vb-audio", "vbaudio", "vb cable", "vb-cable",
        "cable input", "cable output", "motiv mix", "motive mix", "steelseries sonar", "wave link"};
    return std::any_of(markers.begin(), markers.end(), [&](auto marker) { return description.find(marker) != std::string::npos; });
}

std::string transportFamily(std::string_view deviceDescription, bool isVirtual) {
    // Driver/interface descriptions survive an endpoint-friendly-name rename.
    // A user-facing label alone is never enough to identify a cable family.
    std::string compact;
    compact.reserve(deviceDescription.size());
    for (const unsigned char character : deviceDescription)
        if (std::isalnum(character)) compact += static_cast<char>(std::tolower(character));
    if (compact.find("voicemeeter") != std::string::npos) return "otherVirtual";
    if (compact.find("vbaudiovirtualcable") != std::string::npos ||
        compact.find("vbcable") != std::string::npos ||
        compact.find("vbaudiocable") != std::string::npos) return "vbCable";
    if (compact.find("virtualaudiocable") != std::string::npos) return "vacLite";
    return isVirtual ? "otherVirtual" : "physical";
}

std::string endpointState(DWORD value) {
    if (value & DEVICE_STATE_ACTIVE) return "Active";
    if (value & DEVICE_STATE_DISABLED) return "Disabled";
    if (value & DEVICE_STATE_UNPLUGGED) return "Unplugged";
    return "Missing";
}

}
std::optional<std::vector<Endpoint>> detail::collectEndpoints(IMMDeviceEnumerator* enumerator,
        std::span<const Endpoint> previous) {
    if (!enumerator) return std::nullopt;
    std::vector<Endpoint> result;
    PROPERTYKEY stableIdKey{};
    // AudioEndpoint_GUID is the DirectSound endpoint GUID, not StableId. Older
    // Windows/property schemas may not expose the new canonical StableId key;
    // leave it empty and retain the opaque endpoint ID in that case.
    const bool hasStableIdKey = SUCCEEDED(PSGetPropertyKeyFromName(L"System.AudioEndpoint.StableId", &stableIdKey));
    for (const auto direction : {eCapture, eRender}) {
        ComPtr<IMMDeviceCollection> devices;
        if (FAILED(enumerator->EnumAudioEndpoints(direction, DEVICE_STATEMASK_ALL, devices.put())) || !devices)
            return std::nullopt;
        UINT count{};
        if (FAILED(devices->GetCount(&count))) return std::nullopt;
        for (UINT index = 0; index < count; ++index) {
            ComPtr<IMMDevice> device;
            if (FAILED(devices->Item(index, device.put())) || !device) return std::nullopt;
            LPWSTR id{};
            if (FAILED(device->GetId(&id)) || !id) return std::nullopt;
            Endpoint endpoint;
            endpoint.id = utf8(id);
            CoTaskMemFree(id);
            endpoint.direction = direction == eCapture ? "capture" : "render";
            const auto known = std::find_if(previous.begin(), previous.end(), [&](const auto& candidate) {
                return candidate.id == endpoint.id && candidate.direction == endpoint.direction;
            });
            // Optional metadata probes may fail even while an existing stream
            // remains usable. Keep known metadata for this exact identity so a
            // failed query cannot look like a format or transport change.
            if (known != previous.end()) endpoint = *known;
            DWORD state{};
            if (FAILED(device->GetState(&state))) return std::nullopt;
            endpoint.state = endpointState(state);
            if (endpoint.transport.empty()) endpoint.transport = "physical";
            ComPtr<IPropertyStore> properties;
            if (SUCCEEDED(device->OpenPropertyStore(STGM_READ, properties.put())) && properties) {
                const auto name = property(properties.get(), friendlyNameKey);
                if (!name.empty()) endpoint.name = name;
                if (hasStableIdKey) {
                    const auto stableId = property(properties.get(), stableIdKey);
                    if (!stableId.empty()) endpoint.stableId = stableId;
                }
                bool haveDescription{}, haveInterface{};
                const auto description = property(properties.get(), descriptionKey, &haveDescription);
                const auto interfaceName = property(properties.get(), interfaceNameKey, &haveInterface);
                if (haveDescription) endpoint.description = description;
                if (haveInterface) endpoint.interfaceName = interfaceName;
                if (const auto formFactor = endpointFormFactor(properties.get())) endpoint.formFactor = *formFactor;
                const auto driverDescription = description + " " + interfaceName;
                if ((haveDescription && haveInterface) || known == previous.end()) {
                    endpoint.isVirtual = virtualName(driverDescription + " " + endpoint.name);
                    endpoint.transport = transportFamily(driverDescription, endpoint.isVirtual);
                    endpoint.isVirtual = endpoint.transport != "physical";
                }
            }
            if (endpoint.name.empty()) endpoint.name = endpoint.id;
            if (state & DEVICE_STATE_ACTIVE) {
                ComPtr<IAudioClient> client;
                if (SUCCEEDED(device->Activate(IID_IAudioClient, CLSCTX_ALL, nullptr, client.out()))) {
                    WAVEFORMATEX* format{};
                    if (SUCCEEDED(client->GetMixFormat(&format)) && format) {
                        endpoint.channels = format->nChannels;
                        endpoint.rate = format->nSamplesPerSec;
                        CoTaskMemFree(format);
                    }
                }
            }
            result.push_back(std::move(endpoint));
        }
    }
    std::sort(result.begin(), result.end(), [](const auto& a, const auto& b) { return a.id < b.id; });
    return result;
}

namespace {
bool sameEndpoints(const std::vector<Endpoint>& a, const std::vector<Endpoint>& b) {
    if (a.size() != b.size()) return false;
    for (std::size_t i = 0; i < a.size(); ++i) {
        const auto& x = a[i]; const auto& y = b[i];
        if (x.id != y.id || x.stableId != y.stableId || x.name != y.name || x.direction != y.direction ||
            x.state != y.state || x.channels != y.channels || x.rate != y.rate || x.isVirtual != y.isVirtual || x.transport != y.transport ||
            x.formFactor != y.formFactor || x.description != y.description || x.interfaceName != y.interfaceName) return false;
    }
    return true;
}

class Notifications final : public IMMNotificationClient {
public:
    Notifications(std::atomic<std::uint64_t>& generation, HANDLE wake) : generation_(generation), wake_(wake) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IMMNotificationClient)) {
            *value = static_cast<IMMNotificationClient*>(this); AddRef(); return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return references_.fetch_add(1) + 1; }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto value = references_.fetch_sub(1) - 1;
        if (!value) delete this;
        return value;
    }
    HRESULT STDMETHODCALLTYPE OnDeviceStateChanged(LPCWSTR, DWORD) override { return changed(); }
    HRESULT STDMETHODCALLTYPE OnDeviceAdded(LPCWSTR) override { return changed(); }
    HRESULT STDMETHODCALLTYPE OnDeviceRemoved(LPCWSTR) override { return changed(); }
    HRESULT STDMETHODCALLTYPE OnDefaultDeviceChanged(EDataFlow, ERole, LPCWSTR) override { return changed(); }
    HRESULT STDMETHODCALLTYPE OnPropertyValueChanged(LPCWSTR, const PROPERTYKEY) override { return changed(); }
private:
    HRESULT changed() noexcept { generation_.fetch_add(1, std::memory_order_relaxed); SetEvent(wake_); return S_OK; }
    std::atomic<ULONG> references_{1};
    std::atomic<std::uint64_t>& generation_;
    HANDLE wake_{};
};

// Decoding/encoding never relies on alignment of a driver's packed samples.
struct SampleFormat {
    std::uint32_t rate{};
    WORD channels{};
    WORD bytes{};
    WORD blockAlign{};
    DWORD channelMask{};
    bool floating{};
    std::array<float, 64> left{};
    std::array<float, 64> right{};

    bool initialize(const WAVEFORMATEX& wave) {
        rate = wave.nSamplesPerSec; channels = wave.nChannels;
        bytes = wave.wBitsPerSample / 8; blockAlign = wave.nBlockAlign;
        WORD tag = wave.wFormatTag;
        if (tag == WAVE_FORMAT_EXTENSIBLE) {
            if (wave.cbSize < sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)) return false;
            const auto& extended = reinterpret_cast<const WAVEFORMATEXTENSIBLE&>(wave);
            channelMask = extended.dwChannelMask;
            if (IsEqualGUID(extended.SubFormat, floatSubtype)) tag = WAVE_FORMAT_IEEE_FLOAT;
            else if (IsEqualGUID(extended.SubFormat, pcmSubtype)) tag = WAVE_FORMAT_PCM;
            else return false;
        }
        floating = tag == WAVE_FORMAT_IEEE_FLOAT;
        if ((!floating && tag != WAVE_FORMAT_PCM) || channels == 0 || channels > 64 ||
            rate < 8000 || rate > 384000 || wave.wBitsPerSample % 8 || blockAlign < channels * bytes ||
            (floating ? bytes != 4 && bytes != 8 : bytes != 1 && bytes != 2 && bytes != 3 && bytes != 4)) return false;
        if (channelMask && std::popcount(channelMask) != channels) channelMask = 0;
        if (channels == 1) { left[0] = 1; right[0] = 1; }
        else if (!channelMask) { left[0] = 1; right[1] = 1; }
        else {
            unsigned index = 0;
            for (unsigned bit = 0; bit < 32 && index < channels; ++bit) {
                const DWORD speaker = 1u << bit;
                if (!(channelMask & speaker)) continue;
                switch (speaker) {
                case SPEAKER_FRONT_LEFT: left[index] = 1; break;
                case SPEAKER_FRONT_RIGHT: right[index] = 1; break;
                case SPEAKER_FRONT_CENTER: left[index] = right[index] = 0.70710678f; break;
                case SPEAKER_LOW_FREQUENCY: left[index] = right[index] = 0.25f; break;
                case SPEAKER_BACK_LEFT: case SPEAKER_SIDE_LEFT: case SPEAKER_FRONT_LEFT_OF_CENTER: left[index] = 0.70710678f; break;
                case SPEAKER_BACK_RIGHT: case SPEAKER_SIDE_RIGHT: case SPEAKER_FRONT_RIGHT_OF_CENTER: right[index] = 0.70710678f; break;
                default: left[index] = right[index] = 0.5f; break;
                }
                ++index;
            }
        }
        return true;
    }

    float read(const BYTE* source) const noexcept {
        if (floating) {
            double value{};
            if (bytes == 4) { float sample; std::memcpy(&sample, source, 4); value = sample; }
            else std::memcpy(&value, source, 8);
            return std::isfinite(value) ? static_cast<float>(std::clamp(value, -32.0, 32.0)) : 0;
        }
        if (bytes == 1) return (static_cast<int>(*source) - 128) / 128.f;
        if (bytes == 2) { std::int16_t value; std::memcpy(&value, source, 2); return value / 32768.f; }
        if (bytes == 3) {
            std::int32_t value = source[0] | (static_cast<std::int32_t>(source[1]) << 8) | (static_cast<std::int32_t>(source[2]) << 16);
            if (value & 0x800000) value |= static_cast<std::int32_t>(0xff000000u);
            return value / 8388608.f;
        }
        std::int32_t value; std::memcpy(&value, source, 4); return static_cast<float>(value / 2147483648.0);
    }

    void decode(const BYTE* data, std::span<Frame> frames, int channel, bool silent) const noexcept {
        if (silent || !data) { std::fill(frames.begin(), frames.end(), Frame{}); return; }
        for (auto& frame : frames) {
            if (channel >= 0) frame.left = frame.right = read(data + channel * bytes);
            else {
                frame = {};
                for (WORD c = 0; c < channels; ++c) {
                    const float value = read(data + c * bytes);
                    frame.left += value * left[c]; frame.right += value * right[c];
                }
            }
            data += blockAlign;
        }
    }

    void write(BYTE* destination, float value) const noexcept {
        value = std::isfinite(value) ? std::clamp(value, -1.f, 1.f) : 0;
        if (floating) {
            if (bytes == 4) std::memcpy(destination, &value, 4);
            else { const double sample = value; std::memcpy(destination, &sample, 8); }
        } else if (bytes == 1) {
            *destination = static_cast<BYTE>(std::clamp(std::lround(value * 128.f + 128.f), 0l, 255l));
        } else if (bytes == 2) {
            const auto sample = static_cast<std::int16_t>(std::clamp(std::lround(value * 32768.f), -32768l, 32767l));
            std::memcpy(destination, &sample, 2);
        } else if (bytes == 3) {
            const auto sample = static_cast<std::int32_t>(std::clamp(std::llround(value * 8388608.0), -8388608ll, 8388607ll));
            const auto bits = static_cast<std::uint32_t>(sample);
            destination[0] = static_cast<BYTE>(bits); destination[1] = static_cast<BYTE>(bits >> 8); destination[2] = static_cast<BYTE>(bits >> 16);
        } else {
            const auto sample = static_cast<std::int32_t>(std::clamp(std::llround(value * 2147483648.0), -2147483648ll, 2147483647ll));
            std::memcpy(destination, &sample, 4);
        }
    }

    void encode(std::span<const Frame> frames, BYTE* destination) const noexcept {
        for (const auto frame : frames) {
            if (channels == 1) write(destination, (frame.left + frame.right) * 0.5f);
            else {
                for (WORD c = 0; c < channels; ++c) {
                    // Place stereo on front L/R only; do not duplicate speech
                    // into every surround speaker or into the LFE channel.
                    float sample{};
                    if (!channelMask) { if (c == 0) sample = frame.left; if (c == 1) sample = frame.right; }
                    else {
                        DWORD mask = channelMask;
                        unsigned bit = 0;
                        for (WORD i = 0; i <= c && mask; ++i) {
                            bit = static_cast<unsigned>(std::countr_zero(mask));
                            mask &= mask - 1;
                        }
                        if ((1u << bit) == SPEAKER_FRONT_LEFT) sample = frame.left;
                        if ((1u << bit) == SPEAKER_FRONT_RIGHT) sample = frame.right;
                        if (!(channelMask & (SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT)) && (1u << bit) == SPEAKER_FRONT_CENTER)
                            sample = (frame.left + frame.right) * 0.5f;
                    }
                    write(destination + c * bytes, sample);
                }
            }
            destination += blockAlign;
        }
    }
};

std::string failureMessage(HRESULT result, bool exclusive = false) {
    if (exclusive) switch (result) {
    case AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED:
        return "Direct virtual output is disabled; allow exclusive control in the VAC Lite playback device's Windows sound properties";
    case AUDCLNT_E_DEVICE_IN_USE:
        return "Direct virtual output is busy; close other playback apps using this VAC Lite line, then retry audio";
    case AUDCLNT_E_UNSUPPORTED_FORMAT:
        return "VAC Lite has no supported direct output format; virtual output remains stopped to avoid shared playback capture";
    case AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED: case AUDCLNT_E_BUFFER_SIZE_ERROR: case AUDCLNT_E_INVALID_DEVICE_PERIOD:
        return "VAC Lite could not open a direct output buffer; retry audio or check its driver settings";
    case E_ACCESSDENIED:
        return "Windows denied direct virtual output access; check the VAC Lite playback device settings";
    }
    switch (result) {
    case AUDCLNT_E_DEVICE_INVALIDATED: return "Waiting for device";
    case AUDCLNT_E_DEVICE_IN_USE: case AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED: return "Device busy; close its exclusive-mode application";
    case E_ACCESSDENIED: return "Microphone access blocked; check Windows microphone privacy settings";
    case AUDCLNT_E_SERVICE_NOT_RUNNING: return "Windows Audio service unavailable";
    case AUDCLNT_E_UNSUPPORTED_FORMAT: return "Device audio format is unsupported";
    default: {
        std::ostringstream message;
        message << "Audio stream error 0x" << std::hex << static_cast<std::uint32_t>(result);
        return message.str();
    }
    }
}

std::string failureState(HRESULT result) {
    if (result == AUDCLNT_E_DEVICE_IN_USE || result == AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED) return "Busy";
    if (result == E_ACCESSDENIED) return "AccessDenied";
    if (result == AUDCLNT_E_SERVICE_NOT_RUNNING) return "ServiceUnavailable";
    if (result == AUDCLNT_E_UNSUPPORTED_FORMAT || result == E_INVALIDARG) return "Error";
    return "Recovering";
}

struct ClockEstimate {
    std::uint64_t position{};
    std::uint64_t qpc{};
    bool anchored{};
    double multiplier = 1;
    void observe(std::uint64_t currentPosition, std::uint64_t currentQpc, double unitsPerSecond) {
        if (!currentQpc || unitsPerSecond <= 0) return;
        if (!anchored || currentPosition < position || currentQpc <= qpc) {
            position = currentPosition; qpc = currentQpc; anchored = true; return;
        }
        const double seconds = (currentQpc - qpc) / 10000000.0;
        if (seconds < 0.5) return;
        const double estimate = (currentPosition - position) / seconds / unitsPerSecond;
        if (seconds < 3 && estimate > 0.995 && estimate < 1.005)
            multiplier += (estimate - multiplier) * 0.08;
        position = currentPosition; qpc = currentQpc;
    }
};
}

long detail::initializeExclusiveRender(IMMDevice* device, IAudioClient** returnedClient, WAVEFORMATEX** returnedFormat) {
    if (!device || !returnedClient || !returnedFormat) return E_POINTER;
    *returnedClient = nullptr; *returnedFormat = nullptr;
    ComPtr<IAudioClient> client;
    const auto activate = [&]() -> HRESULT {
        client.reset();
        return device->Activate(IID_IAudioClient, CLSCTX_ALL, nullptr, client.out());
    };
    HRESULT result = activate();
    if (FAILED(result)) return result;
    WAVEFORMATEXTENSIBLE selected{};
    std::size_t selectedBytes{};
    const auto consider = [&](const WAVEFORMATEX& candidate) -> HRESULT {
        SampleFormat supported;
        if (!supported.initialize(candidate) || candidate.cbSize > sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX))
            return AUDCLNT_E_UNSUPPORTED_FORMAT;
        if (candidate.wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
            const auto& extended = reinterpret_cast<const WAVEFORMATEXTENSIBLE&>(candidate);
            // The sample converter writes full-width PCM. Do not advertise
            // padding-bit formats such as 24 valid bits in a 32-bit container.
            if (extended.Samples.wValidBitsPerSample != candidate.wBitsPerSample)
                return AUDCLNT_E_UNSUPPORTED_FORMAT;
        }
        const auto answer = client->IsFormatSupported(AUDCLNT_SHAREMODE_EXCLUSIVE, &candidate, nullptr);
        if (answer == S_OK) {
            selectedBytes = sizeof(WAVEFORMATEX) + candidate.cbSize;
            std::memcpy(&selected, &candidate, selectedBytes);
        }
        return answer;
    };
    // Try formats the converter can actually write, preferring the graph's
    // native rate. Both descriptors are necessary for stereo PCM drivers.
    for (const DWORD rate : {48000u, 44100u, 96000u}) {
        for (const WORD tag : {WORD{WAVE_FORMAT_PCM}, WORD{WAVE_FORMAT_IEEE_FLOAT}}) {
            for (const WORD bits : {WORD{16}, WORD{32}}) {
                if (tag == WAVE_FORMAT_IEEE_FLOAT && bits != 32) continue;
                for (const bool extended : {false, true}) {
                    WAVEFORMATEXTENSIBLE candidate{};
                    candidate.Format = {extended ? WORD{WAVE_FORMAT_EXTENSIBLE} : tag, 2, rate,
                        rate * 2 * bits / 8, WORD(2 * bits / 8), bits,
                        extended ? WORD(sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)) : WORD{0}};
                    candidate.Samples.wValidBitsPerSample = bits;
                    candidate.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
                    candidate.SubFormat = tag == WAVE_FORMAT_PCM ? pcmSubtype : floatSubtype;
                    result = consider(candidate.Format);
                    if (selectedBytes) break;
                    if (FAILED(result) && result != AUDCLNT_E_UNSUPPORTED_FORMAT && result != E_INVALIDARG) return result;
                }
                if (selectedBytes) break;
            }
            if (selectedBytes) break;
        }
        if (selectedBytes) break;
    }
    if (!selectedBytes) {
        WAVEFORMATEX* mix{};
        result = client->GetMixFormat(&mix);
        if (SUCCEEDED(result) && mix) {
            result = consider(*mix);
            CoTaskMemFree(mix);
        }
        if (!selectedBytes) return FAILED(result) ? result : AUDCLNT_E_UNSUPPORTED_FORMAT;
    }
    REFERENCE_TIME defaultPeriod{}, minimumPeriod{};
    if (FAILED(result = client->GetDevicePeriod(&defaultPeriod, &minimumPeriod))) return result;
    REFERENCE_TIME period = std::max(defaultPeriod, minimumPeriod);
    if (period <= 0 || period > 10000000) return AUDCLNT_E_INVALID_DEVICE_PERIOD;
    result = client->Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        period, period, &selected.Format, nullptr);
    if (result == AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED) {
        UINT32 alignedFrames{};
        if (FAILED(result = client->GetBufferSize(&alignedFrames))) return result;
        if (!alignedFrames || alignedFrames > selected.Format.nSamplesPerSec) return AUDCLNT_E_BUFFER_SIZE_ERROR;
        period = static_cast<REFERENCE_TIME>((10000000ull * alignedFrames + selected.Format.nSamplesPerSec / 2)
            / selected.Format.nSamplesPerSec);
        if (FAILED(result = activate())) return result;
        result = client->Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
            period, period, &selected.Format, nullptr);
    }
    if (FAILED(result)) return result;
    auto* allocated = static_cast<WAVEFORMATEX*>(CoTaskMemAlloc(selectedBytes));
    if (!allocated) return E_OUTOFMEMORY;
    std::memcpy(allocated, &selected, selectedBytes);
    *returnedFormat = allocated; *returnedClient = client.detach();
    return S_OK;
}

long detail::initializeSharedStream(IMMDevice* device, bool capture, IAudioClient** returnedClient,
    tWAVEFORMATEX** returnedFormat, std::uint32_t* periodFrames, bool preferLowLatency) {
    if (returnedClient) *returnedClient = nullptr;
    if (returnedFormat) *returnedFormat = nullptr;
    if (periodFrames) *periodFrames = 0;
    if (!device || !returnedClient || !returnedFormat || !periodFrames) return E_POINTER;
    // Keep the endpoint's mix format and processing category unchanged. A
    // shorter period changes scheduling, not the converter/effect quality.
    enum class Attempt { shortest, normal, legacy };
    Attempt attempt = preferLowLatency ? Attempt::shortest : Attempt::normal;
    std::uint32_t failedPeriod{};
    for (;;) {
        ComPtr<IAudioClient> client;
        auto result = device->Activate(IID_IAudioClient, CLSCTX_ALL, nullptr, client.out());
        if (FAILED(result)) return result;
#ifdef __IAudioClient2_INTERFACE_DEFINED__
        ComPtr<IAudioClient2> client2;
        if (SUCCEEDED(client->QueryInterface(__uuidof(IAudioClient2), client2.out()))) {
            AudioClientProperties properties{};
            properties.cbSize = sizeof(properties);
            properties.eCategory = capture ? AudioCategory_Other : AudioCategory_Media;
            client2->SetClientProperties(&properties);
        }
        client2.reset();
#endif
        WAVEFORMATEX* allocated{};
        result = client->GetMixFormat(&allocated);
        const auto freeFormat = [](WAVEFORMATEX* value) { CoTaskMemFree(value); };
        std::unique_ptr<WAVEFORMATEX, decltype(freeFormat)> wave(allocated, freeFormat);
        if (FAILED(result)) return result;
        if (!wave || !wave->nSamplesPerSec) return AUDCLNT_E_UNSUPPORTED_FORMAT;
        std::uint32_t selected{};
#ifdef __IAudioClient3_INTERFACE_DEFINED__
        ComPtr<IAudioClient3> client3;
        if (attempt != Attempt::legacy && SUCCEEDED(client->QueryInterface(__uuidof(IAudioClient3), client3.out()))) {
            UINT32 normal{}, fundamental{}, minimum{}, maximum{};
            if (SUCCEEDED(client3->GetSharedModeEnginePeriod(wave.get(), &normal, &fundamental, &minimum, &maximum))
                && fundamental && minimum && minimum <= maximum) {
                const auto shortest = (static_cast<std::uint64_t>(minimum) + fundamental - 1) / fundamental * fundamental;
                if (shortest <= maximum) {
                    if (attempt == Attempt::shortest) selected = static_cast<std::uint32_t>(shortest);
                    else if (normal >= minimum && normal <= maximum && normal % fundamental == 0 && normal != failedPeriod)
                        selected = normal;
                }
            }
            if (selected) {
                result = client3->InitializeSharedAudioStream(AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                    selected, wave.get(), nullptr);
                client3.reset();
                if (SUCCEEDED(result)) {
                    *returnedClient = client.detach(); *returnedFormat = wave.release(); *periodFrames = selected;
                    return S_OK;
                }
                // Neither the failed IAudioClient nor any queried interface
                // survives the next activation. Some drivers poison a client
                // even when Initialize* fails.
                failedPeriod = selected;
                attempt = attempt == Attempt::shortest ? Attempt::normal : Attempt::legacy;
                continue;
            }
        }
        client3.reset();
#endif
        result = client->Initialize(AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_NOPERSIST, 0, 0, wave.get(), nullptr);
        if (FAILED(result)) return result;
        REFERENCE_TIME normal{}, minimum{};
        if (SUCCEEDED(client->GetDevicePeriod(&normal, &minimum)) && normal > 0 && normal <= 10000000) {
            *periodFrames = static_cast<std::uint32_t>((static_cast<std::uint64_t>(normal) * wave->nSamplesPerSec + 9999999) / 10000000);
        }
        *returnedClient = client.detach(); *returnedFormat = wave.release();
        return S_OK;
    }
}

long detail::renderFramesAvailable(IAudioClient* client, bool exclusive, std::uint32_t bufferFrames,
    std::uint32_t* available) {
    if (!client || !available) return E_POINTER;
    *available = 0;
    if (exclusive) { *available = bufferFrames; return S_OK; }
    UINT32 padding{};
    const auto result = client->GetCurrentPadding(&padding);
    if (FAILED(result)) return result;
    if (padding > bufferFrames) return AUDCLNT_E_BUFFER_ERROR;
    *available = bufferFrames - padding;
    return S_OK;
}

struct DeviceCatalog::Impl {
    Handle stop{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    Handle wake{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    std::atomic<std::uint64_t> generation{1};
    mutable std::mutex mutex;
    std::vector<Endpoint> endpoints;
    std::condition_variable initializedCondition;
    bool initialized{};
    std::thread thread;

    Impl() {
        if (!stop.get() || !wake.get())
            throw std::runtime_error("Could not create device notification events");
        thread = std::thread([this] { run(); });
        std::unique_lock lock(mutex);
        initializedCondition.wait_for(lock, std::chrono::seconds(3), [this] { return initialized; });
    }
    ~Impl() { SetEvent(stop.get()); if (thread.joinable()) thread.join(); }

    void run() {
        ComApartment apartment;
        ComPtr<IMMDeviceEnumerator> enumerator;
        auto* notifications = new Notifications(generation, wake.get());
        bool registered{};
        const HANDLE events[]{stop.get(), wake.get()};
        while (WaitForSingleObject(stop.get(), 0) != WAIT_OBJECT_0) {
            if (!enumerator && SUCCEEDED(apartment.result))
                CoCreateInstance(CLSID_MMDeviceEnumerator, nullptr, CLSCTX_ALL, IID_IMMDeviceEnumerator, enumerator.out());
            if (enumerator && !registered)
                registered = SUCCEEDED(enumerator->RegisterEndpointNotificationCallback(notifications));
            DWORD delay = 5000;
            if (enumerator) {
                std::vector<Endpoint> previous;
                { std::lock_guard lock(mutex); previous = endpoints; }
                if (auto next = detail::collectEndpoints(enumerator.get(), previous)) {
                    std::lock_guard lock(mutex);
                    if (!sameEndpoints(endpoints, *next)) {
                        endpoints = std::move(*next); generation.fetch_add(1, std::memory_order_relaxed);
                    }
                } else {
                    // Transient enumeration failure is not evidence that every
                    // endpoint disappeared. Keep healthy streams attached and
                    // reconcile a complete inventory again shortly.
                    delay = 1000;
                }
            }
            { std::lock_guard lock(mutex); initialized = true; }
            initializedCondition.notify_all();
            if (WaitForMultipleObjects(2, events, FALSE, delay) == WAIT_OBJECT_0) break;
        }
        if (registered) enumerator->UnregisterEndpointNotificationCallback(notifications);
        notifications->Release();
    }
};

DeviceCatalog::DeviceCatalog() : impl_(std::make_unique<Impl>()) {}
DeviceCatalog::~DeviceCatalog() = default;
std::vector<Endpoint> DeviceCatalog::enumerate() const { std::lock_guard lock(impl_->mutex); return impl_->endpoints; }
std::uint64_t DeviceCatalog::generation() const noexcept { return impl_->generation.load(std::memory_order_relaxed); }
void DeviceCatalog::refresh() noexcept { impl_->generation.fetch_add(1, std::memory_order_relaxed); SetEvent(impl_->wake.get()); }

struct AudioWorker::Impl {
    const bool capture;
    const bool microphone;
    const std::string requiredTransport;
    const bool exclusiveRender;
    std::shared_ptr<FrameQueue> queue;
    DeviceCatalog& catalog;
    Handle stopEvent{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    Handle controlEvent{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    mutable std::mutex mutex;
    DeviceBinding desired;
    PathState path;
    std::atomic<std::uint64_t> configurationSerial{0};
    std::atomic<bool> stopped{false};
    std::atomic<bool> ready{false};
    std::uint64_t observedGeneration{};
    std::uint64_t observedConfiguration{};
    std::atomic<std::uint64_t> streamUnderruns{0};
    std::atomic<std::uint32_t> targetQueueFrames{0}, pendingResamplerFrames{0};
    std::atomic<std::uint32_t> enginePeriodFrames{0};
    bool preferLowLatency = true; // Stream-thread policy; explicit selection/retry resets it.
    std::thread thread;

    Impl(bool isCapture, bool isMicrophone, std::shared_ptr<FrameQueue> frames, DeviceCatalog& devices, std::string transport)
        : capture(isCapture), microphone(isMicrophone), requiredTransport(std::move(transport)),
          exclusiveRender(!isCapture && requiredTransport == "vacLite"),
          queue(std::move(frames)), catalog(devices) {
        if (!queue || !stopEvent.get() || !controlEvent.get())
            throw std::runtime_error("Could not create audio device worker");
        thread = std::thread([this] { run(); });
    }
    ~Impl() { stop(); }
    void stop() noexcept {
        ready.store(false, std::memory_order_release);
        if (!stopped.exchange(true)) { SetEvent(stopEvent.get()); SetEvent(controlEvent.get()); }
        if (thread.joinable()) thread.join();
    }
    void state(std::string value, std::string message = {}, const Endpoint* endpoint = nullptr) {
        ready.store(value == "Running", std::memory_order_release);
        if (value != "Running" && value != "Priming") {
            targetQueueFrames.store(0, std::memory_order_relaxed);
            pendingResamplerFrames.store(0, std::memory_order_relaxed);
            enginePeriodFrames.store(0, std::memory_order_relaxed);
        }
        std::lock_guard lock(mutex);
        path.state = std::move(value); path.message = std::move(message);
        if (path.state != "Running" && path.state != "Priming") path.periodMs = path.bufferMs = 0;
        if (endpoint) { path.endpointId = endpoint->id; path.channels = endpoint->channels; path.rate = endpoint->rate; }
        else if (path.state == "Unassigned") { path.endpointId.clear(); path.rate = path.channels = 0; }
    }
    DeviceBinding binding() const { std::lock_guard lock(mutex); return desired; }

    std::optional<Endpoint> resolve(const DeviceBinding& selection) {
        const auto devices = catalog.enumerate();
        const std::string_view direction = capture ? "capture" : "render";
        auto exact = std::find_if(devices.begin(), devices.end(), [&](const auto& endpoint) { return endpoint.id == selection.id && endpoint.direction == direction; });
        if (exact != devices.end() && exact->state == "Active") return *exact;
        if (!selection.stableId.empty()) {
            std::optional<Endpoint> stable;
            for (const auto& endpoint : devices) {
                if (endpoint.direction == direction && endpoint.stableId == selection.stableId && endpoint.state == "Active") {
                    if (stable) return std::nullopt; // Never guess between ambiguous identities.
                    stable = endpoint;
                }
            }
            if (stable) return stable;
        }
        return exact == devices.end() ? std::nullopt : std::optional<Endpoint>(*exact);
    }

    // A catalog update does not reopen healthy unrelated paths. Invalidated
    // clients report their own failure; disappearance of this endpoint is local.
    bool stillSelected(const Endpoint& endpoint, std::uint64_t serial) {
        if (configurationSerial.load(std::memory_order_acquire) != serial) return false;
        const auto currentGeneration = catalog.generation();
        if (currentGeneration == observedGeneration) return true;
        observedGeneration = currentGeneration;
        const auto selected = resolve(binding());
        return selected && selected->state == "Active" && selected->id == endpoint.id &&
            selected->channels == endpoint.channels && selected->rate == endpoint.rate &&
            (requiredTransport.empty() || selected->transport == requiredTransport);
    }

    HRESULT openAndRun(const Endpoint& endpoint, const DeviceBinding& selection, std::uint64_t serial) {
        ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT result = CoCreateInstance(CLSID_MMDeviceEnumerator, nullptr, CLSCTX_ALL, IID_IMMDeviceEnumerator, enumerator.out());
        if (FAILED(result)) return result;
        ComPtr<IMMDevice> device;
        const auto id = wide(endpoint.id);
        if (FAILED(result = enumerator->GetDevice(id.c_str(), device.put()))) return result;
        ComPtr<IAudioClient> client;
        WAVEFORMATEX* allocatedFormat{};
        std::uint32_t periodFrames{};
        if (exclusiveRender) {
            // The virtual microphone reaches its cable directly rather than
            // joining Windows' shared playback mix. Never fall back to shared
            // output: that would silently reintroduce screen-share audio.
            if (FAILED(result = detail::initializeExclusiveRender(device.get(), client.put(), &allocatedFormat))) return result;
        } else {
            if (FAILED(result = detail::initializeSharedStream(device.get(), capture,
                client.put(), &allocatedFormat, &periodFrames, preferLowLatency))) return result;
        }
        const auto freeFormat = [](WAVEFORMATEX* value) { CoTaskMemFree(value); };
        std::unique_ptr<WAVEFORMATEX, decltype(freeFormat)> wave(allocatedFormat, freeFormat);
        SampleFormat format;
        if (!format.initialize(*wave)) return AUDCLNT_E_UNSUPPORTED_FORMAT;
        const int channel = microphone ? selection.channel : -1;
        if (capture && channel >= static_cast<int>(format.channels)) return E_INVALIDARG;
        Handle audioEvent{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
        if (!audioEvent.get()) return HRESULT_FROM_WIN32(GetLastError());

        if (FAILED(result = client->SetEventHandle(audioEvent.get()))) return result;
        UINT32 bufferFrames{};
        if (FAILED(result = client->GetBufferSize(&bufferFrames))) return result;
        if (!bufferFrames || bufferFrames > format.rate * 4) return AUDCLNT_E_BUFFER_SIZE_ERROR;
        // Exclusive event rendering consumes a whole buffer each event. For a
        // legacy shared device whose period is unknown, use the actual buffer
        // as a conservative demand estimate rather than guess a short period.
        if (exclusiveRender || !periodFrames) periodFrames = bufferFrames;
        StreamBufferPolicy buffering(format.rate, periodFrames, capture);
        enginePeriodFrames.store(static_cast<std::uint32_t>(buffering.deviceFramesToEngineFrames(periodFrames)), std::memory_order_relaxed);

        ComPtr<IAudioSessionControl2> session;
        if (!capture && SUCCEEDED(client->GetService(__uuidof(IAudioSessionControl2), session.out())))
            session->SetDuckingPreference(TRUE);
        ComPtr<IAudioCaptureClient> captureClient;
        ComPtr<IAudioRenderClient> renderClient;
        if (capture) result = client->GetService(IID_IAudioCaptureClient, captureClient.out());
        else result = client->GetService(IID_IAudioRenderClient, renderClient.out());
        if (FAILED(result)) return result;
        ComPtr<IAudioClock> clock;
        UINT64 clockFrequency{};
        if (SUCCEEDED(client->GetService(IID_IAudioClock, clock.out()))) clock->GetFrequency(&clockFrequency);

        const std::size_t chunkCapacity = std::max<std::size_t>(bufferFrames + 1024, 4096);
        std::vector<Frame> input(chunkCapacity);
        std::vector<Frame> output(chunkCapacity);
        AdaptiveResampler resampler(capture ? format.rate : sampleRate,
                                    capture ? sampleRate : format.rate,
                                    std::max<std::size_t>(32768, chunkCapacity * 4));
        DriftController drift;
        ClockEstimate clockEstimate;
        if (capture) queue->requestClear(); else queue->clear();
        state("Priming", "Preparing current live audio", &endpoint);
        {
            std::lock_guard lock(mutex);
            path.rate = format.rate; path.channels = format.channels;
            path.periodMs = static_cast<double>(periodFrames) * 1000 / format.rate;
            path.bufferMs = static_cast<double>(bufferFrames) * 1000 / format.rate;
        }
        targetQueueFrames.store(buffering.targetFrames(), std::memory_order_relaxed);
        if (!capture) {
            BYTE* data{};
            if (FAILED(result = renderClient->GetBuffer(bufferFrames, &data))) return result;
            if (FAILED(result = renderClient->ReleaseBuffer(bufferFrames, AUDCLNT_BUFFERFLAGS_SILENT))) return result;
        }
        if (FAILED(result = client->Start())) return result;
        DWORD taskIndex{};
        HANDLE task = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
        if (task) AvSetMmThreadPriority(task, AVRT_PRIORITY_HIGH);
        struct StreamCleanup {
            IAudioClient* client; HANDLE task;
            ~StreamCleanup() { client->Stop(); if (task) AvRevertMmThreadCharacteristics(task); }
        } cleanup{client.get(), task};

        { std::lock_guard lock(mutex); ++path.reconnects; }
        state("Running", exclusiveRender ? "Direct virtual microphone output (exclusive WASAPI)"
            : preferLowLatency ? "" : "Compatible audio timing after repeated interruptions", &endpoint);
        const HANDLE events[]{stopEvent.get(), controlEvent.get(), audioEvent.get()};
        auto lastEvent = std::chrono::steady_clock::now();
        const auto streamStart = lastEvent;
        StreamStability stability;
        const auto needsCompatibleTiming = [&](std::chrono::steady_clock::time_point now) {
            if (exclusiveRender || !preferLowLatency) return false;
            if (!stability.observe(std::chrono::duration<double>(now - streamStart).count())) return false;
            preferLowLatency = false;
            return true;
        };
        auto captureCheck = lastEvent + std::chrono::seconds(1);
        auto previousCaptureUnderruns = queue->underrunEvents();
        bool captureWarmed{};
        std::uint64_t fadeFrames{};
        const auto fadeLength = std::max<std::uint32_t>(1, format.rate / 100);
        bool renderPrimed{};
        bool renderUnderrun{};

        while (!stopped.load(std::memory_order_relaxed)) {
            const DWORD wait = WaitForMultipleObjects(3, events, FALSE, 250);
            if (wait == WAIT_OBJECT_0) return S_FALSE;
            if (wait == WAIT_FAILED) return HRESULT_FROM_WIN32(GetLastError());
            if (!stillSelected(endpoint, serial)) return S_FALSE;
            const auto now = std::chrono::steady_clock::now();
            if (wait != WAIT_OBJECT_0 + 2) {
                // Silent capture buffers are healthy and take the event path.
                // This watchdog catches a stopped hardware stream, not silence.
                if (now - lastEvent > std::chrono::seconds(2)) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                continue;
            }
            const double elapsed = std::clamp(std::chrono::duration<double>(now - lastEvent).count(), 0.0001, 0.25);
            if (now - lastEvent > std::chrono::seconds(1)) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
            lastEvent = now;

            if (capture) {
                double feedbackElapsed = elapsed;
                UINT32 packetFrames{};
                if (FAILED(result = captureClient->GetNextPacketSize(&packetFrames))) return result;
                while (packetFrames) {
                    BYTE* data{}; UINT32 count{}; DWORD flags{}; UINT64 position{}, qpc{};
                    if (FAILED(result = captureClient->GetBuffer(&data, &count, &flags, &position, &qpc))) return result;
                    if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) {
                        if (needsCompatibleTiming(now)) {
                            captureClient->ReleaseBuffer(count);
                            return S_FALSE;
                        }
                        resampler.reset(); drift.reset(); clockEstimate = {}; queue->requestClear();
                        fadeFrames = 0;
                        captureWarmed = false; captureCheck = now + std::chrono::seconds(1);
                    }
                    if (!(flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR)) clockEstimate.observe(position, qpc, format.rate);
                    std::size_t offset{};
                    while (offset < count) {
                        const auto frames = std::min<std::size_t>(input.size(), count - offset);
                        format.decode(data ? data + offset * format.blockAlign : nullptr,
                            std::span<Frame>(input.data(), frames), channel, flags & AUDCLNT_BUFFERFLAGS_SILENT);
                        for (std::size_t frame = 0; frame < frames; ++frame) {
                            const float fade = static_cast<float>(std::min<std::uint64_t>(++fadeFrames, fadeLength)) / fadeLength;
                            input[frame].left *= fade; input[frame].right *= fade;
                        }
                        const auto accepted = resampler.push(std::span<const Frame>(input.data(), frames));
                        if (accepted != frames) { resampler.reset(); queue->requestClear(); }
                        // Several packets can arrive at one late callback.
                        // Advance feedback time once, not once per packet or
                        // conversion chunk, so catch-up does not overcorrect.
                        const double correction = drift.update(static_cast<double>(queue->size()), buffering.targetFrames(),
                            std::exchange(feedbackElapsed, 0.0)) / clockEstimate.multiplier;
                        for (;;) {
                            const auto generated = resampler.produce(output, correction);
                            if (resampler.lastError()) { captureClient->ReleaseBuffer(count); return E_FAIL; }
                            if (!generated) break;
                            if (queue->push(std::span<const Frame>(output.data(), generated)) != generated)
                                queue->requestClear();
                            if (generated < output.size()) break;
                        }
                        offset += frames;
                    }
                    if (FAILED(result = captureClient->ReleaseBuffer(count))) return result;
                    if (stopped.load(std::memory_order_relaxed) ||
                        configurationSerial.load(std::memory_order_acquire) != serial) return S_FALSE;
                    if (FAILED(result = captureClient->GetNextPacketSize(&packetFrames))) return result;
                }
                // Ignore expected startup starvation. Sustained late delivery
                // earns a bounded extra block of jitter protection, without
                // changing effects, converter quality or a healthy device.
                if (now >= captureCheck) {
                    const auto underruns = queue->underrunEvents();
                    if (captureWarmed && underruns - previousCaptureUnderruns > 2) buffering.observeUnderrun();
                    previousCaptureUnderruns = underruns; captureWarmed = true;
                    captureCheck = now + std::chrono::seconds(1);
                }
            } else {
                UINT32 available{};
                if (FAILED(result = detail::renderFramesAvailable(client.get(), exclusiveRender, bufferFrames, &available))) return result;
                if (!available) continue;
                if (clock && clockFrequency) {
                    UINT64 position{}, qpc{};
                    if (SUCCEEDED(clock->GetPosition(&position, &qpc))) clockEstimate.observe(position, qpc, static_cast<double>(clockFrequency));
                }
                // Discard a stalled output's backlog instead of replaying old
                // speech. Each render worker owns this consumer cursor.
                if (queue->clearPending() || queue->size() > sampleRate / 5) {
                    queue->clear(); resampler.reset(); drift.reset(); fadeFrames = 0; renderPrimed = false;
                }
                const double buffered = queue->size() + resampler.pendingInputFrames();
                const auto demand = static_cast<std::uint32_t>((static_cast<std::uint64_t>(available) * sampleRate + format.rate - 1) / format.rate);
                if (!renderPrimed && buffered >= buffering.primeFrames(demand)) renderPrimed = true;
                const double correction = drift.update(buffered, buffering.targetFrames(), elapsed) * clockEstimate.multiplier;
                std::size_t filled{};
                if (renderPrimed) {
                    while (filled < available) {
                        const auto generated = resampler.produce(std::span<Frame>(output.data() + filled, available - filled), correction);
                        if (resampler.lastError()) return E_FAIL;
                        filled += generated;
                        if (filled >= available) break;
                        const auto queued = std::min<std::size_t>(queue->size(), input.size());
                        if (!queued) break;
                        const auto read = queue->pop(std::span<Frame>(input.data(), queued));
                        if (!read) break;
                        if (resampler.push(std::span<const Frame>(input.data(), read)) != read) {
                            resampler.reset(); fadeFrames = 0; break;
                        }
                    }
                }
                if (filled < available) {
                    std::fill(output.begin() + static_cast<std::ptrdiff_t>(filled), output.begin() + available, Frame{});
                    if (!renderUnderrun && renderPrimed) {
                        ++streamUnderruns; buffering.observeUnderrun();
                        if (needsCompatibleTiming(now)) return S_FALSE;
                    }
                    renderUnderrun = true;
                    // Re-prime after an underrun rather than alternate bursts
                    // of speech and silence on successive hardware callbacks.
                    renderPrimed = false; fadeFrames = 0;
                } else renderUnderrun = false;
                for (std::size_t frame = 0; frame < filled; ++frame) {
                    const float fade = static_cast<float>(std::min<std::uint64_t>(++fadeFrames, fadeLength)) / fadeLength;
                    output[frame].left *= fade; output[frame].right *= fade;
                }
                BYTE* data{};
                result = renderClient->GetBuffer(available, &data);
                if (exclusiveRender && result == AUDCLNT_E_BUFFER_ERROR) {
                    // Exclusive drivers can temporarily have no next buffer.
                    // Drop these current frames and retry the next event.
                    ++streamUnderruns; renderPrimed = false; fadeFrames = 0;
                    continue;
                }
                if (FAILED(result)) return result;
                format.encode(std::span<const Frame>(output.data(), available), data);
                if (FAILED(result = renderClient->ReleaseBuffer(available, filled ? 0 : AUDCLNT_BUFFERFLAGS_SILENT))) return result;
            }
            targetQueueFrames.store(buffering.targetFrames(), std::memory_order_relaxed);
            const auto pending = resampler.pendingInputFrames();
            pendingResamplerFrames.store(static_cast<std::uint32_t>(capture
                ? (static_cast<std::uint64_t>(pending) * sampleRate + format.rate - 1) / format.rate : pending),
                std::memory_order_relaxed);
        }
        return S_FALSE;
    }

    void run() {
        ComApartment apartment;
        std::size_t backoff{};
        constexpr std::array<DWORD, 4> delays{250, 1000, 2000, 5000};
        const HANDLE events[]{stopEvent.get(), controlEvent.get()};
        while (!stopped.load(std::memory_order_relaxed)) {
            DeviceBinding selection;
            std::uint64_t serial{};
            {
                std::lock_guard lock(mutex);
                selection = desired;
                serial = configurationSerial.load(std::memory_order_acquire);
            }
            const auto generation = catalog.generation();
            if (serial != observedConfiguration) preferLowLatency = true;
            if (serial != observedConfiguration || generation != observedGeneration) {
                backoff = 0; observedConfiguration = serial; observedGeneration = generation;
            }
            DWORD delay = 250;
            if (selection.id.empty() && selection.stableId.empty()) {
                state("Unassigned");
                // No endpoint notification can resolve an empty assignment.
                // Sleep until selection/retry/stop instead of waking ten times
                // per second for every unused input or output node.
                if (WaitForMultipleObjects(2, events, FALSE, INFINITE) == WAIT_OBJECT_0) break;
                continue;
            } else if (FAILED(apartment.result)) {
                state("Error", "Could not initialize Windows audio COM apartment"); delay = 5000;
            } else {
                state("Resolving", "Finding selected device");
                const auto endpoint = resolve(selection);
                if (!endpoint || endpoint->state != "Active") {
                    std::string message = "Waiting for device";
                    if (endpoint && endpoint->state == "Disabled") message = "Device disabled in Windows Sound settings";
                    state("Waiting", std::move(message), endpoint ? &*endpoint : nullptr);
                } else if (!requiredTransport.empty() && endpoint->transport != requiredTransport) {
                    std::string message = "Device does not match the required audio transport";
                    if (requiredTransport == "physical") message = "Select a physical device in this slot";
                    else if (requiredTransport == "vbCable") message = "Select the VB-CABLE receive endpoint in setup";
                    else if (requiredTransport == "vacLite") message = "Select the VAC Lite send endpoint in setup";
                    state("Error", std::move(message), &*endpoint);
                } else if (capture && microphone && endpoint->isVirtual) {
                    state("Error", "Select a physical microphone in this slot", &*endpoint);
                } else if (capture && !microphone && !endpoint->isVirtual) {
                    state("Error", "Select the virtual cable receive endpoint in setup", &*endpoint);
                } else {
                    state("Opening", exclusiveRender ? "Opening direct virtual microphone output" : "Opening shared-mode audio", &*endpoint);
                    HRESULT result;
                    try { result = openAndRun(*endpoint, selection, serial); }
                    catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; }
                    catch (const std::exception&) { result = E_FAIL; }
                    ready.store(false, std::memory_order_release);
                    if (capture) queue->requestClear(); else queue->clear();
                    if (stopped.load(std::memory_order_relaxed)) break;
                    if (result == S_FALSE) { backoff = 0; continue; }
                    state(failureState(result), failureMessage(result, exclusiveRender), &*endpoint);
                }
                delay = delays[std::min(backoff, delays.size() - 1)];
                if (backoff + 1 < delays.size()) ++backoff;
            }
            // Poll generation while waiting, so a fresh arrival breaks a long
            // backoff within 100 ms without catalog callbacks owning workers.
            DWORD waited{};
            while (waited < delay && !stopped.load(std::memory_order_relaxed)) {
                const DWORD step = std::min<DWORD>(100, delay - waited);
                const DWORD wait = WaitForMultipleObjects(2, events, FALSE, step);
                if (wait == WAIT_OBJECT_0) return;
                if (wait == WAIT_OBJECT_0 + 1 || catalog.generation() != observedGeneration) break;
                waited += step;
            }
        }
    }
};

AudioWorker::AudioWorker(bool capture, bool microphone, std::shared_ptr<FrameQueue> queue, DeviceCatalog& catalog, std::string requiredTransport)
    : impl_(std::make_unique<Impl>(capture, microphone, std::move(queue), catalog, std::move(requiredTransport))) {}
AudioWorker::~AudioWorker() = default;
void AudioWorker::configure(DeviceBinding binding) {
    {
        std::lock_guard lock(impl_->mutex);
        const auto& old = impl_->desired;
        if (old.id == binding.id && old.stableId == binding.stableId && old.name == binding.name && old.channel == binding.channel) return;
        impl_->desired = std::move(binding);
        impl_->configurationSerial.fetch_add(1, std::memory_order_release);
    }
    SetEvent(impl_->controlEvent.get());
}
void AudioWorker::retry() noexcept { impl_->configurationSerial.fetch_add(1, std::memory_order_release); SetEvent(impl_->controlEvent.get()); }
bool AudioWorker::ready() const noexcept { return impl_->ready.load(std::memory_order_acquire); }
std::uint32_t AudioWorker::periodFrames() const noexcept { return impl_->enginePeriodFrames.load(std::memory_order_relaxed); }
std::uint32_t AudioWorker::targetQueueFrames() const noexcept { return impl_->targetQueueFrames.load(std::memory_order_relaxed); }
PathState AudioWorker::status() const {
    std::lock_guard lock(impl_->mutex);
    auto state = impl_->path;
    state.queueMs = static_cast<double>(impl_->queue->size()) * 1000.0 / sampleRate;
    state.targetQueueMs = static_cast<double>(impl_->targetQueueFrames.load(std::memory_order_relaxed)) * 1000.0 / sampleRate;
    state.resamplerPendingMs = static_cast<double>(impl_->pendingResamplerFrames.load(std::memory_order_relaxed)) * 1000.0 / sampleRate;
    state.underruns = impl_->queue->underrunEvents() + impl_->streamUnderruns.load(std::memory_order_relaxed);
    state.overruns = impl_->queue->overrunEvents();
    return state;
}
void AudioWorker::stop() noexcept { impl_->stop(); }
}
