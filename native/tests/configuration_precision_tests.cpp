#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <array>
#include <iostream>
#include <memory>
#include <stdexcept>

namespace {
std::size_t checks{};
void require(bool condition) {
    ++checks;
    if (!condition) throw std::runtime_error("Stored gain precision or synthetic rendering changed.");
}
void verify(double gain) {
    am::GraphNode input, effect, output;
    input.id = "example-input"; input.kind = "virtualInput"; input.gainDb = gain;
    effect.id = "example-gain"; effect.kind = "gain"; effect.gainDb = -.3;
    output.id = "example-output"; output.kind = "output";
    am::AudioGraph precise;
    precise.nodes = {input, effect, output};
    precise.edges = {{"example-in", input.id, effect.id, "audio", gain}, {"example-out", effect.id, output.id, "audio", 1.2}};
    const auto saved = am::Json(precise).dump();
    auto legacy = precise;
    for (auto& node : legacy.nodes) node.gainDb = static_cast<float>(node.gainDb);
    for (auto& edge : legacy.edges) edge.gainDb = static_cast<float>(edge.gainDb);
    const auto precisePlan = std::make_unique<am::GraphPlan>(am::compileGraph(precise));
    const auto legacyPlan = std::make_unique<am::GraphPlan>(am::compileGraph(legacy));
    require(*precisePlan == *legacyPlan);
    require(precise.nodes[0].gainDb == gain && precise.edges[0].gainDb == gain);
    require(am::Json(precise).dump() == saved);
    auto preciseProcessor = std::make_unique<am::GraphProcessor>();
    auto legacyProcessor = std::make_unique<am::GraphProcessor>();
    preciseProcessor->prepare(*precisePlan); legacyProcessor->prepare(*legacyPlan);
    std::array<am::Frame, am::quantumFrames> signal{};
    am::GraphProcessor::Inputs inputs{}; inputs[0] = signal;
    for (int block = 0; block < 20; ++block) {
        for (std::size_t frame = 0; frame < signal.size(); ++frame) {
            const float sample = static_cast<float>((frame + block) % 17 - 8.) * .0001f;
            signal[frame] = {sample, -sample};
        }
        preciseProcessor->process(*precisePlan, inputs); legacyProcessor->process(*legacyPlan, inputs);
        for (std::size_t frame = 0; frame < signal.size(); ++frame) {
            const auto a = preciseProcessor->output(2)[frame], b = legacyProcessor->output(2)[frame];
            require(a.left == b.left && a.right == b.right);
        }
    }
}
}
int main() {
    try {
        for (double gain : {-60., -59.999999999, -6., -6.5, -6.1, -.3, .1 + .2, 1.2, 11.999999999, 12.}) verify(gain);
        std::cout << "PASS: " << checks << " precision/legacy-render assertions. Synthetic memory buffers only; no audio devices or personal settings.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
