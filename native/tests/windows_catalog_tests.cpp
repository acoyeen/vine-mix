// The production collector is exercised with fake COM objects only. This test
// never constructs DeviceCatalog/AudioWorker or opens any real audio endpoint.
#include "am/windows_audio_detail.hpp"
#include "am/configuration.hpp"
#include <windows.h>
#include <mmdeviceapi.h>
#include <propsys.h>
#include <propvarutil.h>
#include <atomic>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <string>
#include <utility>

namespace {
enum class Failure { none, enumerateCapture, enumerateRender, count, item, identifier, state };
std::atomic<int> liveObjects{};
std::size_t checks{};
void require(bool condition, const char* message) {
    ++checks;
    if (!condition) throw std::runtime_error(message);
}

constexpr PROPERTYKEY friendlyNameKey{{0xa45c254e,0xdf1c,0x4efd,{0x80,0x20,0x67,0xd1,0x46,0xa8,0x50,0xe0}},14};
constexpr PROPERTYKEY descriptionKey{{0xa45c254e,0xdf1c,0x4efd,{0x80,0x20,0x67,0xd1,0x46,0xa8,0x50,0xe0}},2};
constexpr PROPERTYKEY interfaceNameKey{{0x026e516e,0xb814,0x414b,{0x83,0xcd,0x85,0x6d,0x6f,0xef,0x48,0x22}},2};
struct Metadata {
    bool available{};
    std::wstring name, description, interfaceName;
    bool hasFormFactor{}, failFormFactor{};
    VARTYPE formFactorType = VT_UI4;
    ULONG formFactor{};
};

class PropertyStore final : public IPropertyStore {
public:
    explicit PropertyStore(Metadata metadata) : metadata_(std::move(metadata)) { ++liveObjects; }
    ~PropertyStore() { --liveObjects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto left = --references_; if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE GetCount(DWORD*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetAt(DWORD, PROPERTYKEY*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetValue(REFPROPERTYKEY key, PROPVARIANT* result) override {
        PropVariantInit(result);
        if (IsEqualPropertyKey(key, PKEY_AudioEndpoint_FormFactor)) {
            if (metadata_.failFormFactor) return E_FAIL;
            if (metadata_.hasFormFactor) { result->vt = metadata_.formFactorType; result->ulVal = metadata_.formFactor; }
            return S_OK;
        }
        const auto* text = IsEqualPropertyKey(key, friendlyNameKey) ? &metadata_.name :
            IsEqualPropertyKey(key, descriptionKey) ? &metadata_.description :
            IsEqualPropertyKey(key, interfaceNameKey) ? &metadata_.interfaceName : nullptr;
        if (text && !text->empty()) {
            result->pwszVal = static_cast<LPWSTR>(CoTaskMemAlloc((text->size() + 1) * sizeof(wchar_t)));
            if (!result->pwszVal) return E_OUTOFMEMORY;
            std::memcpy(result->pwszVal, text->c_str(), (text->size() + 1) * sizeof(wchar_t));
            result->vt = VT_LPWSTR;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetValue(REFPROPERTYKEY, REFPROPVARIANT) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Commit() override { return E_NOTIMPL; }
private:
    std::atomic<ULONG> references_{1};
    Metadata metadata_;
};

class Device final : public IMMDevice {
public:
    Device(EDataFlow direction, Failure failure, DWORD state, Metadata metadata)
        : direction_(direction), failure_(failure), state_(state), metadata_(std::move(metadata)) { ++liveObjects; }
    ~Device() { --liveObjects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto left = --references_; if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE Activate(REFIID, DWORD, PROPVARIANT*, void** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE OpenPropertyStore(DWORD, IPropertyStore** result) override {
        *result = nullptr;
        if (!metadata_.available) return E_NOTIMPL;
        *result = new PropertyStore(metadata_); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetId(LPWSTR* result) override {
        *result = nullptr;
        if (failure_ == Failure::identifier) return E_FAIL;
        const std::wstring id = direction_ == eCapture ? L"fake-capture" : L"fake-render";
        *result = static_cast<LPWSTR>(CoTaskMemAlloc((id.size() + 1) * sizeof(wchar_t)));
        if (!*result) return E_OUTOFMEMORY;
        std::memcpy(*result, id.c_str(), (id.size() + 1) * sizeof(wchar_t));
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetState(DWORD* result) override {
        if (failure_ == Failure::state) return E_FAIL;
        *result = state_;
        return S_OK;
    }
private:
    std::atomic<ULONG> references_{1};
    EDataFlow direction_;
    Failure failure_;
    DWORD state_;
    Metadata metadata_;
};

class Collection final : public IMMDeviceCollection {
public:
    Collection(EDataFlow direction, Failure failure, bool empty, DWORD state, Metadata metadata)
        : direction_(direction), failure_(failure), empty_(empty), state_(state), metadata_(std::move(metadata)) { ++liveObjects; }
    ~Collection() { --liveObjects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto left = --references_; if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE GetCount(UINT* result) override {
        if (failure_ == Failure::count) return E_FAIL;
        *result = empty_ ? 0 : 1; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Item(UINT index, IMMDevice** result) override {
        *result = nullptr;
        if (failure_ == Failure::item || index != 0 || empty_) return E_FAIL;
        *result = new Device(direction_, failure_, state_, metadata_); return S_OK;
    }
private:
    std::atomic<ULONG> references_{1};
    EDataFlow direction_;
    Failure failure_;
    bool empty_;
    DWORD state_;
    Metadata metadata_;
};

class Enumerator final : public IMMDeviceEnumerator {
public:
    Failure failure{};
    bool emptyCapture{}, emptyRender{};
    DWORD state = DEVICE_STATE_DISABLED;
    Metadata captureMetadata, renderMetadata;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void** result) override { *result = nullptr; return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE EnumAudioEndpoints(EDataFlow direction, DWORD mask, IMMDeviceCollection** result) override {
        *result = nullptr;
        if (mask != DEVICE_STATEMASK_ALL ||
            (failure == Failure::enumerateCapture && direction == eCapture) ||
            (failure == Failure::enumerateRender && direction == eRender)) return E_FAIL;
        // Fail after the first direction was collected as well as before it,
        // proving that the partial capture list is not published on failure.
        const auto localFailure = direction == eRender ? failure : Failure::none;
        *result = new Collection(direction, localFailure, direction == eCapture ? emptyCapture : emptyRender,
            state, direction == eCapture ? captureMetadata : renderMetadata);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetDefaultAudioEndpoint(EDataFlow, ERole, IMMDevice** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetDevice(LPCWSTR, IMMDevice** result) override { *result = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RegisterEndpointNotificationCallback(IMMNotificationClient*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE UnregisterEndpointNotificationCallback(IMMNotificationClient*) override { return E_NOTIMPL; }
};
}

int main() {
    try {
        Enumerator enumerator;
        auto complete = am::detail::collectEndpoints(&enumerator);
        require(complete && complete->size() == 2, "A successful inventory contains both directions");
        require((*complete)[0].id == "fake-capture" && (*complete)[1].id == "fake-render", "Opaque endpoint identities are preserved");
        require((*complete)[0].state == "Disabled" && (*complete)[1].state == "Disabled", "Successfully observed disabled state is retained");
        require((*complete)[0].name == "fake-capture", "Unavailable optional labels fall back to exact identity");
        require((*complete)[0].formFactor == -1 && (*complete)[0].description.empty() && (*complete)[0].interfaceName.empty(), "Unavailable optional classification metadata remains unknown");
        require(liveObjects == 0, "Successful collection releases all temporary COM objects");
        auto known = *complete;
        known[0].name = "Known cable"; known[0].stableId = "stable-capture";
        known[0].rate = 48000; known[0].channels = 2;
        known[0].isVirtual = true; known[0].transport = "vbCable";
        known[0].formFactor = LineLevel; known[0].description = "Known description"; known[0].interfaceName = "Known interface";
        enumerator.state = DEVICE_STATE_ACTIVE;
        auto unavailableMetadata = am::detail::collectEndpoints(&enumerator, known);
        require(unavailableMetadata && (*unavailableMetadata)[0].state == "Active", "Actual endpoint state is refreshed even when metadata probes fail");
        require((*unavailableMetadata)[0].rate == 48000 && (*unavailableMetadata)[0].channels == 2, "Transient format query failure preserves the known shared format");
        require((*unavailableMetadata)[0].isVirtual && (*unavailableMetadata)[0].transport == "vbCable", "Failed property query cannot reclassify a healthy cable as physical");
        require((*unavailableMetadata)[0].stableId == "stable-capture" && (*unavailableMetadata)[0].name == "Known cable", "Known identity metadata survives optional property unavailability");
        require((*unavailableMetadata)[0].formFactor == LineLevel && (*unavailableMetadata)[0].description == "Known description" && (*unavailableMetadata)[0].interfaceName == "Known interface", "Known classification and connector labels survive property-store failure");
        known[0].id = "unrelated-capture";
        auto newIdentity = am::detail::collectEndpoints(&enumerator, known);
        require(newIdentity && (*newIdentity)[0].rate == 0 && (*newIdentity)[0].stableId.empty() && !(*newIdentity)[0].isVirtual, "Metadata is never inherited from a different endpoint identity");
        require((*newIdentity)[0].formFactor == -1 && (*newIdentity)[0].description.empty() && (*newIdentity)[0].interfaceName.empty(), "Classification metadata is never inherited from another endpoint identity");

        auto& metadata = enumerator.captureMetadata;
        metadata.available = true;
        metadata.name = L"Duplicated friendly name";
        metadata.description = L"Line input";
        metadata.interfaceName = L"Test USB audio interface";
        metadata.hasFormFactor = true; metadata.formFactor = LineLevel;
        auto described = am::detail::collectEndpoints(&enumerator);
        require(described && (*described)[0].name == "Duplicated friendly name" && (*described)[0].description == "Line input" &&
            (*described)[0].interfaceName == "Test USB audio interface", "Driver description and interface labels are collected independently of friendly name");
        require((*described)[0].formFactor == LineLevel, "Documented unsigned form factor is retained for classification");
        const am::Json serialized = (*described)[0];
        require(serialized.at("formFactor") == LineLevel && serialized.at("description") == "Line input" &&
            serialized.at("interfaceName") == "Test USB audio interface", "Endpoint snapshots serialize classification and disambiguation metadata");
        const am::Json unknownSerialized = (*newIdentity)[0];
        require(unknownSerialized.at("formFactor") == -1, "Unavailable classification serializes as unknown rather than a real category");

        for (const auto factor : {UnknownDigitalPassthrough, SPDIF, DigitalAudioDisplayDevice, UnknownFormFactor}) {
            metadata.formFactor = factor;
            const auto observed = am::detail::collectEndpoints(&enumerator);
            require(observed && (*observed)[0].formFactor == factor, "Digital passthrough, S/PDIF, display audio and Windows Unknown preserve distinct Windows categories");
        }
        metadata.description.clear(); metadata.interfaceName.clear(); metadata.failFormFactor = true;
        auto failedProperties = am::detail::collectEndpoints(&enumerator, *described);
        require(failedProperties && (*failedProperties)[0].formFactor == LineLevel && (*failedProperties)[0].description == "Line input" &&
            (*failedProperties)[0].interfaceName == "Test USB audio interface", "Individual missing properties and failed form-factor reads retain exact-ID metadata");
        metadata.failFormFactor = false;
        metadata.formFactorType = VT_I4; metadata.formFactor = LineLevel;
        const auto malformed = am::detail::collectEndpoints(&enumerator);
        require(malformed && (*malformed)[0].formFactor == -1, "Wrong property type cannot invent a supported form factor");
        const auto malformedKnown = am::detail::collectEndpoints(&enumerator, *described);
        require(malformedKnown && (*malformedKnown)[0].formFactor == LineLevel, "Malformed optional property retains a known form factor");
        metadata.formFactorType = VT_UI4; metadata.formFactor = EndpointFormFactor_enum_count;
        const auto outOfRange = am::detail::collectEndpoints(&enumerator);
        require(outOfRange && (*outOfRange)[0].formFactor == -1, "Out-of-range form factors remain unclassified and therefore visible to the UI");
        metadata.hasFormFactor = false;
        const auto missingProperty = am::detail::collectEndpoints(&enumerator);
        require(missingProperty && (*missingProperty)[0].formFactor == -1, "An empty optional property remains unclassified");
        require(liveObjects == 0, "Metadata reads release every temporary COM property store and device");
        metadata = {};
        enumerator.state = DEVICE_STATE_DISABLED;
        for (auto failure : {Failure::enumerateCapture, Failure::enumerateRender, Failure::count,
                Failure::item, Failure::identifier, Failure::state}) {
            enumerator.failure = failure;
            require(!am::detail::collectEndpoints(&enumerator), "Incomplete enumeration cannot masquerade as device removal");
            require(liveObjects == 0, "Error path releases every completed COM object");
        }
        enumerator.failure = Failure::none;
        enumerator.emptyCapture = true;
        auto removedCapture = am::detail::collectEndpoints(&enumerator);
        require(removedCapture && removedCapture->size() == 1 && (*removedCapture)[0].direction == "render", "Successful capture removal still publishes the healthy render path");
        enumerator.emptyRender = true;
        auto empty = am::detail::collectEndpoints(&enumerator);
        require(empty && empty->empty(), "A successfully empty inventory still records genuine removal");
        enumerator.emptyCapture = enumerator.emptyRender = false;
        auto returned = am::detail::collectEndpoints(&enumerator);
        require(returned && returned->size() == 2, "Returning endpoints remain discoverable after errors and removal");
        require(!am::detail::collectEndpoints(nullptr), "Invalid enumerator is a failure, not an empty inventory");
        require(liveObjects == 0, "All test COM resources were released");
        std::cout << "PASS: " << checks << " endpoint catalog checks using fake COM only; no live audio endpoints opened.\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL after " << checks << " checks: " << error.what() << '\n';
        return 1;
    }
}
