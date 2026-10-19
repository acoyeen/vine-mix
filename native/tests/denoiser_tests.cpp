// Synthetic audio only. No devices, live engine, settings store or recording.
#include "am/advanced_effects.hpp"
#include "am/configuration.hpp"
#include <algorithm>
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

static bool tracking{};
static std::uint64_t heapOperations{};
void* operator new(std::size_t count) { if (tracking) ++heapOperations; if (auto p=std::malloc(count?count:1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t count) { return ::operator new(count); }
void operator delete(void* p) noexcept { if (tracking) ++heapOperations; std::free(p); }
void operator delete[](void* p) noexcept { ::operator delete(p); }
extern "C" std::uint64_t am_speex_memory_operations(void);
extern "C" std::uint64_t am_rnnoise_memory_operations(void);
namespace {
using namespace am;
int checks{};
void require(bool ok, const char* message) { ++checks; if (!ok) throw std::runtime_error(message); }
bool equal(Frame a, Frame b) { return a.left==b.left && a.right==b.right; }
double sine(std::size_t i, double hz) { return std::sin(2*std::numbers::pi*hz*i/sampleRate); }
template<class F> void rejects(F action, const char* message) { bool bad{}; try { action(); } catch (const std::exception&) { bad=true; } require(bad,message); }
AudioGraph chain() {
    AudioGraph graph;
    for (const auto& pair : {std::pair{"in","virtualInput"}, {"fx","denoiser"}, {"out","output"}}) {
        GraphNode n; n.id=n.name=pair.first; n.kind=pair.second; graph.nodes.push_back(n);
    }
    graph.nodes[1].denoiser.algorithm="rnnoise";
    graph.edges={{"a","in","fx"},{"b","fx","out"}}; return graph;
}
std::vector<Frame> render(const AudioGraph& graph, const std::vector<Frame>& input) {
    const auto plan=compileGraph(graph); auto processor=std::make_unique<GraphProcessor>(); processor->prepare(plan);
    std::vector<Frame> output; output.reserve(input.size()); GraphProcessor::Inputs views{};
    for (std::size_t i=0;i<input.size();i+=quantumFrames) {
        views[0]=std::span(input).subspan(i,quantumFrames); processor->process(plan,views);
        output.insert(output.end(),processor->output(2).begin(),processor->output(2).end());
    }
    return output;
}
double rms(const std::vector<Frame>& input, std::size_t from, bool right=false) {
    double energy{}; for (std::size_t i=from;i<input.size();++i) { double value=right?input[i].right:input[i].left; energy+=value*value; }
    return std::sqrt(energy/(input.size()-from));
}
void audioAndDelay() {
    auto graph=chain(); std::mt19937 random(7513); std::uniform_real_distribution<float> noise(-.025f,.025f);
    std::vector<Frame> signal(sampleRate*3); for (auto& sample:signal) sample={noise(random),0};
    const auto wet=render(graph,signal);
    double ratio=rms(wet,sampleRate*2)/rms(signal,sampleRate*2);
    std::cout<<"RNNoise stationary-noise RMS ratio "<<ratio<<'\n';
    require(ratio<.5,"Embedded neural model actually reduces stationary noise");
    require(rms(wet,sampleRate,true)==0,"RNNoise keeps silent stereo channels isolated");
    graph.nodes[1].denoiser.rnnoiseMix=0; const auto dry=render(graph,signal);
    // The graph fades newly created sources/cables in for 10 ms. Compare only
    // after that startup ramp and the declared delay have both elapsed.
    bool exact=true; double dryError{}; for(std::size_t i=2400;i<signal.size();++i) { exact &= equal(dry[i],signal[i-1440]); dryError=std::max(dryError,std::abs(static_cast<double>(dry[i].left)-signal[i-1440].left)); }
    std::cout << "RNNoise dry maximum error " << dryError << '\n';
    require(exact,"Zero neural mix preserves exact original float audio at 30 ms delay");
    graph.nodes[1].denoiser.rnnoiseMix=.35; const auto mixed=render(graph,signal); double error{};
    for(std::size_t i=1440;i<signal.size();++i)
        error=std::max(error,std::abs(static_cast<double>(mixed[i].left)-(dry[i].left+.35*(wet[i].left-dry[i].left))));
    require(error<1e-7,"Neural mix blends actual model output with aligned original audio");
    graph.nodes[1].bypass=true; const auto bypass=render(graph,signal);
    require(std::equal(bypass.begin(),bypass.end(),dry.begin(),equal),"Neural bypass matches the zero-mix dry path exactly");
    graph.nodes[1].bypass=false; graph.nodes[1].denoiser.rnnoiseMix=1;
    for (std::size_t at : {2400u,2401u,2639u}) {
        std::vector<Frame> pulse(sampleRate); pulse[at]={.5f,0}; const auto output=render(graph,pulse);
        auto peak=std::max_element(output.begin(),output.end(),[](Frame a,Frame b){return std::abs(a.left)<std::abs(b.left);});
        std::cout<<"RNNoise wet pulse delay "<<peak-output.begin()-at<<" frames\n";
        require(peak->left!=0 && peak-output.begin()==static_cast<std::ptrdiff_t>(at+1440),"Actual neural wet pulse confirms block-independent 30 ms delay");
    }
    require(AdvancedEffect::latencyFrames(GraphKind::denoiser,"rnnoise")==1440
        && AdvancedEffect::latencyFrames(GraphKind::denoiser)==960,"Algorithms report their measured delays");
    std::vector<Frame> hum(sampleRate*4);
    for(std::size_t i=0;i<hum.size();++i) hum[i]={static_cast<float>(.1*sine(i,180)+.03*sine(i,360)),0};
    const auto humming=render(graph,hum);
    require(std::all_of(humming.begin(),humming.end(),[](Frame x){return std::isfinite(x.left)&&std::isfinite(x.right);}),"Sustained humming remains numerically finite");
    std::cout<<"RNNoise sustained synthetic hum RMS ratio "<<rms(humming,sampleRate*3)/rms(hum,sampleRate*3)<<" (listening quality remains unqualified)\n";
}
void persistence() {
    auto config=defaultConfiguration(); config.graph=chain(); config.graph.nodes[1].denoiser.rnnoiseMix=.65;
    auto json=nlohmann::json(config); auto restored=json.get<Configuration>();
    require(restored.graph.nodes[1].denoiser==config.graph.nodes[1].denoiser,"Algorithm, mix and retained spectral reduction round-trip through settings");
    json["graph"]["nodes"][1]["denoiser"].erase("algorithm"); json["graph"]["nodes"][1]["denoiser"].erase("rnnoiseMix");
    auto legacy=json.get<Configuration>();
    require(legacy.graph.nodes[1].denoiser.algorithm=="speex" && legacy.graph.nodes[1].denoiser.suppressionDb==-18,"Existing denoisers retain Speex and their saved reduction");
    auto invalid=config.graph; invalid.nodes[1].denoiser.algorithm="unknown";
    rejects([&]{compileGraph(invalid);},"Unknown algorithm rejects before audio processing");
    for(double value:{-.01,1.01,std::numeric_limits<double>::quiet_NaN()}) {
        invalid=config.graph; invalid.nodes[1].denoiser.rnnoiseMix=value;
        rejects([&]{compileGraph(invalid);},"Out-of-range and non-finite neural mix rejects before processing");
    }
}
void realtimeAndSwitching() {
    AdvancedEffect a(GraphKind::denoiser), b(GraphKind::denoiser); GraphPlan::Node setting;
    setting.denoiser.algorithm="rnnoise";
    a.configure(setting); b.configure(setting);
    // Only a has trained recurrent/FFT/pitch history. A missing or incomplete
    // neural snapshot copy must fail the subsequent sample equality checks.
    for(std::size_t i=0;i<9600;++i)a.process({static_cast<float>(.1*sine(i,211)),static_cast<float>(.05*sine(i,373))});
    setting.denoiser.algorithm="speex";
    a.configure(setting); b.configure(setting);
    const auto heapBefore=heapOperations,speexBefore=am_speex_memory_operations(),neuralBefore=am_rnnoise_memory_operations();
    bool finite=true,identical=true; double maxJump{},previous{};
    tracking=true;
    for(std::size_t i=0;i<sampleRate;++i) {
        if(i==1200||i==9000||i==9450||i==25000)setting.denoiser.algorithm="rnnoise";
        if(i==9300||i==17000||i==40000)setting.denoiser.algorithm="speex";
        setting.denoiser.rnnoiseMix=i<32000?.6:1;
        a.configure(setting); b.configure(setting);
        if(i%997==0)b.inherit(a);
        const Frame input{static_cast<float>(.1*sine(i,211)),static_cast<float>(.05*sine(i,373))};
        const auto output=a.process(input),twin=b.process(input);
        identical &= equal(output.wet,twin.wet)&&equal(output.dry,twin.dry);
        finite &= std::isfinite(output.wet.left)&&std::isfinite(output.wet.right);
        maxJump=std::max(maxJump,std::abs(output.wet.left-previous)); previous=output.wet.left;
    }
    a.reset();b.reset();tracking=false;
    require(heapOperations==heapBefore&&am_speex_memory_operations()==speexBefore&&am_rnnoise_memory_operations()==neuralBefore,
        "Processing, switching, mix edits, snapshot adoption and recovery have zero C++/Speex/RNNoise heap operations");
    require(identical&&finite,"Rapid switching and copied DSP state yield identical finite stereo samples");
    require(maxJump<.08,"Algorithm changes avoid large discontinuities on continuous synthetic vocals");
    setting.denoiser.algorithm="rnnoise"; a.configure(setting); b.configure(setting);
    require(equal(a.process({}).wet,{}),"Recovery clears delayed neural and spectral history");
    AdvancedEffect fresh(GraphKind::denoiser); fresh.configure(setting); fresh.process({}); bool resetIdentical=true;
    for (std::size_t i=1;i<sampleRate/2;++i) {
        Frame input{static_cast<float>(.1*sine(i,241)),0};
        resetIdentical &= equal(a.process(input).wet,fresh.process(input).wet);
    }
    require(resetIdentical,"Reset neural state exactly matches a fresh initialized processor");
    std::vector<double> timings;timings.reserve(500);
    for(int block=0;block<500;++block) {
        auto start=std::chrono::steady_clock::now();
        for (std::size_t i=0;i<quantumFrames;++i) a.process({static_cast<float>(.1*sine(block*quantumFrames+i,211)),.04f});
        timings.push_back(std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count());
    }
    std::sort(timings.begin(),timings.end());std::cout<<"Stereo RNNoise processing p99 "<<timings[495]<<" us / 5000 us quantum\n";
    require(timings[495]<5000,"A stereo neural denoiser fits the realtime processing quantum at synthetic p99");
}
}
int main() {
    try {persistence();audioAndDelay();realtimeAndSwitching();std::cout<<"PASS: "<<checks<<" dual-denoiser assertions; synthetic audio only.\n";return 0;}
    catch(const std::exception& error){tracking=false;std::cerr<<"FAIL after "<<checks<<" assertions: "<<error.what()<<'\n';return 1;}
}
