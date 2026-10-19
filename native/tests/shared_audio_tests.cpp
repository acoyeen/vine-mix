// Exercises production shared-mode opening with fake COM clients only.
// No endpoint enumeration, real streams, control pipe or personal settings.
#include "am/windows_audio_detail.hpp"
#include "am/stream_stability.hpp"
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>

namespace {
int assertions{};
void check(bool value, const char* reason) { ++assertions; if (!value) throw std::runtime_error(reason); }

struct State {
    bool capture{}, client2 = true, client3 = true;
    DWORD rate = 48000;
    WORD channels = 2;
    UINT32 defaultPeriod = 480, fundamental = 48, minimum = 96, maximum = 480;
    REFERENCE_TIME legacyPeriod = 100000;
    HRESULT periodResult = S_OK, devicePeriodResult = S_OK, mixResult = S_OK;
    HRESULT legacyResult = S_OK, propertyResult = S_OK;
    int activationFailure{};
    HRESULT activationResult = E_ACCESSDENIED;
    std::vector<HRESULT> modernResults;
    std::vector<UINT32> requestedPeriods;
    std::vector<int> modernActivations;
    int activations{}, live{}, maximumLive{}, mixQueries{}, properties{}, legacyInitializations{}, starts{};
    int legacyActivation{};
    WAVEFORMATEX format() const {
        const auto align = static_cast<WORD>(channels * sizeof(float));
        return {WAVE_FORMAT_IEEE_FLOAT, channels, rate, rate * align, align, 32, 0};
    }
    void checkFormat(const WAVEFORMATEX* actual) const {
        const auto expected = format();
        check(actual && actual->wFormatTag == expected.wFormatTag && actual->nChannels == expected.nChannels
            && actual->nSamplesPerSec == expected.nSamplesPerSec && actual->nAvgBytesPerSec == expected.nAvgBytesPerSec
            && actual->nBlockAlign == expected.nBlockAlign && actual->wBitsPerSample == expected.wBitsPerSample
            && actual->cbSize == expected.cbSize, "Latency negotiation must preserve the complete device mix format");
    }
};

class Client final : public IAudioClient3 {
public:
    explicit Client(State& state) : state_(state), activation_(state.activations) {
        ++state_.live; state_.maximumLive = std::max(state_.maximumLive, state_.live);
    }
    ~Client() { --state_.live; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        *result = nullptr;
        if (IsEqualIID(iid, __uuidof(IAudioClient3)) && state_.client3) *result = static_cast<IAudioClient3*>(this);
        else if (IsEqualIID(iid, __uuidof(IAudioClient2)) && state_.client2) *result = static_cast<IAudioClient2*>(this);
        else if (IsEqualIID(iid, __uuidof(IAudioClient))) *result = static_cast<IAudioClient*>(this);
        else if (IsEqualIID(iid, __uuidof(IUnknown))) *result = static_cast<IUnknown*>(this);
        if (!*result) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto left = --references_; if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE Initialize(AUDCLNT_SHAREMODE mode, DWORD flags, REFERENCE_TIME duration,
        REFERENCE_TIME period, const WAVEFORMATEX* format, LPCGUID session) override {
        check(!attempted_, "Each initialization attempt requires a fresh audio client after an earlier failure");
        attempted_ = true;
        check(mode == AUDCLNT_SHAREMODE_SHARED, "Physical capture/playback must retain shared mode");
        check(flags == (AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_NOPERSIST)
            && !duration && !period && !session, "Legacy fallback must retain the documented event-driven shared initialization");
        state_.checkFormat(format);
        ++state_.legacyInitializations; state_.legacyActivation = activation_;
        return state_.legacyResult;
    }
    HRESULT STDMETHODCALLTYPE GetBufferSize(UINT32* frames) override { *frames = 960; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetStreamLatency(REFERENCE_TIME*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetCurrentPadding(UINT32*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE IsFormatSupported(AUDCLNT_SHAREMODE, const WAVEFORMATEX*, WAVEFORMATEX**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetMixFormat(WAVEFORMATEX** format) override {
        ++state_.mixQueries; *format = nullptr;
        if (FAILED(state_.mixResult)) return state_.mixResult;
        auto* allocated = static_cast<WAVEFORMATEX*>(CoTaskMemAlloc(sizeof(WAVEFORMATEX)));
        if (!allocated) return E_OUTOFMEMORY;
        *allocated = state_.format(); *format = allocated; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetDevicePeriod(REFERENCE_TIME* normal, REFERENCE_TIME* minimum) override {
        if (normal) *normal = state_.legacyPeriod;
        if (minimum) *minimum = 30000;
        return state_.devicePeriodResult;
    }
    HRESULT STDMETHODCALLTYPE Start() override { ++state_.starts; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Stop() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Reset() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetEventHandle(HANDLE) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetService(REFIID, void** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE IsOffloadCapable(AUDIO_STREAM_CATEGORY, BOOL*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetClientProperties(const AudioClientProperties* properties) override {
        check(!attempted_, "Client properties must precede stream initialization");
        check(properties && properties->cbSize == sizeof(AudioClientProperties) && !properties->bIsOffload
            && properties->Options == AUDCLNT_STREAMOPTIONS_NONE, "Low-latency opening must preserve non-offloaded processing properties");
        check(properties->eCategory == (state_.capture ? AudioCategory_Other : AudioCategory_Media),
            "Capture and playback must retain their original audio processing categories");
        ++state_.properties; configured_ = true; return state_.propertyResult;
    }
    HRESULT STDMETHODCALLTYPE GetBufferSizeLimits(const WAVEFORMATEX*, BOOL, REFERENCE_TIME*, REFERENCE_TIME*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetSharedModeEnginePeriod(const WAVEFORMATEX* format, UINT32* normal,
        UINT32* fundamental, UINT32* minimum, UINT32* maximum) override {
        check(!state_.client2 || configured_, "Client properties must precede querying the supported engine periods");
        state_.checkFormat(format);
        *normal = state_.defaultPeriod; *fundamental = state_.fundamental;
        *minimum = state_.minimum; *maximum = state_.maximum;
        return state_.periodResult;
    }
    HRESULT STDMETHODCALLTYPE GetCurrentSharedModeEnginePeriod(WAVEFORMATEX** format, UINT32* period) override {
        *format = nullptr; *period = 0; return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE InitializeSharedAudioStream(DWORD flags, UINT32 period,
        const WAVEFORMATEX* format, LPCGUID session) override {
        check(!attempted_, "Failed Client3 initialization must never be retried on the same COM client");
        attempted_ = true;
        check(flags == AUDCLNT_STREAMFLAGS_EVENTCALLBACK && !session,
            "Client3 opening must remain event-driven without unsupported legacy flags");
        state_.checkFormat(format);
        check(state_.fundamental && period % state_.fundamental == 0 && period >= state_.minimum && period <= state_.maximum,
            "Every requested engine period must be a legal fundamental multiple inside the supported range");
        const auto attempt = state_.requestedPeriods.size();
        state_.requestedPeriods.push_back(period); state_.modernActivations.push_back(activation_);
        return attempt < state_.modernResults.size() ? state_.modernResults[attempt] : S_OK;
    }
private:
    State& state_;
    int activation_;
    ULONG references_ = 1;
    bool attempted_{}, configured_{};
};

class Device final : public IMMDevice {
public:
    explicit Device(State& state) : state_(state) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE Activate(REFIID iid, DWORD, PROPVARIANT*, void** result) override {
        *result = nullptr; ++state_.activations;
        check(IsEqualIID(iid, __uuidof(IAudioClient)), "Only the selected fake device's audio client may be activated");
        check(state_.live == 0, "All interfaces on a rejected client must be released before activating its replacement");
        if (state_.activations == state_.activationFailure) return state_.activationResult;
        *result = static_cast<IAudioClient*>(new Client(state_)); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OpenPropertyStore(DWORD, IPropertyStore** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetId(LPWSTR* result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetState(DWORD*) override { return E_NOTIMPL; }
private:
    State& state_;
};

void open(State& state, HRESULT expected, UINT32 expectedPeriod = 0, bool preferLowLatency = true) {
    Device device(state);
    IAudioClient* client{}; WAVEFORMATEX* format{}; UINT32 period = 123456;
    const auto result = am::detail::initializeSharedStream(&device, state.capture, &client, &format, &period, preferLowLatency);
    check(result == expected, "Shared opening must preserve the exact success or terminal failure result");
    if (SUCCEEDED(expected)) {
        check(client && format && state.live == 1, "A successful open returns one owned client and mix format");
        state.checkFormat(format);
        check(period == expectedPeriod, "Reported period must match the accepted Client3 period or measured legacy device period");
    } else check(!client && !format && !period && !state.live,
        "Failed shared opening must leave null client/format, zero period and no COM references");
    if (client) client->Release();
    CoTaskMemFree(format);
    check(!state.live && state.maximumLive <= 1, "Shared opening and bounded fallbacks must not leak or overlap COM clients");
    check(state.activations <= 3 && !state.starts, "Opening must be bounded and must never start an audio stream");
}

void lowLatencyAndFormatTests() {
    for (const auto rate : {44100u, 48000u, 96000u}) {
        for (const bool capture : {false, true}) {
            State state; state.rate = rate; state.capture = capture; state.channels = capture ? 1 : 2;
            open(state, S_OK, 96);
            check(state.requestedPeriods == std::vector<UINT32>{96} && state.activations == 1
                && state.properties == 1 && !state.legacyInitializations, "Supported low periods should open immediately for microphone and headphones");
        }
    }
    State rounded; rounded.minimum = 97;
    open(rounded, S_OK, 144);
    check(rounded.requestedPeriods == std::vector<UINT32>{144}, "Minimum period must round upward to a supported fundamental multiple");

    State onlyDefault; onlyDefault.minimum = onlyDefault.defaultPeriod;
    open(onlyDefault, S_OK, 480);
    check(onlyDefault.requestedPeriods == std::vector<UINT32>{480}, "A device with no shorter supported period retains its default");

    State propertyFailure; propertyFailure.propertyResult = E_NOTIMPL;
    open(propertyFailure, S_OK, 96);
    check(propertyFailure.requestedPeriods == std::vector<UINT32>{96}, "Optional property failure must not prevent ordinary supported shared streams");
}

void fallbackTests() {
    for (const auto error : {AUDCLNT_E_INVALID_DEVICE_PERIOD, AUDCLNT_E_ENGINE_PERIODICITY_LOCKED,
            AUDCLNT_E_CPUUSAGE_EXCEEDED, E_FAIL}) {
        State state; state.modernResults = {error, S_OK};
        open(state, S_OK, 480);
        check(state.requestedPeriods == std::vector<UINT32>{96, 480}
            && state.modernActivations == std::vector<int>{1, 2} && state.properties == 2
            && !state.legacyInitializations, "Rejected low periods must fall back to the valid default on a freshly configured client");
    }
    State legacy; legacy.modernResults = {E_FAIL, E_FAIL};
    open(legacy, S_OK, 480);
    check(legacy.requestedPeriods == std::vector<UINT32>{96, 480} && legacy.legacyInitializations == 1
        && legacy.legacyActivation == 3 && legacy.properties == 3, "Rejected modern periods must make one final fresh legacy shared attempt");

    State duplicate; duplicate.minimum = duplicate.defaultPeriod; duplicate.modernResults = {E_FAIL};
    open(duplicate, S_OK, 480);
    check(duplicate.requestedPeriods == std::vector<UINT32>{480} && duplicate.activations == 2
        && duplicate.legacyActivation == 2, "A rejected minimum equal to default must not repeat the identical Client3 request");

    for (const auto badDefault : {0u, 95u, 100u, 528u}) {
        State state; state.defaultPeriod = badDefault; state.modernResults = {E_FAIL};
        open(state, S_OK, 480);
        check(state.requestedPeriods == std::vector<UINT32>{96} && state.activations == 2
            && state.legacyActivation == 2, "An invalid default period must be skipped after the valid low period fails");
    }

    State absent; absent.client3 = false;
    open(absent, S_OK, 480);
    check(absent.activations == 1 && absent.requestedPeriods.empty() && absent.legacyActivation == 1,
        "A device without Client3 must use its untouched initial client for legacy shared mode");
    State oldClient; oldClient.client2 = oldClient.client3 = false;
    open(oldClient, S_OK, 480);
    check(!oldClient.properties && oldClient.activations == 1, "Legacy clients without optional properties must remain supported");
    State queryFailure; queryFailure.periodResult = E_FAIL;
    open(queryFailure, S_OK, 480);
    check(queryFailure.requestedPeriods.empty() && queryFailure.activations == 1 && queryFailure.legacyActivation == 1,
        "A failed period query must not attempt an unknown modern period or unnecessarily replace an uninitialized client");
}

void malformedAndLegacyPeriodTests() {
    std::vector<State> malformed(5);
    malformed[0].fundamental = 0;
    malformed[1].minimum = 0;
    malformed[2].maximum = 0;
    malformed[3].minimum = 481;
    malformed[4].minimum = 470; malformed[4].maximum = 479;
    for (auto& state : malformed) {
        open(state, S_OK, 480);
        check(state.requestedPeriods.empty() && state.activations == 1,
            "Malformed supported period ranges must safely fall back without issuing illegal Client3 requests");
    }
    State overflow; overflow.minimum = std::numeric_limits<UINT32>::max() - 1;
    overflow.maximum = std::numeric_limits<UINT32>::max(); overflow.fundamental = 4;
    open(overflow, S_OK, 480);
    check(overflow.requestedPeriods.empty(), "Rounding a driver period must never wrap unsigned frame counts");

    State fractional; fractional.client3 = false; fractional.rate = 44100; fractional.legacyPeriod = 100001;
    open(fractional, S_OK, 442);
    State unknown; unknown.client3 = false; unknown.devicePeriodResult = E_FAIL;
    open(unknown, S_OK, 0);
    State noPeriod; noPeriod.client3 = false; noPeriod.legacyPeriod = 0;
    open(noPeriod, S_OK, 0);
    State negative; negative.client3 = false; negative.legacyPeriod = -100000;
    open(negative, S_OK, 0);
}

void terminalFailureTests() {
    State firstActivation; firstActivation.activationFailure = 1;
    open(firstActivation, E_ACCESSDENIED);
    State retryActivation; retryActivation.activationFailure = 2; retryActivation.modernResults = {E_FAIL};
    open(retryActivation, E_ACCESSDENIED);
    check(retryActivation.activations == 2, "Failed reactivation must remain terminal rather than looping");
    State legacyActivation; legacyActivation.activationFailure = 3; legacyActivation.modernResults = {E_FAIL, E_FAIL};
    open(legacyActivation, E_ACCESSDENIED);
    State mix; mix.mixResult = AUDCLNT_E_DEVICE_INVALIDATED;
    open(mix, AUDCLNT_E_DEVICE_INVALIDATED);
    check(mix.requestedPeriods.empty() && !mix.legacyInitializations, "Unavailable mix format must fail before any stream initialization");
    for (const auto error : {AUDCLNT_E_DEVICE_INVALIDATED, AUDCLNT_E_DEVICE_IN_USE, E_ACCESSDENIED}) {
        State state; state.modernResults = {E_FAIL, E_FAIL}; state.legacyResult = error;
        open(state, error);
        check(state.activations == 3 && state.legacyInitializations == 1, "Terminal legacy failures must propagate after bounded modern fallbacks");
    }
}

void conservativePeriodTests() {
    for (const bool capture : {false, true}) {
        State state; state.capture = capture;
        open(state, S_OK, 480, false);
        check(state.requestedPeriods == std::vector<UINT32>{480} && state.activations == 1
            && !state.legacyInitializations, "A stream downgraded after runtime glitches must skip the shortest period on reopen");
    }
    State rejected; rejected.modernResults = {AUDCLNT_E_CPUUSAGE_EXCEEDED};
    open(rejected, S_OK, 480, false);
    check(rejected.requestedPeriods == std::vector<UINT32>{480} && rejected.activations == 2
        && rejected.legacyActivation == 2, "A rejected conservative Client3 period must make one fresh legacy attempt");
    State invalid; invalid.defaultPeriod = 100;
    open(invalid, S_OK, 480, false);
    check(invalid.requestedPeriods.empty() && invalid.activations == 1 && invalid.legacyActivation == 1,
        "Malformed default periods must not reintroduce low-period requests in conservative mode");
    State absent; absent.client3 = false;
    open(absent, S_OK, 480, false);
    check(absent.requestedPeriods.empty() && absent.activations == 1,
        "Conservative mode must retain compatibility with legacy shared clients");
    State failed; failed.modernResults = {E_FAIL}; failed.legacyResult = AUDCLNT_E_DEVICE_IN_USE;
    open(failed, AUDCLNT_E_DEVICE_IN_USE, 0, false);
    check(failed.activations == 2, "Conservative fallback must remain bounded and propagate a terminal legacy failure");
}

void stabilityTests() {
    am::StreamStability warmed;
    for (const auto time : {0.0, .1, .5, .999})
        check(!warmed.observe(time), "Startup glitches must not downgrade a stream");
    check(!warmed.observe(1.0) && !warmed.observe(1.1) && warmed.observe(1.2),
        "Exactly three post-startup glitches inside ten seconds must request a conservative reopen");
    check(warmed.observe(1000) && warmed.observe(1001), "A requested downgrade stays latched for its stream lifetime");

    am::StreamStability expired;
    check(!expired.observe(1) && !expired.observe(2) && !expired.observe(12.001),
        "Old glitches outside the ten-second window must expire");
    check(!expired.observe(13) && expired.observe(14), "Three recent glitches still downgrade after old incidents expire");

    am::StreamStability sliding;
    check(!sliding.observe(1) && !sliding.observe(10) && !sliding.observe(11.1) && sliding.observe(11.2),
        "The window must slide with incidents rather than lose clustered glitches at a fixed window boundary");
    am::StreamStability boundary;
    check(!boundary.observe(1) && !boundary.observe(5) && boundary.observe(11),
        "Three glitches spanning exactly ten seconds belong to the same running window");

    am::StreamStability invalid;
    check(!invalid.observe(std::numeric_limits<double>::quiet_NaN())
        && !invalid.observe(std::numeric_limits<double>::infinity()) && !invalid.observe(-1),
        "Invalid elapsed times must not count as runtime glitches");
    check(!invalid.observe(2) && !invalid.observe(3) && invalid.observe(4),
        "Ignoring invalid elapsed times must not damage later observation");
    am::StreamStability resetClock;
    check(!resetClock.observe(20) && !resetClock.observe(21) && !resetClock.observe(2)
        && !resetClock.observe(3) && resetClock.observe(4), "A reset elapsed clock must start a fresh incident history");
}
}

int main() {
    try {
        lowLatencyAndFormatTests(); fallbackTests(); malformedAndLegacyPeriodTests(); terminalFailureTests();
        conservativePeriodTests(); stabilityTests();
        std::cout << "PASS: " << assertions << " offline shared-audio assertions; shortest legal periods, unchanged formats/categories, fresh default/legacy fallback, runtime stability downgrade, malformed drivers and ownership. No real audio devices were opened.\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL after " << assertions << " assertions: " << error.what() << '\n'; return 1;
    }
}
