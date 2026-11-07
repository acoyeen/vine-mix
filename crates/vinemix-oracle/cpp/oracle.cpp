// Test-only C ABI over the unmodified 0.5 C++ graph compiler and processor.
// Used by vinemix-oracle's parity tests; never shipped.
#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <cstring>
#include <memory>
#include <string>

namespace {
struct Oracle {
    am::GraphPlan plan;
    std::unique_ptr<am::GraphProcessor> processor;
};
void copyError(const std::exception& error, char* buffer, int length) {
    if (!buffer || length <= 0) return;
    std::strncpy(buffer, error.what(), static_cast<std::size_t>(length - 1));
    buffer[length - 1] = 0;
}
am::GraphPlan compile(const char* json) {
    am::AudioGraph graph;
    am::Json::parse(json).get_to(graph);
    return am::compileGraph(graph);
}
}

extern "C" {
void* am_oracle_create(const char* graphJson, char* error, int errorLength) {
    try {
        auto oracle = std::make_unique<Oracle>();
        oracle->plan = compile(graphJson);
        oracle->processor = std::make_unique<am::GraphProcessor>();
        oracle->processor->prepare(oracle->plan);
        return oracle.release();
    } catch (const std::exception& e) { copyError(e, error, errorLength); return nullptr; }
}
// mode 0: inherit (const copy); 1: adopt without fade; 2: adopt preserving the fade.
int am_oracle_update(void* handle, const char* graphJson, int mode, char* error, int errorLength) {
    try {
        auto* oracle = static_cast<Oracle*>(handle);
        auto plan = compile(graphJson);
        auto next = std::make_unique<am::GraphProcessor>();
        next->prepare(plan);
        if (mode == 0) next->inherit(*oracle->processor, oracle->plan, plan);
        else next->adopt(*oracle->processor, oracle->plan, plan, mode == 2);
        oracle->processor = std::move(next);
        oracle->plan = std::move(plan);
        return 1;
    } catch (const std::exception& e) { copyError(e, error, errorLength); return 0; }
}
void am_oracle_destroy(void* handle) { delete static_cast<Oracle*>(handle); }
// inputs: 32 nodes x 240 frames x {L,R}; a null row pointer means no input.
void am_oracle_process(void* handle, const float* const* inputs, int deferSinkClipping) {
    auto* oracle = static_cast<Oracle*>(handle);
    am::GraphProcessor::Inputs views{};
    for (std::size_t i = 0; i < am::maximumGraphNodes; ++i)
        if (inputs[i]) views[i] = std::span<const am::Frame>(reinterpret_cast<const am::Frame*>(inputs[i]), am::quantumFrames);
    oracle->processor->process(oracle->plan, views, deferSinkClipping != 0);
}
const float* am_oracle_output(void* handle, int node) {
    return reinterpret_cast<const float*>(static_cast<Oracle*>(handle)->processor->output(static_cast<std::size_t>(node)).data());
}
void am_oracle_meter(void* handle, int node, float* out) {
    const auto& meter = static_cast<Oracle*>(handle)->processor->meter(static_cast<std::size_t>(node));
    out[0] = meter.peak; out[1] = meter.peakLeft; out[2] = meter.peakRight;
    out[3] = meter.clipped ? 1.f : 0.f; out[4] = meter.gainReductionDb; out[5] = meter.detectorDb;
}
void am_oracle_reset(void* handle) { static_cast<Oracle*>(handle)->processor->reset(); }
int am_oracle_signal_channels(void* handle, const int* capture, int* out) {
    auto* oracle = static_cast<Oracle*>(handle);
    am::GraphChannels channels{};
    for (std::size_t i = 0; i < am::maximumGraphNodes; ++i) channels[i] = capture[i];
    const auto result = am::graphSignalChannels(oracle->plan, channels);
    for (std::size_t i = 0; i < am::maximumGraphNodes; ++i) out[i] = result[i];
    return static_cast<int>(oracle->plan.nodeCount);
}
// The C++ validation message for a graph, or an empty string when it compiles.
void am_oracle_validate(const char* graphJson, char* error, int errorLength) {
    error[0] = 0;
    try { (void)compile(graphJson); } catch (const std::exception& e) { copyError(e, error, errorLength); }
}
}
