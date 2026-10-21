#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <windows.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <complex>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <memory>
#include <numbers>
#include <random>
#include <set>
#include <sstream>
#include <stdexcept>

// Offline only: no device, process capture, engine, pipe or audio APIs are linked.
namespace {
using namespace am;
std::size_t assertions{}, frames{};
void require(bool value, const std::string& why) {
    ++assertions;
    if (!value) throw std::runtime_error(why);
}
void expectNear(double actual, double expected, double tolerance, const std::string& why) {
    require(std::isfinite(actual) && std::abs(actual - expected) <= tolerance,
        why + " actual=" + std::to_string(actual) + " expected=" + std::to_string(expected));
}
template<class F> void rejects(F f, const std::string& why) {
    bool caught = false;
    try { f(); } catch (const std::exception&) { caught = true; }
    require(caught, why);
}
GraphNode node(std::string id, const std::string& kind) {
    GraphNode n; n.id = std::move(id); n.kind = kind; n.name = n.id; return n;
}
AudioGraph chain(const std::string& kind) {
    AudioGraph g; g.nodes = {node("input", "microphone"), node("effect", kind), node("output", "output")};
    g.edges = {{"a", "input", "effect"}, {"b", "effect", "output"}}; return g;
}
std::size_t index(const GraphPlan& p, const std::string& id) {
    for (std::size_t i = 0; i < p.nodeCount; ++i) if (p.nodes[i].id == id) return i;
    throw std::runtime_error("Missing node " + id);
}
void block(GraphProcessor& p, const GraphPlan& plan, Frame sample) {
    std::array<Frame, quantumFrames> input; input.fill(sample);
    GraphProcessor::Inputs inputs{}; inputs[index(plan, "input")] = input;
    p.process(plan, inputs); frames += quantumFrames;
}
void settle(GraphProcessor& p, const GraphPlan& plan, Frame sample, int count = 6) {
    for (int i = 0; i < count; ++i) block(p, plan, sample);
}
std::string read(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary); std::ostringstream result; result << file.rdbuf(); return result.str();
}
void write(const std::filesystem::path& path, const std::string& text) {
    std::ofstream file(path, std::ios::binary); file << text; require(static_cast<bool>(file), "Write test fixture");
}
void settings(const std::filesystem::path& root) {
    const auto path = root / "settings.json";
    Configuration initial; migrateLegacyGraph(initial); initial.revision = 41;
    Configuration current = initial; current.revision = 42; current.graph.nodes[0].name = "Preserved microphone";
    ConfigurationStore store(path); store.save(initial); store.save(current);
    const auto primary = read(path), backup = read(path.string() + ".bak");
    require(ConfigurationStore(path).load().revision == 42, "Valid current settings load");
    require(Json::parse(backup).at("revision") == 41, "Last-good backup is previous complete commit");
    std::filesystem::remove(path);
    ConfigurationStore missingPrimary(path);
    const auto restoredMissing = missingPrimary.load();
    require(restoredMissing.revision == 41 && !missingPrimary.warning().empty(), "Missing primary restores a surviving last-good backup with a warning");
    require(!std::filesystem::exists(path) && read(path.string() + ".bak") == backup, "Missing-file recovery is read-only and preserves the backup");
    auto committedMissing = restoredMissing; committedMissing.revision = 43;
    rejects([&] { missingPrimary.save(committedMissing); }, "Backup preview cannot be saved without a recovery choice");
    missingPrimary.recover("restoreBackup");
    missingPrimary.save(committedMissing);
    require(ConfigurationStore(path).load().revision == 43 && read(path.string() + ".bak") == backup, "Saving recovered missing-primary settings retains the original good backup");
    write(path, primary);
    for (const auto version : {std::uint64_t{999}, std::numeric_limits<std::uint64_t>::max()}) {
        auto unsupported = Json(current); unsupported["version"] = version;
        write(path, unsupported.dump());
        ConfigurationStore future(path); future.load();
        require(future.loadState() == "unsupported", "Future schema cannot narrow to a supported version or fall back to an older backup");
        rejects([&] { future.recover("startFresh"); }, "Future schema cannot be overwritten by recovery");
        rejects([&] { future.save(current); }, "Future schema blocks configuration writes");
        require(read(path) == unsupported.dump(), "Future schema bytes remain intact");
    }
    const std::array<std::string, 4> damaged{"{", primary.substr(0, primary.size() / 2), std::string(262145, ' '), "null"};
    for (const auto& text : damaged) {
        write(path, text); ConfigurationStore recovery(path); const auto restored = recovery.load();
        require(restored.revision == 41 && recovery.loadState() == "backupAvailable" && !recovery.writable(), "Damaged files expose a read-only backup preview");
        require(!recovery.warning().empty(), "Recovery is visible to user");
        require(read(path) == text && read(path.string() + ".bak") == backup, "Read-only recovery preserves original and backup exactly");
    }
    ConfigurationStore reload(path);
    require(reload.load().revision == 41 && !reload.warning().empty(), "Reused store first observes damaged primary");
    write(path, primary);
    require(reload.load().revision == 42 && reload.warning().empty(), "Reloading a repaired primary clears stale recovery state");
    auto repaired = current; repaired.revision = 43;
    reload.save(repaired);
    require(read(path.string() + ".bak") == primary, "Save after external repair backs up the valid primary normally");
    write(path, primary); write(path.string() + ".bak", backup);
    write(path, "broken original"); write(path.string() + ".bak", "broken backup");
    ConfigurationStore defaults(path); const auto safe = defaults.load();
    rejects([&] { defaults.save(safe); }, "Two damaged files cannot silently commit defaults");
    require(safe.revision == 0 && !defaults.warning().empty(), "Two damaged files yield safe defaults and warning");
    require(std::all_of(safe.graph.nodes.begin(), safe.graph.nodes.end(), [](const auto& n) { return n.device.id.empty() && n.device.stableId.empty() && !n.app.processId; }), "Recovery never assigns hardware or applications");
    require(read(path) == "broken original" && read(path.string() + ".bak") == "broken backup", "Fallback preserves both corrupt originals");
    write(path.string() + ".bak", backup);
    ConfigurationStore recovered(path); recovered.load(); auto restored = recovered.recover("restoreBackup"); restored.revision = 43; recovered.save(restored);
    require(ConfigurationStore(path).load().revision == 43 && read(path.string() + ".bak") == backup, "Commit after fallback preserves usable backup");
    bool archived = false;
    for (const auto& entry : std::filesystem::directory_iterator(root))
        if (entry.path().filename().string().starts_with("settings.json.corrupt-")) archived |= read(entry.path()) == "broken original";
    require(archived, "Saving recovered settings archives exact damaged bytes");
    const auto beforeFailure = read(path), backupBeforeFailure = read(path.string() + ".bak");
    HANDLE locked = CreateFileW((path.wstring() + L".new").c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    require(locked != INVALID_HANDLE_VALUE, "Obtain test-only staging-file lock");
    auto next = restored; next.revision = 44;
    rejects([&] { recovered.save(next); }, "Staging-file lock rejects save"); CloseHandle(locked);
    require(read(path) == beforeFailure && read(path.string() + ".bak") == backupBeforeFailure, "Failed staging leaves primary and backup byte-identical");
    locked = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    require(locked != INVALID_HANDLE_VALUE, "Obtain test-only primary replacement lock");
    rejects([&] { recovered.save(next); }, "Replacement lock rejects save"); CloseHandle(locked);
    require(read(path) == beforeFailure && read(path.string() + ".bak") == backupBeforeFailure, "Failed replacement preserves last committed settings and backup");
    auto invalid = next; invalid.graph.nodes[0].gainDb = 13;
    rejects([&] { recovered.save(invalid); }, "Invalid configuration rejects before persistence");
    require(read(path) == beforeFailure, "Invalid change does not mutate committed configuration");
    // Individually valid fields can jointly exceed the loader's size limit.
    auto oversized = next;
    for (auto& n : oversized.graph.nodes) n.app.executablePath = std::string(32768, 'x');
    require(Json(oversized).dump(2).size() > 262144, "Construct legal-field oversized document");
    rejects([&] { recovered.save(oversized); }, "A commit must not create a file its loader refuses");
    require(read(path) == beforeFailure, "Oversized rejected commit preserves current configuration");
    {
        // --settings also accepts a filename in the current directory. Keep
        // this process-wide change inside a new test-only fixture directory.
        const auto relativeRoot = root / "relative-path";
        std::filesystem::create_directory(relativeRoot);
        struct RestoreDirectory {
            std::filesystem::path previous = std::filesystem::current_path();
            ~RestoreDirectory() {
                std::error_code ignored;
                std::filesystem::current_path(previous, ignored);
            }
        } restore;
        std::filesystem::current_path(relativeRoot);
        const std::filesystem::path relativePath = "settings.json";
        ConfigurationStore relative(relativePath);
        relative.save(initial);
        require(relative.load().revision == 41, "A relative settings filename saves and loads in the current directory");
        relative.save(current);
        require(relative.load().revision == 42, "A relative settings filename supports atomic replacement");
        require(Json::parse(read("settings.json.bak")).at("revision") == 41,
            "A relative settings filename retains the previous complete commit as backup");
    }
    std::cout << "PASS settings: corrupt/truncated/oversized/version fallback, preservation, and write-failure atomicity\n";
}
void graphBoundsAndPermutations() {
    AudioGraph g;
    for (int i = 0; i < 32; ++i) { auto n = node("n" + std::to_string(i), i == 0 ? "microphone" : i == 31 ? "output" : "mix"); n.gainDb = -static_cast<float>(i % 4); g.nodes.push_back(n); }
    std::set<std::pair<int, int>> links;
    for (int i = 0; i < 31; ++i) links.emplace(i, i + 1);
    std::mt19937 random(0xC0FFEE);
    while (links.size() < 96) { int a = random() % 31, b = a + 1 + random() % (31 - a); links.emplace(a, b); }
    for (const auto [a, b] : links) g.edges.push_back({"e" + std::to_string(g.edges.size()), "n" + std::to_string(a), "n" + std::to_string(b), "audio", -12, false});
    const auto original = compileGraph(g);
    require(original.nodeCount == 32 && original.edgeCount == 96, "Exact 32 nodes and 96 unique cables accepted");
    auto invalid = g; invalid.nodes.push_back(node("excess", "gain")); rejects([&] { compileGraph(invalid); }, "33 nodes rejected");
    invalid = g;
    for (int a = 0; a < 31 && invalid.edges.size() == 96; ++a) for (int b = a + 1; b < 32; ++b)
        if (!links.contains({a,b})) { invalid.edges.push_back({"excess", "n" + std::to_string(a), "n" + std::to_string(b)}); break; }
    rejects([&] { compileGraph(invalid); }, "97 unique valid cables rejected");
    std::array<double, 32> expected{}; expected[0] = .125;
    for (int target = 1; target < 32; ++target) {
        for (const auto [a,b] : links) if (b == target) expected[target] += expected[a] * std::pow(10., -12. / 20.);
        expected[target] *= std::pow(10., -static_cast<double>(target % 4) / 20.);
    }
    for (int trial = 0; trial < 80; ++trial) {
        std::shuffle(g.nodes.begin(), g.nodes.end(), random); std::shuffle(g.edges.begin(), g.edges.end(), random);
        const auto p = compileGraph(g); require(sameGraphTopology(original, p), "Permutation preserves topology identity");
        auto processor = std::make_unique<GraphProcessor>(); std::array<Frame, quantumFrames> samples; samples.fill({.125f, -.0625f});
        GraphProcessor::Inputs views{}; views[index(p,"n0")] = samples;
        for (int blockIndex = 0; blockIndex < 4; ++blockIndex) { processor->process(p, views); frames += quantumFrames; }
        for (int i = 0; i < 32; ++i) {
            const auto value = processor->output(index(p,"n" + std::to_string(i))).back();
            expectNear(value.left, expected[i], 2e-7, "DAG output matches independent scalar path-sum"); expectNear(value.right, -expected[i] * .5, 2e-7, "DAG right channel retains independent sign and amplitude");
        }
        auto bad = g;
        auto removable = std::find_if(bad.edges.begin(), bad.edges.end(), [](const auto& e) { return std::stoi(e.to.substr(1)) != std::stoi(e.from.substr(1)) + 1; });
        if (removable != bad.edges.end()) bad.edges.erase(removable); else bad.edges.pop_back();
        bad.edges.push_back({"feedback", "n30", "n1", "audio", -60, true});
        rejects([&] { compileGraph(bad); }, "Muted feedback rejected regardless of node/cable order");
        bad = g; bad.edges[0].to = "deleted-node"; rejects([&] { compileGraph(bad); }, "Dangling cable rejected regardless of order");
    }
    std::cout << "PASS graph bounds: 80 seeded permutations at 32 nodes/96 cables with scalar-reference stereo outputs\n";
}
void dynamics() {
    for (double attack : {.1, 10., 200.}) {
        auto g = chain("compressor"); g.nodes[1].compressor = {-20, 4, attack, 120, 0};
        const auto p = compileGraph(g); auto dsp = std::make_unique<GraphProcessor>(); settle(*dsp,p,{.01f,.005f});
        const double finalGain = std::pow(5., -.75);
        const int measuredFrames = static_cast<int>(std::lround(attack * .001 * sampleRate));
        double measured{};
        for (int count = 0; count < measuredFrames; count += quantumFrames) {
            block(*dsp,p,{.5f,.25f});
            if (count + static_cast<int>(quantumFrames) >= measuredFrames)
                measured = dsp->output(2)[measuredFrames - count - 1].left / .5;
        }
        expectNear((measured - finalGain) / (1 - finalGain), std::exp(-measuredFrames / (attack * .001 * sampleRate)), 3e-6, "Attack reaches one time constant within sample quantization independently measured from signal");
        settle(*dsp,p,{.5f,.25f}, static_cast<int>(attack / 5 * 16 + 10));
        const double left = dsp->output(2).back().left;
        expectNear(left, .5 * finalGain, 2e-6, "Compressor final ratio"); expectNear(dsp->output(2).back().right, left * .5, 1e-7, "Stereo gain is linked");
    }
    for (double release : {5., 120., 2000.}) {
        auto g = chain("compressor"); g.nodes[1].compressor = {-20, 4, .1, release, 0};
        const auto p = compileGraph(g); auto dsp = std::make_unique<GraphProcessor>(); settle(*dsp,p,{.5f,.25f},100);
        const double initial = dsp->output(2).back().left / .5;
        const int measuredFrames = static_cast<int>(release * .001 * sampleRate); double measured{};
        for (int count = 0; count < measuredFrames; count += quantumFrames) {
            block(*dsp,p,{.01f,.005f});
            if (count + static_cast<int>(quantumFrames) >= measuredFrames)
                measured = dsp->output(2)[measuredFrames - count - 1].left / .01;
        }
        expectNear((1 - measured) / (1 - initial), std::exp(-1.), 3e-6, "Release reaches one time constant independently measured from signal");
    }
    std::cout << "PASS compressor: signal-measured attack 0.1/10/200 ms; release 5/120/2000 ms and stereo linking\n";
}
void equalizerExtremes() {
    for (const auto* type : {"peak", "lowShelf", "highShelf", "lowPass", "highPass"})
        for (double frequency : {20.,20000.}) for (double q : {.1,20.}) for (double gain : {-24.,24.}) {
            auto g = chain("eq"); g.nodes[1].eqBands = {{type,true,frequency,gain,q}}; const auto p = compileGraph(g);
            const auto& c = p.nodes[1].eq[0];
            const auto discriminant = std::sqrt(std::complex<double>(c.a1*c.a1 - 4*c.a2,0));
            require(std::abs((-c.a1 + discriminant) / 2.) < 1 && std::abs((-c.a1 - discriminant) / 2.) < 1, "All valid EQ extrema have stable poles inside unit circle");
            auto dsp = std::make_unique<GraphProcessor>(); settle(*dsp,p,{0,0});
            std::array<Frame, quantumFrames> samples{}; samples[0] = {.001f,-.0005f}; GraphProcessor::Inputs views{}; views[0] = samples;
            double energy{}; bool valid = true; double peak{};
            for (int b = 0; b < 400; ++b) {
                dsp->process(p,views); frames += quantumFrames;
                for (const auto sample : dsp->output(1)) {
                    valid &= std::isfinite(sample.left) && std::isfinite(sample.right) && std::abs(sample.right + sample.left * .5f) < 1e-7;
                    peak = std::max(peak, static_cast<double>(std::abs(sample.left))); energy += sample.left*sample.left;
                }
                samples.fill({});
            }
            require(valid && std::isfinite(energy) && peak < 1, "Extreme EQ impulse stays finite, un-clipped and stereo-linear before sink");
        }
    std::cout << "PASS EQ: 40 legal frequency/gain/Q/filter extrema, independent pole stability and 2-second impulse traces\n";
}
void transitions() {
    for (const auto* kind : {"gain", "pan", "compressor", "eq"}) {
        auto g = chain(kind);
        if (g.nodes[1].kind == "pan") g.nodes[1].balance = 1;
        if (g.nodes[1].kind == "compressor") g.nodes[1].compressor = {-20,4,.1,5,0};
        if (g.nodes[1].kind == "eq") g.nodes[1].eqBands = {{"lowShelf",true,1000,-12,.707}};
        auto p = compileGraph(g); auto dsp = std::make_unique<GraphProcessor>(); settle(*dsp,p,{.25f,.125f},200);
        const double old = dsp->output(2).back().left;
        if (g.nodes[1].kind == "gain") g.nodes[1].gainDb = -24; else g.nodes[1].bypass = true;
        auto nextPlan = compileGraph(g); auto next = std::make_unique<GraphProcessor>(); next->inherit(*dsp,p,nextPlan);
        const double target = g.nodes[1].kind == "gain" ? .25*std::pow(10.,-24./20.) : .25;
        double previous = old;
        for (int b = 0; b < 2; ++b) {
            block(*next,nextPlan,{.25f,.125f});
            for (const auto sample : next->output(2)) { require(std::abs(sample.left-previous) <= std::abs(target-old)/480+2e-6, "Bypass/level transition has bounded sample step"); previous = sample.left; }
        }
        expectNear(previous,target,2e-6,"Bypass or level reaches target in 10 ms");
    }
    auto g = chain("gain"); g.nodes.push_back(node("control","control")); g.nodes.back().gainDb = -12;
    g.edges.push_back({"control-edge","control","effect","control",0,false});
    auto p = compileGraph(g); auto dsp = std::make_unique<GraphProcessor>(); settle(*dsp,p,{.4f,.2f});
    double previous = dsp->output(2).back().left; g.edges.back().muted = true; g.nodes.back().muted = true;
    auto nextPlan = compileGraph(g); auto next = std::make_unique<GraphProcessor>(); next->inherit(*dsp,p,nextPlan);
    const double maximumStep = (.4-previous)/480+1e-6;
    for (int b = 0; b < 2; ++b) {
        block(*next,nextPlan,{.4f,.2f}); for(const auto sample : next->output(2)) {
            require(sample.left >= previous-1e-7 && sample.left-previous <= maximumStep,"Disabled muted controller restores local audio monotonically over ramp"); previous=sample.left;
        }
    }
    expectNear(previous,.4,1e-6,"Muted control link bypass restores local value");
    std::cout << "PASS transitions: per-sample gain/bypass/control slope limits and exact 10 ms settling\n";
}
}
int main(int argc, char** argv) {
    try {
        if (argc != 2) throw std::runtime_error("Supply an output directory for test-only settings fixtures.");
        const auto root = std::filesystem::absolute(argv[1]);
        if (std::filesystem::exists(root)) throw std::runtime_error("Fixture directory must be new; existing files are never overwritten.");
        std::filesystem::create_directories(root);
        settings(root); graphBoundsAndPermutations(); dynamics(); equalizerExtremes(); transitions();
        std::cout << "PASS: " << assertions << " qualification assertions; " << frames << " synthetic frames; no audio devices or personal settings accessed.\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL after " << assertions << " assertions: " << error.what() << '\n'; return 1; }
}
