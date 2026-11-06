#pragma once
#include "am/graph.hpp"

namespace am {
// Auxiliary state is allocated during GraphProcessor::prepare. Every operation
// after construction is bounded, with no allocation, locking or I/O.
class AdvancedEffect {
public:
    // wet is the processed main signal, including its Mix setting. effectWet
    // exposes the Reverb or Echo contribution before that Mix operation.
    struct Result { Frame wet, dry, effectWet{}; };
    explicit AdvancedEffect(GraphKind kind);
    ~AdvancedEffect();
    void configure(const GraphPlan::Node& node) noexcept;
    // A null detector preserves self-detection; a connected silent detector
    // remains silent and never falls back to the programme signal.
    Result process(Frame input, const Frame* detector = nullptr) noexcept;
    void inherit(const AdvancedEffect& previous) noexcept;
    // Engine-only ownership transfer; a fading predecessor retains 10 ms of
    // Echo output, independently of the transferred long history.
    void adopt(AdvancedEffect& previous, bool preserveFade) noexcept;
    void reset() noexcept;
    float gainReductionDb() const noexcept;
    float detectorDb() const noexcept;
    static bool supports(GraphKind kind) noexcept;
    static std::uint32_t latencyFrames(GraphKind kind, std::string_view algorithm = "speex") noexcept {
        return kind == GraphKind::denoiser ? (algorithm == "rnnoise" ? 1440 : 960) : kind == GraphKind::distortion ? 32 : 0;
    }
private:
    struct State;
    std::unique_ptr<State> state_;
};
}
