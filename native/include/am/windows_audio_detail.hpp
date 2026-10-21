#pragma once
#include "am/types.hpp"
#include <optional>
#include <span>
#include <string_view>

struct IMMDeviceEnumerator;
struct IMMDevice;
struct IAudioClient;
struct tWAVEFORMATEX;

namespace am::detail {
// A failed read is distinct from a successful empty inventory. The caller must
// retain its last complete snapshot on failure. Accepting the enumerator lets
// offline verification exercise Windows error paths with fake COM objects.
std::optional<std::vector<Endpoint>> collectEndpoints(IMMDeviceEnumerator* enumerator,
    std::span<const Endpoint> previous = {});

// Pure stream-selection policy, shared by initial opening and catalog refresh.
// Generic microphone capture accepts any Windows capture endpoint. Dedicated
// cable nodes continue to require their explicit transport family.
std::string_view streamEndpointRejection(const Endpoint& endpoint, bool capture,
    bool microphone, std::string_view requiredTransport);

// Owns no hardware policy outside this exact device. The returned client owns
// one COM reference and the returned format must be freed with CoTaskMemFree.
// Accepting the device enables offline tests of the actual opening sequence.
// A failed exclusive open never falls back to shared rendering.
long initializeExclusiveRender(IMMDevice* device, IAudioClient** client, tWAVEFORMATEX** format);
// Prefer the shortest legal shared period. Failed initialization retries on a
// fresh client, first at the default period and finally with legacy shared I/O.
// The caller owns the returned COM reference/CoTaskMem format. periodFrames is
// in device-rate frames (zero only when a legacy period cannot be queried).
// Set preferLowLatency=false after repeated stream glitches to start at default
// timing; this still permits legacy fallback and never selects exclusive mode.
long initializeSharedStream(IMMDevice* device, bool capture, IAudioClient** client,
    tWAVEFORMATEX** format, std::uint32_t* periodFrames, bool preferLowLatency = true);
// Event-driven exclusive rendering always services the complete buffer and
// must not use the shared-mode padding calculation.
long renderFramesAvailable(IAudioClient* client, bool exclusive, std::uint32_t bufferFrames,
    std::uint32_t* available);
}
