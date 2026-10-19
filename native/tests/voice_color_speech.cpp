// File-only listening/measurement fixture. Uses production GraphProcessor but
// never constructs an engine, opens an endpoint, or reads personal settings.
#include "am/graph.hpp"
#include <nlohmann/json.hpp>
#include <algorithm>
#include <array>
#include <cmath>
#include <complex>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <numbers>
#include <stdexcept>
#include <vector>

namespace {
using namespace am;
using json = nlohmann::json;
using Audio = std::vector<Frame>;
double db(double amplitude) { return 20 * std::log10(std::max(1e-20, amplitude)); }
double energy(const Audio& x) { double e{}; for (auto s : x) e += (s.left*s.left+s.right*s.right)*.5; return e; }
double rms(const Audio& x) { return std::sqrt(energy(x)/x.size()); }
double peak(const Audio& x) { double p{}; for (auto s:x) p=std::max({p,std::abs(double(s.left)),std::abs(double(s.right))}); return p; }
Audio scale(Audio x, double gain) { for (auto& s:x) { s.left=float(s.left*gain); s.right=float(s.right*gain); } return x; }
std::uint16_t u16(std::istream& f) { unsigned char b[2]{}; f.read(reinterpret_cast<char*>(b),2); return b[0]|(b[1]<<8); }
std::uint32_t u32(std::istream& f) { unsigned char b[4]{}; f.read(reinterpret_cast<char*>(b),4); return b[0]|(b[1]<<8)|(b[2]<<16)|(b[3]<<24); }
Audio readWave(const std::filesystem::path& path) {
    std::ifstream f(path,std::ios::binary); char id[4]{}; f.read(id,4);
    if(std::string(id,4)!="RIFF") throw std::runtime_error("Input must be RIFF WAVE");
    u32(f); f.read(id,4); if(std::string(id,4)!="WAVE") throw std::runtime_error("Input must be WAVE");
    unsigned channels{}, rate{}, bits{}, format{}; std::vector<char> bytes;
    while(f.read(id,4)) { const auto size=u32(f); const std::string chunk(id,4);
        if(chunk=="fmt ") { format=u16(f); channels=u16(f); rate=u32(f); u32(f); u16(f); bits=u16(f); if(size>16) f.seekg(size-16,std::ios::cur); }
        else if(chunk=="data") { bytes.resize(size); f.read(bytes.data(),size); }
        else f.seekg(size,std::ios::cur);
        if(size%2) f.seekg(1,std::ios::cur);
    }
    if(format!=1||bits!=16||(channels!=1&&channels!=2)||rate!=sampleRate||bytes.empty())
        throw std::runtime_error("Fixture must be 48 kHz mono/stereo PCM16; generate with the supplied script");
    Audio audio(bytes.size()/(channels*2));
    const auto sample=[&](std::size_t offset) { const auto lo=static_cast<unsigned char>(bytes[offset]), hi=static_cast<unsigned char>(bytes[offset+1]); return static_cast<std::int16_t>(lo|(hi<<8))/32768.f; };
    for(std::size_t i=0;i<audio.size();++i) audio[i]={sample(i*channels*2),sample(i*channels*2+(channels==2?2:0))};
    return audio;
}
void writeWave(const std::filesystem::path& path,const Audio& audio) {
    std::ofstream f(path,std::ios::binary);
    const auto w16=[&](unsigned n) { const char b[]={char(n),char(n>>8)}; f.write(b,2); };
    const auto w32=[&](unsigned n) { const char b[]={char(n),char(n>>8),char(n>>16),char(n>>24)}; f.write(b,4); };
    f.write("RIFF",4); w32(unsigned(audio.size()*4+36)); f.write("WAVEfmt ",8); w32(16); w16(1); w16(2); w32(sampleRate); w32(sampleRate*4); w16(4); w16(16); f.write("data",4); w32(unsigned(audio.size()*4));
    for(auto s:audio) for(auto v:{s.left,s.right}) w16(static_cast<std::uint16_t>(std::int16_t(std::lround(std::clamp(v,-1.f,.999969f)*32768))));
}
AudioGraph chain(double tone,double character,bool compressorOnly=false) {
    AudioGraph g; GraphNode source; source.id="source"; source.kind="virtualInput";
    GraphNode effect; effect.id="voice"; effect.kind=compressorOnly?"compressor":"voiceColor"; effect.voiceColor={tone,character};
    if(compressorOnly) { effect.compressor.thresholdDb=-30; effect.compressor.ratio=6; effect.compressor.attackMs=3; effect.compressor.releaseMs=100; }
    g.nodes={source,effect}; g.edges={{"signal","source","voice"}}; return g;
}
Audio render(const AudioGraph& graph,const Audio& input) {
    const auto plan=std::make_unique<GraphPlan>(compileGraph(graph)); const auto processor=std::make_unique<GraphProcessor>(); processor->prepare(*plan);
    std::array<Frame,quantumFrames> buffer{}; GraphProcessor::Inputs views{}; views[0]=buffer;
    for(int i=0;i<10;++i) processor->process(*plan,views);
    Audio out(input.size());
    for(std::size_t offset=0;offset<input.size();offset+=quantumFrames) {
        buffer={}; const auto n=std::min(quantumFrames,input.size()-offset); std::copy_n(input.begin()+offset,n,buffer.begin());
        processor->process(*plan,views); std::copy_n(processor->output(1).begin(),n,out.begin()+offset);
    }
    return out;
}
void fft(std::array<std::complex<double>,2048>& x) {
    constexpr std::size_t N=2048;
    for(std::size_t i=1,j=0;i<N;++i) { std::size_t bit=N>>1; for(;j&bit;bit>>=1) j^=bit; j^=bit; if(i<j) std::swap(x[i],x[j]); }
    for(std::size_t len=2;len<=N;len<<=1) { const auto step=std::polar(1.,-2*std::numbers::pi/len);
        for(std::size_t i=0;i<N;i+=len) { std::complex<double> w=1; for(std::size_t j=0;j<len/2;++j) { const auto a=x[i+j],b=x[i+j+len/2]*w; x[i+j]=a+b; x[i+j+len/2]=a-b; w*=step; } }
    }
}
std::array<double,4> bands(const Audio& audio) {
    std::array<double,4> e{}; constexpr std::array<double,5> edge{70,250,1000,4500,14000};
    for(std::size_t offset=0;offset+2048<=audio.size();offset+=1024) {
        std::array<std::complex<double>,2048> spectrum{};
        for(std::size_t i=0;i<2048;++i) spectrum[i]=(audio[offset+i].left+audio[offset+i].right)*.5*(.5-.5*std::cos(2*std::numbers::pi*i/2047));
        fft(spectrum);
        for(std::size_t i=1;i<=1024;++i) { const double hz=double(i)*sampleRate/2048; for(std::size_t b=0;b<4;++b) if(hz>=edge[b]&&hz<edge[b+1]) e[b]+=std::norm(spectrum[i]); }
    }
    return e;
}
json metrics(const Audio& input,const Audio& output) {
    const auto originalRms=rms(input), outputRms=rms(output); const auto matched=scale(output,originalRms/outputRms);
    double error{},dot{},width{}; for(std::size_t i=0;i<input.size();++i) {
        const double l=matched[i].left-input[i].left,r=matched[i].right-input[i].right;
        error+=(l*l+r*r)*.5; dot+=(input[i].left*matched[i].left+input[i].right*matched[i].right)*.5;
        const double side=(matched[i].left-matched[i].right)*.5; width+=side*side;
    }
    const auto before=bands(input),after=bands(matched); json shape=json::object(); const std::array<const char*,4> labels{"body_70_250Hz","low_mid_250_1000Hz","presence_1000_4500Hz","air_4500_14000Hz"};
    for(std::size_t b=0;b<4;++b) shape[labels[b]]=10*std::log10(std::max(after[b],1e-30)/std::max(before[b],1e-30));
    const double inCrest=db(peak(input)/originalRms),outCrest=db(peak(output)/outputRms);
    return {{"inputRmsDbfs",db(originalRms)},{"inputPeakDbfs",db(peak(input))},{"inputOverFullScale",peak(input)>1},{"outputRmsDbfs",db(outputRms)},{"outputPeakDbfs",db(peak(output))},{"inputCrestDb",inCrest},{"outputCrestDb",outCrest},{"crestReductionDb",inCrest-outCrest},{"matchedResidualPercent",100*std::sqrt(error/energy(input))},{"matchedCorrelation",dot/energy(input)},{"matchedStereoSidePercent",100*std::sqrt(width/energy(input))},{"matchedBandChangeDb",shape}};
}
}
int main(int argc,char** argv) {
    try {
        if(argc==6&&std::string(argv[1])=="--compare") {
            std::array<Audio,3> clips{readWave(argv[2]),readWave(argv[3]),readWave(argv[4])};
            double largest{}; for(auto& clip:clips) { clip=scale(clip,std::pow(10.,-27./20)/rms(clip)); largest=std::max(largest,peak(clip)); }
            const double gain=std::min(std::pow(10.,6./20),.85/largest);
            Audio comparison; json segments=json::array(); const std::array<const char*,3> names{"Dry input","Previous Voice Colour","Revised Voice Colour"};
            for(std::size_t i=0;i<clips.size();++i) {
                const auto clip=scale(clips[i],gain); segments.push_back({{"label",names[i]},{"startSeconds",double(comparison.size())/sampleRate},{"durationSeconds",double(clip.size())/sampleRate},{"rmsDbfs",db(rms(clip))},{"peakDbfs",db(peak(clip))}});
                comparison.insert(comparison.end(),clip.begin(),clip.end()); if(i+1<clips.size()) comparison.insert(comparison.end(),std::size_t(sampleRate*.8),Frame{});
            }
            const auto path=std::filesystem::path(argv[5]); writeWave(path,comparison);
            json info={{"file",path.string()},{"segments",segments},{"matching","Each segment has equal stereo RMS, then the same gain is applied to all three; peak remains <= 0.85. No playback occurred."},{"fixture","Generated speech, clean source -> Voice Colour -> output. Settings: tone=0, character=1. Source level -24dBFS RMS; output audition levels matched."}};
            auto reportPath=path; reportPath.replace_extension("json"); std::ofstream(reportPath)<<info.dump(2)<<'\n';
            std::cout<<"Wrote dry/previous/revised comparison: "<<path.string()<<'\n'; return 0;
        }
        if(argc!=4) throw std::runtime_error("usage: VoiceColorSpeech source.wav output-directory revision-label");
        const auto source=readWave(argv[1]); const auto directory=std::filesystem::path(argv[2]); std::filesystem::create_directories(directory);
        struct Setting { const char* name; double tone,character; };
        constexpr Setting settings[]={{"neutral",0,0},{"warm",-1,0},{"bright",1,0},{"broadcast_middle",0,.5},{"broadcast_max",0,1},{"warm_broadcast",-1,1},{"bright_broadcast",1,1}};
        json report={{"revision",argv[3]},{"source",argv[1]},{"sampleRate",sampleRate},{"durationSeconds",double(source.size())/sampleRate},{"normalization","Whole-clip stereo RMS; listening files independently matched to -27 dBFS RMS. Band changes compare that same RMS-matched output to input. Synthetic TTS fixture, not an audition or human mic recording."},{"cases",json::array()}};
        for(bool precompressed:{false,true}) {
            auto prepared=scale(source,std::pow(10.,-20./20)/rms(source));
            if(precompressed) prepared=render(chain(0,0,true),prepared);
            for(int level:{-36,-24,-12}) {
                const auto input=scale(prepared,std::pow(10.,level/20.)/rms(prepared));
                const auto prefix=std::string(precompressed?"compressed_":"plain_")+std::to_string(-level)+"db_";
                writeWave(directory/(prefix+"dry_matched.wav"),scale(input,std::pow(10.,-27./20)/rms(input)));
                for(auto setting:settings) {
                    const auto output=render(chain(setting.tone,setting.character),input);
                    if(!std::all_of(output.begin(),output.end(),[](Frame s){return std::isfinite(s.left)&&std::isfinite(s.right);})) throw std::runtime_error("Nonfinite speech output");
                    const auto file=prefix+setting.name; auto row=metrics(input,output); row["precompressed"]=precompressed; row["inputLevelDbfs"]=level; row["setting"]=setting.name; row["tone"]=setting.tone; row["character"]=setting.character;
                    row["rawWaveClips"]=peak(output)>1; row["matchedFile"]=file+"_matched.wav";
                    writeWave(directory/(file+"_raw.wav"),output);
                    const auto matched=scale(output,std::pow(10.,-27./20)/rms(output)); if(peak(matched)>=1) throw std::runtime_error("Listening normalization would clip: "+file+" peak "+std::to_string(peak(matched))+" crest "+std::to_string(db(peak(output)/rms(output))));
                    writeWave(directory/(file+"_matched.wav"),matched);
                    report["cases"].push_back(row);
                    if(setting.character==1&&setting.tone==0) std::cout<<prefix<<"broadcast: residual="<<row["matchedResidualPercent"]<<"%; crest reduction="<<row["crestReductionDb"]<<"dB; bands="<<row["matchedBandChangeDb"].dump()<<'\n';
                }
            }
        }
        std::ofstream(directory/"metrics.json")<<report.dump(2)<<'\n'; std::cout<<"Wrote "<<report["cases"].size()<<" cases, raw/matched WAVs, and metrics.json; no audio device opened.\n";
        return 0;
    } catch(const std::exception& error) { std::cerr<<error.what()<<'\n'; return 1; }
}
