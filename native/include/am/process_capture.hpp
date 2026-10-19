#pragma once
#include "am/application_types.hpp"
#include "am/types.hpp"
#include <memory>
#include <vector>

namespace am {
class FrameQueue;

// Read-only discovery of this session's visible applications and applications
// with audio sessions. Neither function opens a capture stream or changes an
// application's playback device, mute, or volume. Call outside the audio loop.
bool processCaptureAvailable() noexcept;
std::vector<ApplicationInfo> enumerateAudioApplications();

// Captures the selected application and its process tree. This is a copy of
// rendered audio, not a replacement for the application's original playback.
// Produces bounded stereo 48 kHz frames, with local recovery after app restart.
class ProcessCaptureWorker {
public:
    explicit ProcessCaptureWorker(std::shared_ptr<FrameQueue> queue);
    ~ProcessCaptureWorker();
    ProcessCaptureWorker(const ProcessCaptureWorker&) = delete;
    ProcessCaptureWorker& operator=(const ProcessCaptureWorker&) = delete;
    void configure(ProcessBinding binding);
    void retry() noexcept;
    bool ready() const noexcept;
    PathState status() const;
    void stop() noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
