// Production exclusive-opening and buffer policy against fake COM clients.
// No real device activation, audio streams, personal settings or control pipe.
#include "am/windows_audio_detail.hpp"
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <ksmedia.h>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <stdexcept>

namespace {
int assertions{};
void check(bool value, const char* reason) { ++assertions; if (!value) throw std::runtime_error(reason); }
enum class Format { pcm16, pcm32Extensible, float32, mixMono24, paddedMix, unsupported };
struct State {
    Format accepted = Format::pcm16;
    HRESULT initializeResult = S_OK, periodResult = S_OK, paddingResult = S_OK;
    bool alignFirst{}, alignAlways{}, rejectReactivation{};
    UINT32 alignedFrames = 528, padding = 120;
    int activations{}, live{}, maximumLive{}, initialized{}, sharedInitializations{}, formatChecks{}, paddingChecks{}, starts{};
    REFERENCE_TIME lastDuration{}, lastPeriod{};
    WAVEFORMATEXTENSIBLE selected{};
};
class Client final : public IAudioClient {
public:
    explicit Client(State& state) : state_(state) {
        ++state_.live; state_.maximumLive = std::max(state_.maximumLive, state_.live);
    }
    ~Client() { --state_.live; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto left = --references_; if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE Initialize(AUDCLNT_SHAREMODE mode, DWORD flags, REFERENCE_TIME duration,
        REFERENCE_TIME period, const WAVEFORMATEX* format, LPCGUID) override {
        ++state_.initialized;
        if (mode == AUDCLNT_SHAREMODE_SHARED) ++state_.sharedInitializations;
        check(mode == AUDCLNT_SHAREMODE_EXCLUSIVE, "Virtual output must never initialize shared playback");
        check(flags == AUDCLNT_STREAMFLAGS_EVENTCALLBACK && duration > 0 && duration == period,
            "Exclusive output requires equal nonzero event-driven period and buffer duration");
        state_.lastDuration = duration; state_.lastPeriod = period;
        std::memcpy(&state_.selected, format, sizeof(WAVEFORMATEX) + format->cbSize);
        if (state_.alignAlways || (state_.alignFirst && state_.initialized == 1)) return AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED;
        return state_.initializeResult;
    }
    HRESULT STDMETHODCALLTYPE GetBufferSize(UINT32* frames) override { *frames = state_.alignedFrames; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetStreamLatency(REFERENCE_TIME*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetCurrentPadding(UINT32* frames) override {
        ++state_.paddingChecks; *frames = state_.padding; return state_.paddingResult;
    }
    HRESULT STDMETHODCALLTYPE IsFormatSupported(AUDCLNT_SHAREMODE mode, const WAVEFORMATEX* format,
        WAVEFORMATEX** closest) override {
        ++state_.formatChecks;
        check(mode == AUDCLNT_SHAREMODE_EXCLUSIVE && !closest, "Negotiate exact exclusive formats without shared closest-match conversion");
        bool match{};
        switch (state_.accepted) {
        case Format::pcm16:
            match = format->nSamplesPerSec == 48000 && format->nChannels == 2 && format->wFormatTag == WAVE_FORMAT_PCM && format->wBitsPerSample == 16; break;
        case Format::pcm32Extensible:
            match = format->nSamplesPerSec == 48000 && format->nChannels == 2 && format->wFormatTag == WAVE_FORMAT_EXTENSIBLE && format->wBitsPerSample == 32
                && reinterpret_cast<const WAVEFORMATEXTENSIBLE*>(format)->SubFormat.Data1 == WAVE_FORMAT_PCM; break;
        case Format::float32:
            match = format->nSamplesPerSec == 48000 && format->nChannels == 2 && format->wFormatTag == WAVE_FORMAT_IEEE_FLOAT && format->wBitsPerSample == 32; break;
        case Format::mixMono24:
            match = format->nSamplesPerSec == 32000 && format->nChannels == 1 && format->wBitsPerSample == 24; break;
        case Format::paddedMix:
            match = format->nSamplesPerSec == 32000 && format->nChannels == 1 && format->wBitsPerSample == 32; break;
        case Format::unsupported: break;
        }
        return match ? S_OK : AUDCLNT_E_UNSUPPORTED_FORMAT;
    }
    HRESULT STDMETHODCALLTYPE GetMixFormat(WAVEFORMATEX** format) override {
        auto* result = static_cast<WAVEFORMATEXTENSIBLE*>(CoTaskMemAlloc(sizeof(WAVEFORMATEXTENSIBLE)));
        if (!result) return E_OUTOFMEMORY;
        *result = {};
        const WORD bits = state_.accepted == Format::paddedMix ? 32 : 24;
        result->Format = {WAVE_FORMAT_EXTENSIBLE, 1, 32000, DWORD(32000 * bits / 8), WORD(bits / 8), bits, 22};
        result->Samples.wValidBitsPerSample = 24;
        result->dwChannelMask = SPEAKER_FRONT_CENTER;
        result->SubFormat = {WAVE_FORMAT_PCM, 0, 0x0010, {0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71}};
        *format = &result->Format; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetDevicePeriod(REFERENCE_TIME* normal, REFERENCE_TIME* minimum) override {
        *normal = 100000; *minimum = 30000; return state_.periodResult;
    }
    HRESULT STDMETHODCALLTYPE Start() override { ++state_.starts; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Stop() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Reset() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetEventHandle(HANDLE) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetService(REFIID, void** result) override { *result = nullptr; return E_NOTIMPL; }
private:
    State& state_;
    ULONG references_ = 1;
};
class Device final : public IMMDevice {
public:
    explicit Device(State& state) : state_(state) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE Activate(REFIID iid, DWORD, PROPVARIANT*, void** result) override {
        *result = nullptr; ++state_.activations;
        check(IsEqualIID(iid, __uuidof(IAudioClient)), "Only the selected fake endpoint's audio client is activated");
        check(state_.live == 0, "A failed audio client must be released before its replacement is activated");
        if (state_.rejectReactivation && state_.activations > 1) return E_ACCESSDENIED;
        *result = static_cast<IAudioClient*>(new Client(state_)); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OpenPropertyStore(DWORD, IPropertyStore** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetId(LPWSTR* result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetState(DWORD*) override { return E_NOTIMPL; }
private:
    State& state_;
};
void open(State& state, HRESULT expected) {
    Device device(state); IAudioClient* client{}; WAVEFORMATEX* format{};
    const auto result = am::detail::initializeExclusiveRender(&device, &client, &format);
    check(result == expected, "Exclusive initialization must preserve its exact success or failure");
    if (expected == S_OK) {
        check(client && format && state.live == 1, "Successful open returns one owned client and chosen format");
        check(format->nSamplesPerSec == state.selected.Format.nSamplesPerSec
            && format->wBitsPerSample == state.selected.Format.wBitsPerSample, "Returned format matches initialized renderer");
    } else check(!client && !format && state.live == 0, "Failed open leaves no client, format or partial stream");
    if (client) client->Release();
    CoTaskMemFree(format);
    check(state.live == 0 && state.maximumLive == 1, "Negotiation and alignment retry do not leak COM clients");
    check(state.sharedInitializations == 0 && state.starts == 0, "No failure silently falls back to shared playback or starts a stream");
}
}
int main() {
    try {
        for (const auto accepted : {Format::pcm16, Format::pcm32Extensible, Format::float32, Format::mixMono24}) {
            State state; state.accepted = accepted; open(state, S_OK);
            check(state.initialized == 1 && state.activations == 1, "Supported format opens once");
            check(state.lastPeriod == 100000, "Initial event period uses the device's safe default period");
            if (accepted == Format::pcm16) check(state.formatChecks == 1, "Native stereo PCM16 is preferred");
            if (accepted == Format::mixMono24) check(state.selected.Format.nChannels == 1 && state.selected.Format.nSamplesPerSec == 32000,
                "Safe supported mix format is available when standard stereo candidates fail");
        }
        State aligned; aligned.alignFirst = true; open(aligned, S_OK);
        check(aligned.activations == 2 && aligned.initialized == 2 && aligned.lastPeriod == 110000,
            "Alignment retry reactivates once and derives equal 11 ms duration/period from 528 frames at 48 kHz");
        State repeated; repeated.alignAlways = true; open(repeated, AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED);
        check(repeated.activations == 2 && repeated.initialized == 2, "Repeated alignment failures are bounded without shared fallback");
        State noFrames; noFrames.alignFirst = true; noFrames.alignedFrames = 0; open(noFrames, AUDCLNT_E_BUFFER_SIZE_ERROR);
        State reactivate; reactivate.alignFirst = true; reactivate.rejectReactivation = true; open(reactivate, E_ACCESSDENIED);
        for (const auto error : {AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED, AUDCLNT_E_DEVICE_IN_USE, AUDCLNT_E_UNSUPPORTED_FORMAT, E_ACCESSDENIED}) {
            State state; state.initializeResult = error; open(state, error);
            check(state.activations == 1 && state.initialized == 1, "Busy, disabled and unsupported direct output must remain closed");
        }
        for (const auto accepted : {Format::paddedMix, Format::unsupported}) {
            State state; state.accepted = accepted; open(state, AUDCLNT_E_UNSUPPORTED_FORMAT);
            check(state.initialized == 0, "Unsupported and partial-valid-bit formats must never initialize a renderer");
        }
        State badPeriod; badPeriod.periodResult = E_FAIL; open(badPeriod, E_FAIL);
        check(badPeriod.initialized == 0, "Period query failure cannot open an unknown buffer configuration");
        State buffer; auto* client = new Client(buffer); UINT32 available{};
        buffer.paddingResult = E_FAIL;
        check(am::detail::renderFramesAvailable(client, true, 528, &available) == S_OK && available == 528 && buffer.paddingChecks == 0,
            "Exclusive events service the entire buffer without querying unsupported shared padding");
        buffer.paddingResult = S_OK;
        check(am::detail::renderFramesAvailable(client, false, 480, &available) == S_OK && available == 360 && buffer.paddingChecks == 1,
            "Shared physical playback retains its independent padding calculation");
        buffer.padding = 481;
        check(am::detail::renderFramesAvailable(client, false, 480, &available) == AUDCLNT_E_BUFFER_ERROR && available == 0,
            "Invalid shared padding fails without underflow");
        client->Release();
        std::cout << "PASS: " << assertions << " offline exclusive-render assertions; format negotiation, valid-bit safety, aligned reactivation, fail-closed errors and full-buffer event policy. No real audio devices were opened.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL after " << assertions << " assertions: " << error.what() << '\n'; return 1; }
}
