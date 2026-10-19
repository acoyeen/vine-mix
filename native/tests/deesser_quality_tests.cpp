// Offline quality probes only: no Windows endpoint, application or configuration access.
#include "am/advanced_effects.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <numbers>
#include <random>
#include <string>
#include <vector>

namespace {
using namespace am;
constexpr double pi = std::numbers::pi;
double sine(std::size_t n, double hz) { return std::sin(2*pi*hz*n/sampleRate); }
double db(double ratio) { return 20*std::log10(std::max(1e-20,ratio)); }
std::vector<Frame> process(GraphKind kind, const GraphPlan::Node& settings, const std::vector<Frame>& input) {
    AdvancedEffect effect(kind); effect.configure(settings);
    std::vector<Frame> output; output.reserve(input.size());
    for(auto f:input) output.push_back(effect.process(f).wet);
    return output;
}
double rms(const std::vector<Frame>& data, std::size_t begin, std::size_t end, bool right=false) {
    double sum{}; for(std::size_t n=begin;n<end;++n) { auto value=right?data[n].right:data[n].left; sum+=value*value; }
    return std::sqrt(sum/(end-begin));
}
double amplitude(const std::vector<Frame>& data, double hz, std::size_t begin, std::size_t end, bool right=false) {
    double real{},imag{};
    for(std::size_t n=begin;n<end;++n) { double value=right?data[n].right:data[n].left;
        const double phase=2*pi*hz*n/sampleRate; real+=value*std::cos(phase); imag+=value*std::sin(phase); }
    return 2*std::hypot(real,imag)/(end-begin);
}
double tone(GraphKind kind, GraphPlan::Node settings,double hz,double peak) {
    std::vector<Frame> input(sampleRate);
    for(std::size_t n=0;n<input.size();++n) input[n]={static_cast<float>(peak*sine(n,hz)),static_cast<float>(-.37*peak*sine(n,hz))};
    auto output=process(kind,settings,input);
    return db(rms(output,sampleRate/2,sampleRate)/rms(input,sampleRate/2,sampleRate));
}
int failures{},checks{};
void check(bool condition,const std::string& what) { ++checks; if(!condition){++failures;std::cout<<"QUALITY FAIL: "<<what<<'\n';} }
void sweep() {
    GraphPlan::Node settings;
    std::cout<<"De-esser default sweep: crossover=6000 Hz, threshold=-24 dBFS, max=9 dB, input peak=-14 dBFS\n";
    for(double hz:{200.,1000.,3000.,4000.,6000.,8000.,10000.,12000.,16000.,20000.})
        std::cout<<"  "<<hz<<" Hz => "<<tone(GraphKind::deEsser,settings,hz,.2)<<" dB\n";
    settings.deEsser={6000,-60,24};
    std::cout<<"De-esser maximum-range sweep: crossover=6000 Hz, threshold=-60 dBFS, max=24 dB, input peak=-14 dBFS\n";
    for(double hz:{1000.,3000.,6000.,8000.,10000.,12000.,16000.,20000.})
        std::cout<<"  "<<hz<<" Hz => "<<tone(GraphKind::deEsser,settings,hz,.2)<<" dB\n";
    settings={};
    std::cout<<"De-esser 8000 Hz level sensitivity at defaults\n";
    for(double level:{-42.,-36.,-30.,-24.,-18.,-12.,-6.})
        std::cout<<"  peak="<<level<<" dBFS => "<<tone(GraphKind::deEsser,settings,8000,std::pow(10,level/20))<<" dB\n";
    std::cout<<"Plosive default sweep: crossover=150 Hz, threshold=-24 dBFS, max=12 dB, input peak=-14 dBFS\n";
    for(double hz:{30.,60.,90.,120.,150.,200.,300.,1000.})
        std::cout<<"  "<<hz<<" Hz => "<<tone(GraphKind::plosive,settings,hz,.2)<<" dB\n";
}
void voicedBurst() {
    GraphPlan::Node settings;
    // Changing-amplitude harmonic vowel plus an explicitly known sibilance-band
    // cluster. This is a measurable voiced/consonant fixture, not recorded speech.
    const std::array<double,7> consonants{6000,7000,8000,9000,10000,11000,12000};
    std::vector<Frame> signal(sampleRate*2);
    for(std::size_t n=0;n<signal.size();++n) {
        const double voice=.10*sine(n,180)+.05*sine(n,360)+.025*sine(n,720);
        double consonant{};
        if(n>=sampleRate/2 && n<sampleRate) for(std::size_t k=0;k<consonants.size();++k)
            consonant+=.035*std::sin(2*pi*consonants[k]*n/sampleRate+.37*k*k);
        signal[n]={static_cast<float>(voice+consonant),static_cast<float>((voice+consonant)*.4)};
    }
    auto output=process(GraphKind::deEsser,settings,signal);
    double totalIn{},totalOut{};
    for(double hz:consonants){
        const auto before=amplitude(signal,hz,3*sampleRate/4,sampleRate),after=amplitude(output,hz,3*sampleRate/4,sampleRate);
        totalIn+=before*before;totalOut+=after*after;
    }
    std::cout<<"Voiced+sibilant burst at default: selected-band="<<10*std::log10(totalOut/totalIn)
        <<" dB, 180 Hz body="<<db(amplitude(output,180,3*sampleRate/4,sampleRate)/.10)<<" dB\n";
    for(double threshold:{-30.,-36.,-48.}){
        settings.deEsser.thresholdDb=threshold;output=process(GraphKind::deEsser,settings,signal);
        totalOut=0;for(double hz:consonants){auto value=amplitude(output,hz,3*sampleRate/4,sampleRate);totalOut+=value*value;}
        std::cout<<"  threshold="<<threshold<<" => selected-band="<<10*std::log10(totalOut/totalIn)<<" dB\n";
    }
    double linkError{};for(auto f:output)linkError=std::max(linkError,std::abs(static_cast<double>(f.right)-.4*f.left));
    check(linkError<1e-7,"linked stereo retains 0.4 channel ratio");
    settings.deEsser.reductionDb=0;output=process(GraphKind::deEsser,settings,signal);
    bool transparent=true;for(std::size_t n=0;n<signal.size();++n)transparent&=output[n].left==signal[n].left&&output[n].right==signal[n].right;
    check(transparent,"zero range is bit-exact dry audio");
}
void transientRecovery() {
    GraphPlan::Node settings;settings.deEsser={6000,-36,12};
    std::vector<Frame> input(sampleRate*2);
    for(std::size_t n=0;n<input.size();++n){
        const double peak=n>=sampleRate/2&&n<sampleRate/2+2400?.25:.005;
        input[n]={static_cast<float>(peak*sine(n,10000)),0};
    }
    const auto output=process(GraphKind::deEsser,settings,input);
    std::cout<<"10 kHz burst, peak=-12 dBFS, threshold=-36 dBFS, range=12 dB\n";
    for(int milliseconds:{0,1,2,5,10,20,40,50,60,80,100,150,250,500}){
        auto start=sampleRate/2+milliseconds*48;
        std::cout<<"  time="<<milliseconds<<" ms => "<<db(rms(output,start,start+240)/rms(input,start,start+240))<<" dB\n";
    }
    const std::size_t recovered=sampleRate/2+250*48; // 200 ms after the 50 ms burst ends.
    check(db(rms(output,recovered,recovered+240)/rms(input,recovered,recovered+240))>-.75,
        "quiet treble recovers to within 0.75 dB at 200 ms after the consonant ends");
}
void fricationBursts() {
    constexpr std::size_t taps=129;
    std::array<double,taps> kernel{};
    // A test-only linear FIR isolates the 6-12 kHz consonant band for both
    // synthesis and measurement. It is independent of the production detector.
    for(std::size_t n=0;n<taps;++n){
        const double offset=static_cast<double>(n)-64;
        const double ideal=offset==0?.25:(std::sin(pi*.5*offset)-std::sin(pi*.25*offset))/(pi*offset);
        kernel[n]=ideal*(.5-.5*std::cos(2*pi*n/(taps-1)));
    }
    auto filter=[&](const std::vector<Frame>& input){
        std::vector<Frame> filtered(input.size());
        for(std::size_t n=0;n<input.size();++n) for(std::size_t k=0;k<=std::min(n,taps-1);++k)
            filtered[n].left+=static_cast<float>(input[n-k].left*kernel[k]);
        return filtered;
    };
    std::mt19937 random(42917);std::uniform_real_distribution<float> uniform(-1,1);
    std::vector<Frame> raw(sampleRate*2);for(auto& f:raw)f.left=uniform(random);
    auto noise=filter(raw);const auto noiseRms=rms(noise,sampleRate/2,sampleRate);
    for(double targetRms:{.025,.05,.1}){
        std::vector<Frame> signal(sampleRate*2);
        for(std::size_t n=0;n<signal.size();++n){
            const double voice=.1*sine(n,180)+.05*sine(n,360)+.025*sine(n,720);
            const double consonant=n>=sampleRate/2&&n<sampleRate?noise[n].left*targetRms/noiseRms:0;
            signal[n]={static_cast<float>(voice+consonant),static_cast<float>(voice+consonant)};
        }
        const auto before=filter(signal);GraphPlan::Node settings;
        for(double threshold:{-24.,-30.,-36.}){
            settings.deEsser.thresholdDb=threshold;const auto output=process(GraphKind::deEsser,settings,signal);
            const auto after=filter(output);
            const double reduction=db(rms(after,3*sampleRate/4,sampleRate)/rms(before,3*sampleRate/4,sampleRate));
            std::cout<<"Seeded frication burst RMS="<<db(targetRms)<<" dBFS, threshold="<<threshold<<": selected-band="<<reduction
                <<" dB, body="<<db(amplitude(output,180,3*sampleRate/4,sampleRate)/.1)<<" dB\n";
            check(std::abs(db(amplitude(output,180,3*sampleRate/4,sampleRate)/.1))<.1,"de-essing noise-like consonants preserves voiced body");
        }
    }
}
void qualityRegressions() {
    GraphPlan::Node settings;settings.deEsser={6000,-60,24};settings.plosive={150,-60,24};
    // Frequency is the dynamic shelf midpoint. The max-range response must not
    // inherit the old complementary split's ~3 dB ceiling at this position.
    auto highCenter=tone(GraphKind::deEsser,settings,6000,.2);
    auto lowCenter=tone(GraphKind::plosive,settings,150,.2);
    check(highCenter<-10.5&&highCenter>-13.5,"24 dB de-esser range provides approximately 12 dB at shelf midpoint");
    check(lowCenter<-10.5&&lowCenter>-13.5,"24 dB plosive range provides approximately 12 dB at shelf midpoint");
    check(tone(GraphKind::deEsser,settings,12000,.2)<-17,"upper sibilance range no longer leaks through the old low crossover band");
    check(tone(GraphKind::plosive,settings,60,.2)<-17,"low plosive energy no longer leaks through the old high crossover band");
    check(tone(GraphKind::deEsser,settings,200,.2)>-.1,"strong de-essing preserves distant voice fundamentals");
    check(tone(GraphKind::plosive,settings,2000,.2)>-.1,"strong low-band reduction preserves distant voice harmonics");
    for(auto kind:{GraphKind::deEsser,GraphKind::plosive}){
        double previous=1;
        for(double amount:{0.,3.,6.,9.,12.,18.,24.}){
            settings.deEsser.reductionDb=settings.plosive.reductionDb=amount;
            const auto measured=tone(kind,settings,kind==GraphKind::deEsser?9000:70,.2);
            check(measured<=previous+.001,"increasing maximum reduction monotonically strengthens selected-band attenuation");
            previous=measured;
        }
        settings.deEsser.reductionDb=settings.plosive.reductionDb=24;
        previous=1;
        for(double threshold:{0.,-12.,-18.,-24.,-30.,-36.,-48.,-60.}){
            settings.deEsser.thresholdDb=settings.plosive.thresholdDb=threshold;
            const auto measured=tone(kind,settings,kind==GraphKind::deEsser?9000:70,.2);
            check(measured<=previous+.001,"lowering threshold monotonically strengthens selected-band attenuation");
            previous=measured;
        }
        settings.deEsser.thresholdDb=settings.plosive.thresholdDb=-24;
        const double below=tone(kind,settings,kind==GraphKind::deEsser?9000:70,.0001);
        check(std::abs(below)<1e-5,"quiet below-threshold input is transparent");
        settings.deEsser.thresholdDb=settings.plosive.thresholdDb=-60;
    }
}
void settingsAndStereoIsolation() {
    for(auto kind:{GraphKind::deEsser,GraphKind::plosive}) {
        GraphPlan::Node settings;settings.deEsser={6000,-36,12};settings.plosive={150,-36,12};
        AdvancedEffect effect(kind),reference(kind);effect.configure(settings);reference.configure(settings);
        const double selected=kind==GraphKind::deEsser?9000:70,opposite=kind==GraphKind::deEsser?180:2000;
        std::vector<Frame> linked(sampleRate),alone(sampleRate);
        for(std::size_t n=0;n<linked.size();++n){
            const float left=static_cast<float>(.25*sine(n,selected)),right=static_cast<float>(.1*sine(n,opposite));
            linked[n]=effect.process({left,right}).wet;alone[n]=reference.process({0,right}).wet;
        }
        check(std::abs(db(rms(linked,sampleRate/2,sampleRate,true)/rms(alone,sampleRate/2,sampleRate,true)))<.1,
            "loud target-band energy on left does not duck opposite-band voice on right");
        check(std::isfinite(effect.gainReductionDb())&&effect.gainReductionDb()>0&&effect.gainReductionDb()<=12.0001,
            "active reduction telemetry is finite, positive, and bounded by selected range");
        check(std::isfinite(effect.detectorDb())&&effect.detectorDb()>-36,
            "active detector telemetry exceeds the triggering threshold");
        double previous{},maxStep{},maxPeak{};bool finite=true;
        for(std::size_t n=0;n<sampleRate;++n){
            if(n%2400==0){
                const bool extreme=(n/2400)%2==0;
                settings.deEsser={extreme?12000.:2000.,extreme?-60.:0.,extreme?24.:0.};
                settings.plosive={extreme?300.:60.,extreme?-60.:0.,extreme?24.:0.};
                effect.configure(settings);
            }
            // Continuous material while changing every parameter over its whole
            // legal span. The natural sample derivative remains measurable.
            const float value=static_cast<float>(.15*sine(n,selected)+.1*sine(n,opposite));
            const auto output=effect.process({value,static_cast<float>(-.37*value)}).wet;
            finite&=std::isfinite(output.left)&&std::isfinite(output.right);
            maxStep=std::max(maxStep,std::abs(output.left-previous));previous=output.left;
            maxPeak=std::max(maxPeak,std::abs(static_cast<double>(output.left)));
        }
        std::cout<<(kind==GraphKind::deEsser?"De-esser":"Plosive")<<" legal-extreme edits: max sample step="<<maxStep<<", peak="<<maxPeak<<'\n';
        check(finite&&maxPeak<.4,"full-span live parameter changes remain bounded on continuous synthetic material");
        // Reset must erase all recursive detector and shelf history.
        effect.reset();effect.configure(settings);bool silent=true;
        for(int n=0;n<2400;++n){auto out=effect.process({}).wet;silent&=out.left==0&&out.right==0;}
        check(silent,"recovery reset erases old audio and dynamic detector state");
        check(effect.gainReductionDb()==0&&std::isfinite(effect.detectorDb())&&effect.detectorDb()<=-90,
            "silence clears dynamics telemetry after reset");
    }
}
}
int main(){std::cout<<std::fixed<<std::setprecision(3);sweep();voicedBurst();fricationBursts();transientRecovery();qualityRegressions();settingsAndStereoIsolation();
    std::cout<<"Completed "<<checks<<" invariants, "<<failures<<" failures; offline synthetic signals only.\n";return failures?1:0;}
