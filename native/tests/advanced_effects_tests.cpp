// Synthetic in-memory DSP only. No endpoint/device/engine implementation linked.
#include "am/advanced_effects.hpp"
#include "am/configuration.hpp"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <new>
#include <numbers>
#include <random>
#include <stdexcept>
#include <vector>

static bool trackAllocations{};
static std::uint64_t allocationOperations{};
void* operator new(std::size_t count) { if (trackAllocations) ++allocationOperations; if (void* p = std::malloc(count ? count : 1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t count) { return ::operator new(count); }
void operator delete(void* p) noexcept { if (trackAllocations) ++allocationOperations; std::free(p); }
void operator delete[](void* p) noexcept { ::operator delete(p); }
extern "C" std::uint64_t am_speex_memory_operations(void);
namespace {
using namespace am;
std::uint64_t checks{};
void require(bool ok, const std::string& message) { ++checks; if (!ok) throw std::runtime_error(message); }
void near(double value, double target, double tolerance, const std::string& message) {
    require(std::isfinite(value) && std::abs(value-target) <= tolerance, message + " actual=" + std::to_string(value) + " expected=" + std::to_string(target));
}
template<class F> void rejects(F action, const std::string& message) { bool bad{}; try { action(); } catch (const std::exception&) { bad = true; } require(bad, message); }
GraphNode node(const std::string& id, const std::string& kind) {
    GraphNode n; n.id = n.name = id; n.kind = kind;
    if (kind == "gate") n.expander.mode = "gate";
    return n;
}
AudioGraph chain(const std::string& kind) {
    AudioGraph graph; graph.nodes = {node("in", "virtualInput"), node("fx", kind), node("out", "output")};
    graph.edges = {{"a", "in", "fx"}, {"b", "fx", "out"}}; return graph;
}
double sine(std::size_t frame, double frequency) { return std::sin(2 * std::numbers::pi * frequency * frame / sampleRate); }
std::vector<Frame> render(const AudioGraph& graph, const std::vector<Frame>& input) {
    const auto plan = compileGraph(graph); auto dsp = std::make_unique<GraphProcessor>(); dsp->prepare(plan);
    std::vector<Frame> result; result.reserve(input.size());
    GraphProcessor::Inputs inputs{};
    for (std::size_t offset = 0; offset < input.size(); offset += quantumFrames) {
        inputs[0] = std::span(input).subspan(offset, quantumFrames); dsp->process(plan, inputs);
        result.insert(result.end(), dsp->output(2).begin(), dsp->output(2).end());
    }
    return result;
}
double rms(const std::vector<Frame>& values, std::size_t first, std::size_t last, bool right = false) {
    double energy{}; for (std::size_t i = first; i < last; ++i) { const auto value = right ? values[i].right : values[i].left; energy += value * value; }
    return std::sqrt(energy / (last-first));
}
void spectralReduction() {
    std::mt19937 random(7391); std::uniform_real_distribution<float> noise(-.025f, .025f);
    std::vector<Frame> signal(sampleRate * 4);
    for (auto& value : signal) value = {noise(random), noise(random)};
    auto graph = chain("denoiser"); graph.nodes[1].denoiser.suppressionDb = -24;
    const auto cleaned = render(graph, signal);
    double ratio = rms(cleaned, sampleRate*2, sampleRate*4) / rms(signal, sampleRate*2, sampleRate*4);
    require(ratio < .65 && ratio > .001, "Spectral denoising lowers steady seeded broadband noise without becoming a gate");
    std::cout << "Denoiser stationary-noise RMS ratio " << ratio << '\n';
    // Train with noise, then add a changing voiced harmonic signal. Measure
    // correlation with the clean signal, not merely any remaining output.
    std::vector<float> cleanVoice(signal.size());
    for (std::size_t i = sampleRate*2; i < signal.size(); ++i) {
        const double time = static_cast<double>(i)/sampleRate;
        const double phase = 2*std::numbers::pi*180*time - 25./3*std::cos(2*std::numbers::pi*3*time);
        const double envelope = .55 + .45*std::sin(2*std::numbers::pi*5*time);
        const float voice = cleanVoice[i] = static_cast<float>(envelope*(.18*std::sin(phase)+.08*std::sin(2*phase)+.04*std::sin(4*phase)));
        signal[i].left += voice;
        signal[i].right = 0; // independent stereo estimator must not invent right-channel speech
    }
    const auto speech = render(graph, signal); double cross{}, referenceEnergy{};
    for (std::size_t i = sampleRate*3; i < signal.size(); ++i) {
        const double voice = cleanVoice[i-960];
        cross += speech[i].left * voice; referenceEnergy += voice*voice;
    }
    std::cout << "Denoiser voiced correlation gain " << cross/referenceEnergy << '\n';
    require(cross/referenceEnergy > .45, "Denoiser retains a newly arriving voiced harmonic signal after noise-only adaptation");
    require(rms(speech, sampleRate*3, sampleRate*4, true) < 1e-4, "Denoiser keeps independent stereo channels isolated");

    // With noise suppression at zero, locate the sample delay using an isolated
    // pulse. The bypass dry ring must have the identical delay, including after
    // toggles and recovery; no zero-latency/delayed crossfade comb filtering.
    std::vector<Frame> pulse(sampleRate); pulse[2400] = {.5f, -.25f};
    graph.nodes[1].denoiser.suppressionDb = 0;
    const auto transparent = render(graph, pulse);
    const auto maximum = std::max_element(transparent.begin(), transparent.end(), [](Frame a, Frame b) { return std::abs(a.left) < std::abs(b.left); });
    require(static_cast<std::size_t>(maximum-transparent.begin()) == 2400+960, "Measured Speex plus frame staging latency is exactly 960 frames / 20 ms");
    graph.nodes[1].bypass = true; const auto bypassed = render(graph, pulse);
    near(bypassed[3360].left, .5, 1e-7, "Denoiser bypass preserves dry audio with the same 20 ms delay");
    near(bypassed[3360].right, -.25, 1e-7, "Denoiser bypass preserves stereo polarity");
}
void splitBand() {
    for (const char* kind : {"deEsser", "plosive"}) {
        auto graph = chain(kind); const bool high = graph.nodes[1].kind == "deEsser";
        graph.nodes[1].deEsser = {4000, -30, 18}; graph.nodes[1].plosive = {180, -30, 18};
        std::vector<Frame> selected(sampleRate), other(sampleRate);
        for (std::size_t i = 0; i < selected.size(); ++i) {
            selected[i] = {static_cast<float>(.2*sine(i,high ? 10000 : 45)),static_cast<float>(.1*sine(i,high ? 10000 : 45))};
            other[i] = {static_cast<float>(.2*sine(i,high ? 200 : 6000)),static_cast<float>(.1*sine(i,high ? 200 : 6000))};
        }
        const auto reduced = render(graph, selected), preserved = render(graph, other);
        const double ratio = rms(reduced, sampleRate/2, sampleRate)/rms(selected,sampleRate/2,sampleRate);
        require(ratio < .55, std::string(kind)+" reduces its loud selected frequency band");
        require(rms(preserved,sampleRate/2,sampleRate)/rms(other,sampleRate/2,sampleRate) > .94,
            std::string(kind)+" retains distant voice frequencies instead of lowering the whole signal");
        auto quiet = selected; for (auto& value : quiet) { value.left *= .005f; value.right *= .005f; }
        const auto below = render(graph, quiet);
        near(rms(below,sampleRate/2,sampleRate),rms(quiet,sampleRate/2,sampleRate),1e-9,
            "Below-threshold energy is retained; the processor is dynamic attenuation, not a permanent high/low cut");
        for (std::size_t i = sampleRate/2; i < reduced.size(); ++i) near(reduced[i].right,reduced[i].left*.5,1e-6,"Dynamic band reduction is stereo linked");
        graph.nodes[1].deEsser.reductionDb = graph.nodes[1].plosive.reductionDb = 0;
        const auto zero = render(graph, selected);
        near(rms(zero,sampleRate/2,sampleRate),rms(selected,sampleRate/2,sampleRate),1e-7,"Zero maximum reduction is transparent");
        std::cout << kind << " selected-band RMS ratio " << ratio << '\n';
    }
}
void transientsAndParameterEdits() {
    for (const char* kind : {"denoiser","deEsser","plosive","reverb"}) {
        auto graph = chain(kind);
        auto plan = compileGraph(graph);
        auto current = std::make_unique<GraphProcessor>(); current->prepare(plan);
        std::array<Frame,quantumFrames> input{}; GraphProcessor::Inputs views{}; views[0] = input;
        // Isolated clap-like impulses, low plosive bursts and short high-band
        // consonant bursts must all remain finite without turning into tails of
        // NaN or infinity. These synthetic artifacts are not recorded speech.
        bool finite = true;
        for (int block=0; block<1000; ++block) {
            for (std::size_t frame=0; frame<quantumFrames; ++frame) {
                const auto t=static_cast<std::size_t>(block)*quantumFrames+frame;
                double value = t%12000==0 ? .85 : 0;
                if (t%16000 < 2400) value += .35*sine(t,45);
                if (t%14000 < 1200) value += .2*sine(t,8000);
                input[frame] = {static_cast<float>(value),static_cast<float>(-.37*value)};
            }
            current->process(plan,views);
            for (auto value:current->output(1)) finite &= std::isfinite(value.left)&&std::isfinite(value.right);
        }
        require(finite,std::string(kind)+" remains stable through repeated clap-like, low-band and sibilant bursts");
        // Exercise both legal extremes during active processing, not just
        // initial construction. Existing state is cloned on each graph adoption.
        for (bool high : {true,false}) {
            graph.nodes[1].denoiser.suppressionDb = high ? 0 : -40;
            graph.nodes[1].deEsser = {high?12000.:2000., high?0.:-60., high?24.:0.};
            graph.nodes[1].plosive = {high?300.:60., high?0.:-60., high?24.:0.};
            graph.nodes[1].reverb = {high?1.:0., high?5.:.1, high?1.:0., high?100.:0.};
            const auto changed = compileGraph(graph); auto next = std::make_unique<GraphProcessor>(); next->prepare(changed);
            const auto beforeHeap = allocationOperations, beforeSpeex = am_speex_memory_operations();
            trackAllocations=true; next->inherit(*current,plan,changed);
            for (int block=0;block<50;++block) next->process(changed,views);
            trackAllocations=false;
            require(beforeHeap==allocationOperations&&beforeSpeex==am_speex_memory_operations(),"Live legal-extreme edits do not allocate in inheritance/configuration/processing");
            for(auto value:next->output(1)) require(std::isfinite(value.left)&&std::isfinite(value.right),"Extreme edited effect remains finite before sink saturation");
            current=std::move(next); plan=changed;
        }
        // Broken upstream values are sanitised by the graph boundary. Once
        // recovered/reset, no old speech or poisoned recursive state remains.
        input.fill({std::numeric_limits<float>::infinity(),std::numeric_limits<float>::quiet_NaN()});
        current->process(plan,views);current->reset();input.fill({});
        for(int block=0;block<10;++block)current->process(plan,views);
        near(current->output(2).back().left,0,0,"Non-finite upstream input cannot survive recovery as delayed content");
    }
}
void reverberation() {
    auto graph = chain("reverb"); graph.nodes[1].reverb = {1, .7, .2, 25};
    std::vector<Frame> impulse(sampleRate*4); impulse[2400] = {.3f, .3f};
    const auto tail = render(graph, impulse);
    const auto first = std::find_if(tail.begin(),tail.end(),[](Frame x){return std::abs(x.left)>1e-7;});
    require(first-tail.begin() >= 2400+1200+1215, "Wet reverb waits for selected pre-delay plus shortest reflection");
    require(rms(tail,6000,18000) > .00001, "Reverb creates a measurable tail after the impulse ends");
    require(rms(tail,sampleRate*3,sampleRate*4) < rms(tail,6000,18000)*.02, "Reverb tail decays stably toward silence");
    bool stereo{}; for(std::size_t i=5000;i<14000;++i) stereo |= std::abs(tail[i].left-tail[i].right)>1e-6;
    require(stereo,"Different reflection lengths produce stereo reverb from mono excitation");
    graph.nodes[1].reverb.mix=0; const auto dry=render(graph,impulse);
    near(dry[2400].left,.3,1e-7,"Zero reverb mix is exact undelayed dry audio");
    graph.nodes[1].reverb.mix=1; graph.nodes[1].bypass=true; const auto bypass=render(graph,impulse);
    near(bypass[2400].left,.3,1e-7,"Reverb bypass stays undelayed and removes the tail");
    near(rms(bypass,5000,10000),0,0,"Reverb bypass emits no old wet tail");
}
void stateAndRealtime() {
    for(const char* kind:{"denoiser","deEsser","plosive","reverb","expander","gate"}) {
        auto graph=chain(kind); const auto plan=compileGraph(graph);
        auto a=std::make_unique<GraphProcessor>(),b=std::make_unique<GraphProcessor>(); a->prepare(plan);b->prepare(plan);
        std::array<Frame,quantumFrames> input{};GraphProcessor::Inputs views{};views[0]=input;
        for(int block=0;block<70;++block){for(std::size_t i=0;i<input.size();++i)input[i]={static_cast<float>(.1*sine(block*quantumFrames+i,377)),.025f};a->process(plan,views);}
        const auto speexBefore=am_speex_memory_operations(); const auto heapBefore=allocationOperations;
        trackAllocations=true;b->inherit(*a,plan,plan);trackAllocations=false;
        require(allocationOperations==heapBefore&&am_speex_memory_operations()==speexBefore,"Adopting effect state does not allocate or free on the audio thread");
        bool identical=true,finite=true;
        trackAllocations=true;
        for(int block=0;block<12;++block){a->process(plan,views);b->process(plan,views);for(std::size_t i=0;i<quantumFrames;++i){auto x=a->output(2)[i],y=b->output(2)[i];identical&=x.left==y.left&&x.right==y.right;finite&=std::isfinite(x.left)&&std::isfinite(x.right);}}
        a->reset();b->reset();input.fill({});a->process(plan,views);b->process(plan,views);
        trackAllocations=false;
        require(identical&&finite,std::string(kind)+" parameter-only snapshot inheritance preserves all filter, spectrum and delay history exactly");
        require(allocationOperations==heapBefore&&am_speex_memory_operations()==speexBefore,"Processing/recovery uses zero C++ or Speex heap operations");
        near(a->output(2).back().left,0,0,"Recovery clears delayed speech and reverb history");
        auto bypass=graph;bypass.nodes[1].bypass=true;const auto bypassPlan=compileGraph(bypass);b->inherit(*a,plan,bypassPlan);
        input.fill({.1f,-.05f});for(int i=0;i<12;++i)b->process(bypassPlan,views);
        near(b->output(2).back().left,.1,1e-7,"Bypass settles to dry audio after its declared latency");
        bypass.nodes[1].muted=true;const auto mute=compileGraph(bypass);for(int i=0;i<3;++i)b->process(mute,views);
        near(b->output(2).back().left,0,0,"Bypass retains saved mute safety for new effects");
    }
}
void validationAndPersistence() {
    auto config=defaultConfiguration();config.graph=chain("denoiser");
    config.graph.nodes.push_back(node("de","deEsser"));config.graph.nodes.push_back(node("pl","plosive"));config.graph.nodes.push_back(node("rv","reverb"));
    config.graph.nodes[1].denoiser.suppressionDb=-27;config.graph.nodes[3].deEsser={7200,-32,14};config.graph.nodes[4].plosive={210,-30,18};config.graph.nodes[5].reverb={.36,2.4,.7,32};
    config.graph.nodes[1].eqBands[1].resetFrequencyHz=1234;
    const auto json=Json(config);const auto reopened=json.get<Configuration>();validateConfiguration(reopened);
    require(Json(reopened)==json,"All new effect controls and EQ reset anchors round-trip without changing legacy settings");
    auto old=json;for(auto& n:old["graph"]["nodes"]){n.erase("denoiser");n.erase("deEsser");n.erase("plosive");n.erase("reverb");for(auto& b:n["eqBands"])b.erase("resetFrequencyHz");}
    const auto compatible=old.get<Configuration>();near(compatible.graph.nodes[1].denoiser.suppressionDb,-18,0,"Missing denoiser settings use safe defaults");
    near(compatible.graph.nodes[1].eqBands[1].q,1,0,"Legacy EQ Q is retained, not silently broadened");near(compatible.graph.nodes[1].eqBands[1].resetFrequencyHz,0,0,"Missing legacy reset anchor remains unset");
    auto invalid=config.graph;invalid.nodes[1].denoiser.suppressionDb=1;rejects([&]{compileGraph(invalid);},"Invalid denoising range rejected");
    invalid=config.graph;invalid.nodes[1].deEsser.frequencyHz=1500;rejects([&]{compileGraph(invalid);},"Invalid de-esser range rejected");
    invalid=config.graph;invalid.nodes[1].plosive.reductionDb=25;rejects([&]{compileGraph(invalid);},"Invalid plosive reduction rejected");
    invalid=config.graph;invalid.nodes[1].reverb.decaySeconds=std::numeric_limits<double>::infinity();rejects([&]{compileGraph(invalid);},"Non-finite reverb settings rejected");
    invalid=config.graph;invalid.nodes[1].eqBands[1].resetFrequencyHz=1;rejects([&]{compileGraph(invalid);},"Invalid EQ reset anchor rejected");
}
void dedicatedGate() {
    auto legacy = chain("expander");
    legacy.nodes[1].expander = {"gate", -40, 7, 3, 17, 40, 48, 6};
    const auto savedLegacy = Json(legacy);
    auto graph = legacy; graph.nodes[1].kind = "gate";
    const auto legacyPlan = compileGraph(legacy), plan = compileGraph(graph);
    require(plan.nodes[1].kind == GraphKind::gate && AdvancedEffect::supports(GraphKind::gate)
        && !isSource(GraphKind::gate) && !isSink(GraphKind::gate),
        "Dedicated Gate compiles as its own processor kind using the existing gate algorithm");
    require(Json(legacy) == savedLegacy && Json(savedLegacy.get<AudioGraph>()) == savedLegacy,
        "Existing Expander records saved in Gate mode retain their exact kind and controls without migration");
    GraphChannels channels{}; channels[0] = 2;
    require(graphSignalChannels(plan, channels) == graphSignalChannels(legacyPlan, channels),
        "Dedicated Gate preserves the legacy gate's programme channel metadata");

    auto old = std::make_unique<GraphProcessor>(), current = std::make_unique<GraphProcessor>();
    old->prepare(legacyPlan); current->prepare(plan);
    std::array<Frame, quantumFrames> input{}; GraphProcessor::Inputs inputs{}; inputs[0] = input;
    bool identical = true, finite = true;
    const auto beforeHeap = allocationOperations;
    trackAllocations = true;
    for (std::size_t block = 0; block < 180; ++block) {
        // Cross the threshold in alternating stereo channels, including quiet
        // intervals long enough to exercise hold, hysteresis and closing gain.
        const bool voice = block % 60 < 20;
        for (std::size_t frame = 0; frame < quantumFrames; ++frame) {
            const auto sample = static_cast<float>((voice ? .08 : .0003) * sine(block * quantumFrames + frame, 377));
            input[frame] = block % 2 ? Frame{sample, -.25f * sample} : Frame{.25f * sample, -sample};
        }
        old->process(legacyPlan, inputs); current->process(plan, inputs);
        for (std::size_t frame = 0; frame < quantumFrames; ++frame) {
            const auto first = old->output(2)[frame], second = current->output(2)[frame];
            identical &= first.left == second.left && first.right == second.right;
            finite &= std::isfinite(second.left) && std::isfinite(second.right);
        }
    }
    input.fill({.0003f, -.00015f});
    for (int block = 0; block < 100; ++block) { old->process(legacyPlan, inputs); current->process(plan, inputs); }
    trackAllocations = false;
    require(identical && finite, "Dedicated Gate is bit-exact with legacy gate DSP across stereo threshold/hold/closing transitions");
    require(allocationOperations == beforeHeap, "Dedicated Gate processing performs no C++ heap operations");
    require(current->meter(1).gainReductionDb > 40 && current->meter(1).detectorDb < -40,
        "Dedicated Gate publishes actual gain-reduction and detector telemetry for quiet programme audio");
    near(current->meter(1).gainReductionDb, old->meter(1).gainReductionDb, 0,
        "Dedicated and legacy Gate share exact gain-reduction telemetry");

    auto inherited = std::make_unique<GraphProcessor>(); inherited->prepare(plan);
    const auto inheritHeap = allocationOperations;
    trackAllocations = true;
    inherited->inherit(*current, plan, plan);
    current->process(plan, inputs); inherited->process(plan, inputs);
    bool sameHistory = true;
    for (std::size_t frame = 0; frame < quantumFrames; ++frame)
        sameHistory &= current->output(2)[frame].left == inherited->output(2)[frame].left
            && current->output(2)[frame].right == inherited->output(2)[frame].right;
    trackAllocations = false;
    require(sameHistory && allocationOperations == inheritHeap,
        "Gate snapshot adoption retains exact detector/hold/envelope history without allocating");
    graph.nodes[1].bypass = true; const auto bypass = compileGraph(graph);
    auto dry = std::make_unique<GraphProcessor>(); dry->prepare(bypass); dry->inherit(*current, plan, bypass);
    input.fill({.1f, -.05f});
    for (int block = 0; block < 3; ++block) dry->process(bypass, inputs);
    near(dry->output(2).back().left, .1, 1e-7, "Dedicated Gate bypass returns undelayed dry programme audio");
    graph.nodes[1].muted = true; const auto muted = compileGraph(graph);
    for (int block = 0; block < 3; ++block) dry->process(muted, inputs);
    near(dry->output(2).back().left, 0, 0, "Dedicated Gate bypass preserves explicit mute safety");

    auto config = defaultConfiguration(); config.graph = graph;
    const auto document = Json(config); const auto reopened = document.get<Configuration>(); validateConfiguration(reopened);
    require(Json(reopened) == document && reopened.graph.nodes[1].kind == "gate"
        && reopened.graph.nodes[1].expander.mode == "gate",
        "Gate kind and every existing gate control survive configuration save/load exactly");
    auto invalid = graph; invalid.nodes[1].expander.mode = "expander";
    rejects([&] { compileGraph(invalid); }, "A dedicated Gate cannot silently run Expander mode");
    invalid = graph; invalid.nodes[1].expander.mode = "unknown";
    rejects([&] { compileGraph(invalid); }, "Gate rejects unknown processing modes");
    invalid = graph; invalid.nodes[1].expander.rangeDb = 81;
    rejects([&] { compileGraph(invalid); }, "Gate validates the existing attenuation range contract");
    invalid = graph; invalid.edges.push_back({"invalid-detector", "in", "fx", "sidechain"});
    rejects([&] { compileGraph(invalid); }, "Gate has no detector-only cable input");
}
void timing() {
    AudioGraph graph;graph.nodes.push_back(node("in","virtualInput"));std::string previous="in";
    for(int i=0;i<8;++i){const char* kind=std::array{"denoiser","deEsser","plosive","reverb"}[i%4];std::string id="effect"+std::to_string(i);graph.nodes.push_back(node(id,kind));graph.edges.push_back({"edge"+std::to_string(i),previous,id});previous=id;}
    graph.nodes.push_back(node("out","output"));graph.edges.push_back({"last",previous,"out"});
    const auto plan=compileGraph(graph);auto dsp=std::make_unique<GraphProcessor>();dsp->prepare(plan);
    std::array<Frame,quantumFrames> input;for(std::size_t i=0;i<input.size();++i)input[i]={static_cast<float>(.08*sine(i,220)),.04f};GraphProcessor::Inputs views{};views[0]=input;
    std::vector<double> timings;timings.reserve(2000);
    for(int block=0;block<2100;++block){const auto start=std::chrono::steady_clock::now();dsp->process(plan,views);const double us=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count();if(block>=100)timings.push_back(us);}
    std::sort(timings.begin(),timings.end());std::cout<<"Eight-effect stereo chain: median="<<timings[1000]<<" us; p99="<<timings[1980]<<" us; max="<<timings.back()<<" us; 5000 us quantum. Synthetic offline measurement, not live latency.\n";
    require(timings[1980]<5000,"Representative eight-effect stereo processing p99 stays below its 5 ms quantum");
}
void maximumGraphTiming() {
    for (const char* kind : {"denoiser","deEsser","plosive","reverb"}) {
        AudioGraph graph; graph.nodes.push_back(node("in","virtualInput")); std::string previous="in";
        for(int i=0;i<30;++i){std::string id="fx"+std::to_string(i);graph.nodes.push_back(node(id,kind));graph.edges.push_back({"e"+std::to_string(i),previous,id});previous=id;}
        graph.nodes.push_back(node("out","output"));graph.edges.push_back({"last",previous,"out"});
        const auto plan=compileGraph(graph);auto first=std::make_unique<GraphProcessor>(),second=std::make_unique<GraphProcessor>();first->prepare(plan);second->prepare(plan);
        std::array<Frame,quantumFrames> input;input.fill({.03f,-.01f});GraphProcessor::Inputs views{};views[0]=input;
        std::vector<double> processTimes,copyTimes,resetTimes;processTimes.reserve(300);copyTimes.reserve(300);resetTimes.reserve(30);
        for(int block=0;block<300;++block){
            const auto start=std::chrono::steady_clock::now();first->process(plan,views);
            const auto copied=std::chrono::steady_clock::now();second->inherit(*first,plan,plan);
            const auto done=std::chrono::steady_clock::now();
            processTimes.push_back(std::chrono::duration<double,std::micro>(copied-start).count());copyTimes.push_back(std::chrono::duration<double,std::micro>(done-copied).count());
            if(block%10==0){const auto reset=std::chrono::steady_clock::now();second->reset();resetTimes.push_back(std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-reset).count());}
        }
        std::sort(processTimes.begin(),processTimes.end());std::sort(copyTimes.begin(),copyTimes.end());std::sort(resetTimes.begin(),resetTimes.end());
        std::cout<<"Maximum32-node graph (30 "<<kind<<"): processing p99="<<processTimes[297]<<" us; inherit p99="<<copyTimes[297]<<" us; reset max="<<resetTimes.back()<<" us.\n";
        require(processTimes[297]+copyTimes[297]<5000,"Full legal graph processing plus state adoption fits the synthetic 5 ms quantum at p99");
        require(resetTimes.back()<5000,"Full legal graph recovery clears preallocated history within one synthetic quantum");
    }
}
}
int main(){try{validationAndPersistence();dedicatedGate();spectralReduction();splitBand();reverberation();stateAndRealtime();transientsAndParameterEdits();timing();maximumGraphTiming();std::cout<<"PASS: "<<checks<<" advanced-effect assertions; no audio devices, personal settings or live processes accessed.\n";return 0;}catch(const std::exception& e){trackAllocations=false;std::cerr<<"FAIL after "<<checks<<" checks: "<<e.what()<<'\n';return 1;}}
