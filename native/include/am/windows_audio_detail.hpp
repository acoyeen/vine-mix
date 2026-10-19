#pragma once
#include "am/types.hpp"
#include <optional>
#include <span>

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

// Owns no hardware policy outside this exact device. The returned client owns
// one COM reference and the returned format must be freed with CoTaskMemFree.
// Accepting the device enables offline tests of the actual opening sequence.
// A failed exclusive open never falls back to shared rendering.
long initializeExclusiveRender(IMMDevice* device, IAudioClient** client, tWAVEFORMATEX** format);
// Event-driven exclusive rendering always services the complete buffer and
// must not use the shared-mode padding calculation.
long renderFramesAvailable(IAudioClient* client, bool exclusive, std::uint32_t bufferFrames,
    std::uint32_t* available);
}
