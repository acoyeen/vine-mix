#include "am/graph.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <complex>
#include <iomanip>
#include <iostream>
#include <memory>
#include <numbers>
#include <random>
#include <stdexcept>
#include <vector>

// Silent quality measurements, independent of hardware, settings and UI.
// The source is generated in memory; read the effect output before sink clipping.
namespace {
using namespace am;
int checks{}, failures{};
void check(bool ok, const std::string& text) {
    ++checks; if (!ok) { ++failures; std::cout << "FAIL " << text << '\n'; }
}
double db(double ratio) { return 20*std::log10(std::max(1e-20,ratio)); }
GraphNode node(const char* id,const char* kind) {
    GraphNode n; n.id=n.name=id;n.kind=kind;
    if(n.kind=="tone") n.eqBands=toneDefaults();
    if(n.kind=="highPass"||n.kind=="lowPass") n.eqBands={{n.kind,true,1000,0,.7071067811865476}};
    return n;
}
AudioGraph chain(const char* kind) {
    AudioGraph graph;graph.nodes={node("source","virtualInput"),node("effect",kind)};
    graph.edges={{"signal","source","effect"}};return graph;
}
std::vector<Frame> render(const AudioGraph& graph,const std::vector<Frame>& input) {
    auto plan=std::make_unique<GraphPlan>(compileGraph(graph));auto processor=std::make_unique<GraphProcessor>();processor->prepare(*plan);
    std::array<Frame,quantumFrames> buffer{};GraphProcessor::Inputs views{};views[0]=buffer;
    for(int i=0;i<20;++i)processor->process(*plan,views); // settle control ramps on silence
    std::vector<Frame> output(input.size());
    for(std::size_t offset=0;offset<input.size();offset+=quantumFrames) {
        buffer.fill({});std::copy_n(input.begin()+offset,std::min(quantumFrames,input.size()-offset),buffer.begin());
        processor->process(*plan,views);std::copy_n(processor->output(1).begin(),std::min(quantumFrames,input.size()-offset),output.begin()+offset);
    }
    return output;
}
std::vector<Frame> tone(double f,double amp,double seconds=1) {
    std::vector<Frame> result(static_cast<std::size_t>(seconds*sampleRate));
    for(std::size_t i=0;i<result.size();++i) {float x=static_cast<float>(amp*std::sin(2*std::numbers::pi*f*i/sampleRate));result[i]={x,x};}return result;
}
double rms(const std::vector<Frame>& samples,std::size_t first=0,bool right=false) {
    double energy=0;for(std::size_t i=first;i<samples.size();++i) {double x=right?samples[i].right:samples[i].left;energy+=x*x;}return std::sqrt(energy/(samples.size()-first));
}
double peak(const std::vector<Frame>& samples) {double result=0;for(auto x:samples)result=std::max({result,std::abs(static_cast<double>(x.left)),std::abs(static_cast<double>(x.right))});return result;}
double response(const AudioGraph& graph,double f) {auto in=tone(f,.01);auto out=render(graph,in);return db(rms(out,sampleRate/2)/rms(in,sampleRate/2));}
double maxError(const std::vector<Frame>& a,const std::vector<Frame>& b,std::size_t delay=0) {
    double result=0;for(std::size_t i=sampleRate/4+delay;i<a.size();++i)result=std::max({result,std::abs(static_cast<double>(a[i].left-b[i-delay].left)),std::abs(static_cast<double>(a[i].right-b[i-delay].right))});return result;
}
double harmonic(const std::vector<Frame>& samples,double f,std::size_t first) {
    std::complex<double> total;for(std::size_t i=first;i<samples.size();++i)total+=static_cast<double>(samples[i].left)*std::polar(1.,-2*std::numbers::pi*f*i/sampleRate);return 2*std::abs(total)/(samples.size()-first);
}
void equalization() {
    for(const char* kind:{"eq","tone"}) {
        auto graph=chain(kind);auto input=tone(997,.1);auto out=render(graph,input);
        check(maxError(out,input)<1e-7,std::string(kind)+" flat curve transparent");
    }
    auto eq=chain("eq");
    for(double gain:{-24.,-12.,6.,24.}) for(double f:{40.,1000.,12000.,19000.}) {
        eq.nodes[1].eqBands={{"peak",true,f,gain,1}};
        const auto measured=response(eq,f);check(std::abs(measured-gain)<.02,"Bell center gain calibrated");
    }
    for(const char* kind:{"highPass","lowPass"}) {
        auto graph=chain(kind);double center=response(graph,1000),stop=response(graph,std::string(kind)=="highPass"?100:10000);
        std::cout<<kind<<" cutoff="<<center<<" dB; stop-band="<<stop<<" dB\n";
        check(std::abs(center+3.0103)<.01,std::string(kind)+" Butterworth cutoff -3 dB");check(stop<-35,std::string(kind)+" 2-pole stop band");
    }
    eq.nodes[1].eqBands={{"notch",true,1000,0,1}};check(response(eq,1000)<-100,"Notch actually rejects its center");
    auto shelf=chain("eq");shelf.nodes[1].eqBands={{"lowShelf",true,1000,12,.7071067811865476}};
    check(std::abs(response(shelf,30)-12)<.01,"Low shelf reaches declared low-frequency gain");check(std::abs(response(shelf,19000))<.01,"Low shelf preserves treble");
    shelf.nodes[1].eqBands[0].type="highShelf";check(std::abs(response(shelf,19000)-12)<.01,"High shelf reaches declared high-frequency gain");check(std::abs(response(shelf,30))<.01,"High shelf preserves bass");
    auto toneGraph=chain("tone");toneGraph.nodes[1].eqBands[0].gainDb=6;toneGraph.nodes[1].eqBands[1].gainDb=-6;toneGraph.nodes[1].eqBands[2].gainDb=6;
    std::cout<<"Tone gains 30Hz="<<response(toneGraph,30)<<"; 1kHz="<<response(toneGraph,1000)<<"; 19kHz="<<response(toneGraph,19000)<<" dB\n";
    // Linear filters must not create harmonics on a sinusoid.
    eq.nodes[1].eqBands={{"peak",true,1000,12,1}};auto output=render(eq,tone(1000,.03));double fundamental=harmonic(output,1000,sampleRate/2),harmonicEnergy=0;
    for(int i=2;i<=10;++i)harmonicEnergy+=std::pow(harmonic(output,1000*i,sampleRate/2),2);
    std::cout<<"EQ THD="<<db(std::sqrt(harmonicEnergy)/fundamental)<<" dB\n";check(db(std::sqrt(harmonicEnergy)/fundamental)<-100,"EQ no added harmonic distortion at sensible headroom");
}
void compressorAndUtility() {
    auto graph=chain("compressor");graph.nodes[1].compressor={-18,4,10,120,0};
    auto input=tone(1000,.5,2);auto output=render(graph,input);double gain=db(rms(output,sampleRate)/rms(input,sampleRate));
    const double expected=(db(.5)+18)*(1./4-1);
    std::cout<<"Compressor expected peak-referred gain="<<expected<<" measured="<<gain<<" dB\n";
    check(std::abs(gain-expected)<1.5,"Compressor sine effective curve stays within measured detector-ballistics tolerance");
    const double fundamental=harmonic(output,1000,sampleRate);double harmonics=0;for(int i=2;i<=12;++i)harmonics+=std::pow(harmonic(output,1000*i,sampleRate),2);
    std::cout<<"Compressor sine THD="<<db(std::sqrt(harmonics)/fundamental)<<" dB\n";
    check(db(std::sqrt(harmonics)/fundamental)<-45,"Compressor default no strong steady harmonic distortion");
    graph.nodes[1].compressor.ratio=1;output=render(graph,input);check(maxError(output,input)<1e-7,"Compressor ratio one/makeup zero is transparent");
    graph.nodes[1].compressor={-18,4,10,120,0};auto quiet=tone(1000,.02);check(maxError(render(graph,quiet),quiet)<1e-7,"Compressor below threshold transparent");
    auto stereo=input;for(auto& x:stereo)x.right=-x.left*.25f;output=render(graph,stereo);double err=0;for(std::size_t i=sampleRate;i<output.size();++i)err=std::max(err,std::abs(static_cast<double>(output[i].right+.25f*output[i].left)));check(err<1e-7,"Compressor stereo linked");
    auto balance=chain("pan");balance.nodes[1].balance=1;auto p=render(balance,stereo);check(rms(p,sampleRate)<1e-10,"Balance right removes left");check(std::abs(rms(p,sampleRate,true)/rms(stereo,sampleRate,true)-1)<1e-7,"Balance right retains existing right, does not repan");
    auto mono=chain("mono");auto m=render(mono,stereo);check(std::abs(rms(m,sampleRate)/rms(input,sampleRate)-.375)<1e-7,"Mono averages, prevents double-amplitude sum");
    bool linked=true;for(auto x:m)linked&=x.left==x.right;check(linked,"Mono outputs identical stereo samples");
    for(auto& x:stereo)x.right=-x.left;check(rms(render(mono,stereo))==0,"Mono cancels exactly opposite-polarity inputs as arithmetic requires");
    for(double ratio:{2.,4.,20.})for(double threshold:{-30.,-18.,-6.}) {
        graph.nodes[1].compressor={threshold,ratio,10,120,0};std::vector<Frame> dc(sampleRate*2,{.75f,.375f});auto limited=render(graph,dc);
        const double target=.75*std::pow(.75/std::pow(10.,threshold/20),1/ratio-1);
        check(std::abs(limited.back().left-target)<1e-6,"Compressor exact settled static ratio over three thresholds/ratios");
    }
}
void denoise() {
    auto graph=chain("denoiser");graph.nodes[1].denoiser.suppressionDb=0;
    std::mt19937 random(119731);std::normal_distribution<float> noise(0,.03f);std::vector<Frame> input(sampleRate*3);for(auto& x:input)x={noise(random),noise(random)};
    auto output=render(graph,input);std::vector<Frame> error(output.size());for(std::size_t i=960;i<output.size();++i)error[i]={output[i].left-input[i-960].left,output[i].right-input[i-960].right};
    std::cout<<"Denoiser zero reduction noise residual="<<db(rms(error,sampleRate)/rms(input,sampleRate))<<" dB; max error="<<maxError(output,input,960)<<"\n";
    check(rms(error,sampleRate)/rms(input,sampleRate)<.001,"Denoiser zero reduction near transparent apart from PCM quantization");
    // Impulse latency must not depend on its location inside a 480-frame analysis block.
    for(std::size_t pos:{1200,1327,1439,1440,1679,1680}) {
        std::vector<Frame> pulse(sampleRate);pulse[pos]={.2f,0};auto y=render(graph,pulse);auto largest=std::max_element(y.begin(),y.end(),[](Frame a,Frame b){return std::abs(a.left)<std::abs(b.left);});
        check(static_cast<std::size_t>(largest-y.begin())==pos+960,"Denoiser block-independent 20 ms delay");check(rms(y,0,true)==0,"Denoiser no stereo crosstalk");
    }
    for(double reduction:{-12.,-18.,-30.,-40.}) {
        graph.nodes[1].denoiser.suppressionDb=reduction;auto noiseOut=render(graph,input);double attenuation=db(rms(noiseOut,sampleRate*2)/rms(input,sampleRate*2));
        std::cout<<"Denoiser steady noise requested="<<reduction<<" measured="<<attenuation<<" dB\n";
        check(attenuation<-7,"Denoiser removes measurable stationary noise");
    }
    graph.nodes[1].denoiser.suppressionDb=-18;auto music=tone(440,.1,4);auto musicOut=render(graph,music);std::cout<<"Denoiser sustained 440Hz attenuation="<<db(rms(musicOut,sampleRate*3)/rms(music,sampleRate*3))<<" dB (speech-only limitation)\n";
    // Strong attacks following silence should be preserved, not classified as a gate closure.
    std::vector<Frame> transient(sampleRate*3);for(std::size_t i=sampleRate;i<sampleRate+480;++i) {float x=static_cast<float>(.3*std::exp(-static_cast<double>(i-sampleRate)/120)*std::sin(2*std::numbers::pi*1000*i/sampleRate));transient[i]={x,x};}
    auto transientOut=render(graph,transient);std::cout<<"Denoiser isolated transient peak ratio="<<peak(transientOut)/peak(transient)<<"\n";check(peak(transientOut)/peak(transient)>.8,"Denoiser retains an isolated newly arriving transient");
}
void reverb() {
    auto graph=chain("reverb");graph.nodes[1].reverb={1,1.2,0,0};std::vector<Frame> impulse(sampleRate*8);impulse[0]={.1f,.1f};
    for(double decay:{.1,.7,1.2,5.}) {
        graph.nodes[1].reverb.decaySeconds=decay;auto output=render(graph,impulse);double inputEnergy=.01,energy=0;std::vector<double> cumulative(output.size());
        for(std::size_t i=output.size();i-->0;) {energy+=output[i].left*output[i].left;cumulative[i]=energy;}
        const auto end=std::find_if(cumulative.begin(),cumulative.end(),[&](double value){return value<=energy*1e-6;});double measured=static_cast<double>(end-cumulative.begin())/sampleRate;
        double lr=0,ll=0,rr=0;std::size_t nonzero=0;for(std::size_t i=sampleRate/10;i<sampleRate/2;++i) {auto x=output[i];lr+=x.left*x.right;ll+=x.left*x.left;rr+=x.right*x.right;nonzero+=std::abs(x.left)>1e-10;}
        std::cout<<"Reverb decay="<<decay<<"s: integrated impulse energy gain="<<10*std::log10(energy/inputEnergy)<<" dB; peak ratio="<<peak(output)/.1<<"; energy RT60="<<measured<<"s; stereo correlation="<<lr/std::sqrt(ll*rr)<<"; occupied samples="<<100.*nonzero/(sampleRate*.4)<<"%\n";
        check(peak(output)<1,"Reverb moderate impulse does not overload");check(measured<decay+.5,"Reverb decays within nominal plus initial-reflection tolerance");
        check(energy<=inputEnergy*1.001,"Reverb impulse wet energy cannot exceed input energy");
        if(decay>=.7)check(nonzero>sampleRate*.4*.9,"Reverb active tail becomes dense after 100 ms");
    }
    graph.nodes[1].reverb={1,1.2,.5,15};
    for(double f:{30.,80.,200.,440.,1000.,5000.,10000.}) {
        auto in=tone(f,.1,4),out=render(graph,in);const double gain=db(rms(out,sampleRate*3)/rms(in,sampleRate*3));std::cout<<"Reverb wet steady tone "<<f<<"Hz: gain="<<gain<<" dB; peak="<<peak(out)<<"\n";
        check(gain<=.001,"Normalised reverb cannot amplify a steady tone");
    }
    graph.nodes[1].reverb.mix=0;auto in=tone(997,.1);check(maxError(render(graph,in),in)<1e-7,"Reverb zero mix exactly dry");
    graph.nodes[1].bypass=true;graph.nodes[1].reverb.mix=1;check(maxError(render(graph,in),in)<1e-7,"Reverb bypass exactly dry");
    graph.nodes[1].bypass=false;graph.nodes[1].reverb={1,5,0,0};auto hot=tone(40,.5,8),out=render(graph,hot);std::cout<<"Reverb long-decay 40Hz input peak .5: output peak="<<peak(out)<<"\n";
    check(peak(out)<.75,"Long-decay low-frequency startup transient retains practical headroom");
    // Damping must reduce late high-frequency energy without growing any mode.
    auto undamped=render(graph,impulse);graph.nodes[1].reverb.damping=1;auto damped=render(graph,impulse);std::cout<<"Reverb maximum damping late RMS vs undamped="<<db(rms(damped,sampleRate)/rms(undamped,sampleRate))<<" dB\n";check(rms(damped,sampleRate)<rms(undamped,sampleRate),"Damping removes late tail energy");
}
void reverbExtremes() {
    auto graph=chain("reverb");
    constexpr std::array frequencies{1,5,10,20,25,30,40,50,63,80,100,125,160,200,250,315,400,500,630,800,1000,1250,1600,2000,2500,3150,4000,5000,6300,8000,10000,12500,16000,20000,23000};
    std::vector<Frame> signal(sampleRate*8);
    for(std::size_t i=0;i<signal.size();++i) {double value=0;for(int f:frequencies)value+=.001*std::sin(2*std::numbers::pi*f*i/sampleRate);signal[i]={static_cast<float>(value),static_cast<float>(value)};}
    for(double decay:{.1,.7,1.2,5.})for(double damping:{0.,.5,1.}) {
        graph.nodes[1].reverb={1,decay,damping,0};auto out=render(graph,signal);double highest=-1000;
        for(int f:frequencies) {double gain=db(harmonic(out,f,sampleRate*7)/harmonic(signal,f,sampleRate*7));highest=std::max(highest,gain);check(gain<.02,"Reverb full-range fixed-parameter frequency gain bounded by unity");}
        std::cout<<"Reverb full-range sweep decay="<<decay<<" damping="<<damping<<" highest transfer="<<highest<<" dB\n";
        check(peak(out)<.1,"Reverb multisine startup remains finite with headroom");
    }
    for(double pre:{0.,15.,100.}) {
        graph.nodes[1].reverb={1,1.2,.5,pre};std::vector<Frame> impulse(sampleRate);impulse[2400]={.1f,.1f};auto out=render(graph,impulse);
        auto first=std::find_if(out.begin(),out.end(),[](Frame f){return f.left!=0||f.right!=0;});auto position=static_cast<std::size_t>(first-out.begin());
        check(position==2400+1215+static_cast<std::size_t>(pre*48),"Reverb pre-delay preserves selected timing plus 25.3125 ms first reflection");
    }
    // Exercise audible parameter ramps on a running signal, preserving state as
    // a real graph edit does. This is separate from static stability checks.
    graph.nodes[1].reverb={.2,1.2,.5,15};auto plan=std::make_unique<GraphPlan>(compileGraph(graph));auto old=std::make_unique<GraphProcessor>();old->prepare(*plan);
    std::array<Frame,quantumFrames> input{};GraphProcessor::Inputs views{};views[0]=input;std::size_t frame=0;
    auto fill=[&] {for(auto& x:input) {double t=static_cast<double>(frame++)/sampleRate;x={static_cast<float>(.1*std::sin(2*std::numbers::pi*200*t)+.1*std::sin(2*std::numbers::pi*3000*t)),static_cast<float>(.15*std::sin(2*std::numbers::pi*437*t))};}};
    for(int i=0;i<500;++i){fill();old->process(*plan,views);}
    double maximum=0;
    for(const auto& settings:std::array{ReverbSettings{1,5,0,100},ReverbSettings{1,.1,1,0},ReverbSettings{0,5,0,100},ReverbSettings{.2,1.2,.5,15}}) {
        graph.nodes[1].reverb=settings;auto nextPlan=std::make_unique<GraphPlan>(compileGraph(graph));auto next=std::make_unique<GraphProcessor>();next->prepare(*nextPlan);next->inherit(*old,*plan,*nextPlan);
        bool finite=true;for(int block=0;block<100;++block){fill();next->process(*nextPlan,views);for(auto x:next->output(1)){finite&=std::isfinite(x.left)&&std::isfinite(x.right);maximum=std::max({maximum,std::abs(static_cast<double>(x.left)),std::abs(static_cast<double>(x.right))});}}
        check(finite,"Reverb legal-extreme live edits remain finite");old=std::move(next);plan=std::move(nextPlan);
    }
    std::cout<<"Reverb extreme live edit peak="<<maximum<<" for input <=.2\n";check(maximum<.6,"Reverb extreme edits do not produce severe level bursts");
    old->reset();input.fill({});bool silent=true;for(int block=0;block<1200;++block){old->process(*plan,views);for(auto x:old->output(1))silent&=x.left==0&&x.right==0;}check(silent,"Reverb recovery remains exactly silent for six seconds");
}
}
int main() {
    try {std::cout<<std::fixed<<std::setprecision(5);equalization();compressorAndUtility();denoise();reverb();reverbExtremes();std::cout<<"CHECKS "<<checks<<" FAILURES "<<failures<<"\n";return failures?1:0;}
    catch(const std::exception& e) {std::cerr<<e.what()<<'\n';return 2;}
}
