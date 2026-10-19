#pragma once
#include "am/graph.hpp"

namespace am {
// Auxiliary state is allocated during GraphProcessor::prepare. Every operation
// after construction is bounded, with no allocation, locking or I/O.
class AdvancedEffect {
public:
    struct Result { Frame wet, dry; };
    explicit AdvancedEffect(GraphKind kind);
    ~AdvancedEffect();
    void configure(const GraphPlan::Node& node) noexcept;
    Result process(Frame input) noexcept;
    void inherit(const AdvancedEffect& previous) noexcept;
    void reset() noexcept;
    float gainReductionDb() const noexcept;
    float detectorDb() const noexcept;
    static bool supports(GraphKind kind) noexcept;
    static std::uint32_t latencyFrames(GraphKind kind, std::string_view algorithm = "speex") noexcept {
        return kind == GraphKind::denoiser ? (algorithm == "rnnoise" ? 1440 : 960) : 0;
    }
private:
    struct State;
    std::unique_ptr<State> state_;
};
}
