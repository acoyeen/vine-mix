#pragma once
#include "am/configuration.hpp"
#include "am/dsp.hpp"
#include "am/graph.hpp"
#include "am/windows_audio.hpp"
#include <atomic>
#include <filesystem>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace am {
class Engine {
public:
    explicit Engine(std::filesystem::path settings = {});
    ~Engine();
    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;
    void start();
    void stop();
    void requestStop() noexcept;
    bool stopping() const noexcept;
    Json snapshot();
    void configure(Configuration configuration, std::uint64_t expectedRevision);
    void retry();
    void recover(const std::string& action);
    std::vector<Endpoint> endpoints();
private:
    struct Port;
    struct Meter;
    struct Runtime;
    std::unique_ptr<Runtime> prepare(const Configuration&, GraphPlan);
    void publish(std::unique_ptr<Runtime>);
    void retire(); // configurationMutex_ held, never called by the audio thread
    void mixLoop();
    void maintenanceLoop();
    void validateEndpoints(const Configuration&);
    void writeLog(const std::string& message);
    ConfigurationStore store_;
    Configuration configuration_;
    std::mutex configurationMutex_;
    DeviceCatalog catalog_;
    // Ownership is entirely on control/maintenance threads. The audio thread
    // only reads raw pointers and acknowledges a generation AFTER its final use
    // of retired plans (including the short routing crossfade).
    std::vector<std::unique_ptr<Runtime>> runtimes_;
    std::atomic<Runtime*> published_{nullptr};
    std::atomic<std::uint64_t> activeGeneration_{};
    std::uint64_t nextGeneration_{};
    std::atomic<bool> stopping_{false};
    std::atomic<bool> resumeRequested_{false};
    std::atomic<std::uint64_t> audioFrames_{};
    std::atomic<std::uint64_t> spectrumRevision_{};
    std::thread mixThread_, maintenanceThread_;
    void* stopEvent_{};
    std::mutex endpointsMutex_;
    std::vector<Endpoint> endpoints_;
    std::vector<ApplicationInfo> applications_;
    std::filesystem::path logPath_;
    std::string warning_;
    std::mutex logMutex_;
    std::atomic<bool> started_{false};
};
}
