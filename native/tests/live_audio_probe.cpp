// Explicitly opt-in, virtual-endpoint-only diagnostic. Never records a microphone,
// changes Windows defaults, persists mixer settings, or renders to physical audio.
// Captured samples exist in memory only; the output file contains metrics.
#include "am/windows_audio.hpp"
#include "am/process_capture.hpp"
#include "am/dsp.hpp"
#include <windows.h>
#include <mmsystem.h>
#include <nlohmann/json.hpp>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <numbers>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

using Json = nlohmann::json;
using Clock = std::chrono::steady_clock;
constexpr double rate = 48000;
constexpr float amplitude = 0.0039810717f; // -48 dBFS, always.
constexpr size_t chirpFrames = 12000;
double elapsed(Clock::time_point start) { return std::chrono::duration<double>(Clock::now()-start).count(); }
double clockSeconds(Clock::time_point time) { return std::chrono::duration<double>(time.time_since_epoch()).count(); }
std::filesystem::path utf8Path(const std::string& value) { return std::filesystem::path(std::u8string(reinterpret_cast<const char8_t*>(value.data()),value.size())); }
std::string executablePath() {
    std::wstring path(32768, L'\0');
    path.resize(GetModuleFileNameW(nullptr,path.data(),static_cast<DWORD>(path.size())));
    const int bytes = WideCharToMultiByte(CP_UTF8,0,path.data(),static_cast<int>(path.size()),nullptr,0,nullptr,nullptr);
    std::string result(bytes,'\0');
    WideCharToMultiByte(CP_UTF8,0,path.data(),static_cast<int>(path.size()),result.data(),bytes,nullptr,nullptr);
    return result;
}
Json pathJson(const am::PathState& state) {
    return {{"state",state.state},{"message",state.message},{"rate",state.rate},{"channels",state.channels},
        {"underruns",state.underruns},{"overruns",state.overruns},{"reconnects",state.reconnects},{"queueMs",state.queueMs}};
}
am::Frame signal(size_t frame) {
    if (frame < 96000 || frame >= 96000+chirpFrames) return {};
    const double t = (frame-96000)/rate;
    const double fade = std::min({1.0,t/.005,(.25-t)/.005});
    const double phaseL = 2*std::numbers::pi*(241*t+756*t*t/(2*.25));
    const double phaseR = 2*std::numbers::pi*(1223*t+678*t*t/(2*.25));
    return {static_cast<float>(amplitude*fade*std::sin(phaseL)),static_cast<float>(amplitude*fade*std::sin(phaseR))};
}
struct Capture {
    std::string label;
    std::shared_ptr<am::FrameQueue> queue = std::make_shared<am::FrameQueue>(32768);
    std::unique_ptr<am::AudioWorker> audio;
    std::unique_ptr<am::ProcessCaptureWorker> process;
    std::vector<am::Frame> samples;
    std::vector<double> receivedAt;
    bool observeOnly{};
    size_t observedFrames{}, nonfinite{};
    double firstArrival{}, lastArrival{}, peak{};
    am::PathState status() const { return audio ? audio->status() : process->status(); }
    bool ready() const { return audio ? audio->ready() : process->ready(); }
    void stop() { if(audio) audio->stop(); if(process) process->stop(); }
    void drain(Clock::time_point start, bool keep) {
        std::array<am::Frame,4096> chunk{};
        for (;;) {
            const size_t available = std::min(queue->size(),chunk.size());
            if (!available) break;
            const size_t count = queue->pop(std::span<am::Frame>(chunk.data(),available));
            if (!count) break;
            if(keep) {
                const double lastTime = elapsed(start);
                if(observeOnly) {
                    if(!observedFrames)firstArrival=lastTime-(count-1)/rate;
                    lastArrival=lastTime;observedFrames+=count;
                    for(size_t n=0;n<count;++n) {
                        const auto frame=chunk[n];
                        if(!std::isfinite(frame.left)||!std::isfinite(frame.right))++nonfinite;
                        peak=std::max({peak,std::abs(double(frame.left)),std::abs(double(frame.right))});
                    }
                } else {
                    for(size_t n=0;n<count;++n) { samples.push_back(chunk[n]); receivedAt.push_back(lastTime-(count-1-n)/rate); }
                }
            }
        }
    }
};
struct TimerPeriod {
    TimerPeriod() { timeBeginPeriod(1); }
    ~TimerPeriod() { timeEndPeriod(1); }
};
struct Match { double correlation{}, gain{}; size_t offset{}; double crossCorrelation{}; };
Match match(const std::vector<am::Frame>& samples, bool right) {
    Match best;
    auto channel=[right](am::Frame value) { return right?value.right:value.left; };
    size_t onset=0;
    while(onset<samples.size() && std::abs(channel(samples[onset]))<amplitude*.015f) ++onset;
    if(onset==samples.size() || samples.size()<chirpFrames+2000) return best;
    const size_t lo=onset>2000?onset-2000:0;
    const size_t hi=std::min(onset+2000,samples.size()-chirpFrames);
    std::array<double,chirpFrames/4> reference{};
    double referenceEnergy=0;
    for(size_t n=0;n<chirpFrames;n+=4) { const double value=channel(signal(n+96000)); reference[n/4]=value; referenceEnergy+=value*value; }
    for(size_t offset=lo;offset<=hi;++offset) {
        double dot=0,energy=0;
        for(size_t n=0;n<chirpFrames;n+=4) { const double value=channel(samples[offset+n]); dot+=reference[n/4]*value; energy+=value*value; }
        const double correlation=energy>1e-12 ? dot/std::sqrt(energy*referenceEnergy) : 0;
        if(correlation>best.correlation) { best={correlation,dot/referenceEnergy,offset,0}; }
    }
    double crossDot=0,crossEnergy=0;
    for(size_t n=0;n<chirpFrames;n+=4) { const double other=right?samples[best.offset+n].left:samples[best.offset+n].right; crossDot+=reference[n/4]*other; crossEnergy+=other*other; }
    best.crossCorrelation=crossEnergy>1e-12?crossDot/std::sqrt(crossEnergy*referenceEnergy):0;
    return best;
}
Json analyze(const Capture& capture,double injectionTime,double startClockSeconds,bool hasRenderer) {
    if(capture.observeOnly) {
        const double duration=capture.lastArrival-capture.firstArrival;
        return {{"label",capture.label},{"mode","passive virtual capture compatibility; no stimulus or signal quality assertion"},
            {"pass",capture.observedFrames>48000 && capture.nonfinite==0 && capture.queue->droppedFrames()==0},
            {"capturedFrames",capture.observedFrames},{"observedFrameRate",duration>0?(capture.observedFrames-1)/duration:0},
            {"peak",capture.peak},{"nonfiniteSamples",capture.nonfinite},{"queueDroppedFrames",capture.queue->droppedFrames()},{"path",pathJson(capture.status())}};
    }
    const Match left=match(capture.samples,false),right=match(capture.samples,true);
    double peak=0; size_t nonfinite=0;
    for(auto frame:capture.samples) { if(!std::isfinite(frame.left)||!std::isfinite(frame.right))++nonfinite; peak=std::max({peak,std::abs(double(frame.left)),std::abs(double(frame.right))}); }
    auto channel=[&](const Match& value) -> Json {
        return {{"correlation",value.correlation},{"linearGain",value.gain},{"gainDb",value.gain>0?20*std::log10(value.gain):-200.0},
            {"oppositeChannelCorrelation",value.crossCorrelation},{"arrivalClockSeconds",capture.receivedAt.empty()?0:capture.receivedAt[value.offset]+startClockSeconds},
            {"arrivalLatencyMs",!hasRenderer||capture.receivedAt.empty()?Json(nullptr):Json((capture.receivedAt[value.offset]-injectionTime)*1000)}};
    };
    const double duration=capture.receivedAt.size()>1?capture.receivedAt.back()-capture.receivedAt.front():0;
    return {{"label",capture.label},{"pass",left.correlation>.85 && right.correlation>.80 && nonfinite==0},
        {"left",channel(left)},{"right",channel(right)},{"capturedFrames",capture.samples.size()},
        {"observedFrameRate",duration>0?(capture.samples.size()-1)/duration:0},{"peak",peak},{"nonfiniteSamples",nonfinite},
        {"queueDroppedFrames",capture.queue->droppedFrames()},{"path",pathJson(capture.status())}};
}
int main(int argc,char** argv) {
    Json result={{"tool","AudioMixer.LiveAudioProbe"},{"version",1},{"processId",GetCurrentProcessId()},
        {"stimulusPeakDbFS",-48},{"latencyNote","Approximate application-queue submission to capture-drain arrival; includes diagnostic and transport buffering, not a calibrated acoustic measurement."}};
    std::string output;
    try {
        std::string renderId,captureId,startFile,readyFile,processPath;
        DWORD processTarget=0;
        int consumers=1; double seconds=6; bool run=false,list=false,renderOnly=false,captureOnly=false,observeOnly=false;
        for(int n=1;n<argc;++n) {
            const std::string key=argv[n];
            auto value=[&] { if(n+1>=argc)throw std::runtime_error("Missing value for "+key);return std::string(argv[++n]); };
            if(key=="--run")run=true;
            else if(key=="--list")list=true;
            else if(key=="--render")renderId=value();
            else if(key=="--capture")captureId=value();
            else if(key=="--consumers")consumers=std::stoi(value());
            else if(key=="--seconds")seconds=std::stod(value());
            else if(key=="--render-only")renderOnly=true;
            else if(key=="--capture-only")captureOnly=true;
            else if(key=="--observe-only")observeOnly=true;
            else if(key=="--process-target")processTarget=static_cast<DWORD>(std::stoul(value()));
            else if(key=="--process-path")processPath=value();
            else if(key=="--output")output=value();
            else if(key=="--start-file")startFile=value();
            else if(key=="--ready-file")readyFile=value();
            else throw std::runtime_error("Unknown option "+key);
        }
        if(!run&&!list)throw std::runtime_error("Use --list for read-only inventory, or --run --render <virtual ID> --capture <virtual ID> [--consumers 2] --output <JSON>. Process capture: separate --render-only process, then --capture-only --process-target <PID> --process-path <exact executable>. Physical endpoints are always refused.");
        if(renderOnly&&captureOnly)throw std::runtime_error("render-only and capture-only are mutually exclusive");
        if(observeOnly&&(!captureOnly||captureId.empty()||processTarget))throw std::runtime_error("observe-only requires capture-only and an explicit virtual capture endpoint; process targets are not allowed");
        if(processTarget&&processPath.empty())throw std::runtime_error("process-target requires exact process-path");
        if(processTarget&&(processTarget==GetCurrentProcessId()||!std::filesystem::equivalent(utf8Path(processPath),utf8Path(executablePath()))))
            throw std::runtime_error("Process capture is restricted to a separate synthetic probe process running this same executable");
        if(consumers<1||consumers>2||seconds<4||seconds>60)throw std::runtime_error("consumers must be 1 or 2; seconds must be between 4 and 60");
        am::DeviceCatalog catalog;
        std::vector<am::Endpoint> endpoints;
        for(int n=0;n<100;++n) { endpoints=catalog.enumerate(); if(!endpoints.empty())break;std::this_thread::sleep_for(std::chrono::milliseconds(50)); }
        if(list) {
            result["endpoints"]=Json::array();
            for(const auto& endpoint:endpoints)result["endpoints"].push_back({{"id",endpoint.id},{"name",endpoint.name},{"direction",endpoint.direction},{"state",endpoint.state},{"transport",endpoint.transport},{"rate",endpoint.rate},{"channels",endpoint.channels}});
        } else {
            TimerPeriod timerPeriod;
            auto requireVirtual=[&](const std::string& id,const std::string& direction) {
                auto found=std::find_if(endpoints.begin(),endpoints.end(),[&](auto& endpoint){return endpoint.id==id;});
                if(found==endpoints.end()||found->state!="Active"||found->direction!=direction||!found->isVirtual||(found->transport!="vbCable"&&found->transport!="vacLite"))
                    throw std::runtime_error("Refused: explicit active VB-CABLE/VAC virtual "+direction+" endpoint required");
                return *found;
            };
            am::Endpoint renderEndpoint;
            if(!captureOnly)renderEndpoint=requireVirtual(renderId,"render");
            am::Endpoint captureEndpoint;
            if(!captureId.empty())captureEndpoint=requireVirtual(captureId,"capture");
            else if(!processTarget&&!renderOnly)throw std::runtime_error("At least --capture or --process-target is required unless render-only");
            if(renderOnly&&(!captureId.empty()||processTarget))throw std::runtime_error("render-only cannot request capture");
            result["renderEndpoint"]={{"id",renderId},{"name",renderEndpoint.name}};
            result["captureEndpoint"]={{"id",captureId},{"name",captureEndpoint.name}};
            if(!readyFile.empty()) { std::ofstream file(utf8Path(readyFile));file<<Json({{"processId",GetCurrentProcessId()},{"executablePath",executablePath()},{"stage","validated; no audio streams opened"}}).dump(2); }
            if(!startFile.empty()) {
                const auto waitStart=Clock::now();
                while(!std::filesystem::exists(utf8Path(startFile))) {
                    if(elapsed(waitStart)>90)throw std::runtime_error("Start-file wait timed out without opening audio streams");
                    std::this_thread::sleep_for(std::chrono::milliseconds(50));
                }
            }
            std::vector<std::unique_ptr<Capture>> captures;
            for(int n=0;!captureId.empty()&&n<consumers;++n) {
                auto capture=std::make_unique<Capture>();capture->label="shared-capture-"+std::to_string(n+1);
                capture->observeOnly=observeOnly;
                capture->audio=std::make_unique<am::AudioWorker>(true,false,capture->queue,catalog,captureEndpoint.transport);
                capture->audio->configure({captureEndpoint.id,captureEndpoint.stableId,captureEndpoint.name,0});
                captures.push_back(std::move(capture));
            }
            if(processTarget) {
                auto capture=std::make_unique<Capture>();capture->label="process-loopback-"+std::to_string(processTarget);
                capture->process=std::make_unique<am::ProcessCaptureWorker>(capture->queue);
                capture->process->configure({processTarget,processPath,"Synthetic probe only"});
                captures.push_back(std::move(capture));
            }
            const auto openStart=Clock::now();
            while(!std::all_of(captures.begin(),captures.end(),[](auto& capture){return capture->ready();})) {
                for(auto& capture:captures)capture->drain(openStart,false);
                if(elapsed(openStart)>12) {
                    result["openingStatus"]=Json::array();for(auto& capture:captures)result["openingStatus"].push_back({{"label",capture->label},{"path",pathJson(capture->status())}});
                    throw std::runtime_error("Capture path did not become ready within 12 seconds; no stimulus rendered");
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
            if(!readyFile.empty()&&!captures.empty()) { std::ofstream file(utf8Path(readyFile));file<<Json({{"processId",GetCurrentProcessId()},{"executablePath",executablePath()},{"stage","capture-ready"}}).dump(2); }
            auto renderQueue=std::make_shared<am::FrameQueue>(32768);
            std::unique_ptr<am::AudioWorker> renderer;
            if(!captureOnly) {
                renderer=std::make_unique<am::AudioWorker>(false,false,renderQueue,catalog,renderEndpoint.transport);
                renderer->configure({renderEndpoint.id,renderEndpoint.stableId,renderEndpoint.name,0});
            }
            const auto renderStart=Clock::now();
            while(renderer&&!renderer->ready()) {
                for(auto& capture:captures)capture->drain(renderStart,false);
                if(elapsed(renderStart)>8)throw std::runtime_error("Virtual render path did not become ready");
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
            for(auto& capture:captures) { capture->queue->clear();capture->samples.reserve(static_cast<size_t>((seconds+1)*rate));capture->receivedAt.reserve(static_cast<size_t>((seconds+1)*rate)); }
            const auto start=Clock::now();
            double injectionTime=0;size_t produced=0;size_t feederDrops=0;
            std::array<am::Frame,240> chunk{};
            while(elapsed(start)<seconds) {
                const size_t desired=renderer?static_cast<size_t>(elapsed(start)*rate)+720:0;
                while(produced+chunk.size()<=desired) {
                    for(size_t n=0;n<chunk.size();++n)chunk[n]=signal(produced+n);
                    if(produced==96000)injectionTime=elapsed(start);
                    feederDrops+=chunk.size()-renderQueue->push(chunk);produced+=chunk.size();
                }
                for(auto& capture:captures)capture->drain(start,true);
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }
            if(renderer)result["renderPath"]=pathJson(renderer->status());result["renderFeederDroppedFrames"]=feederDrops;
            result["injectionTimeSeconds"]=injectionTime;result["injectionClockSeconds"]=renderer?Json(clockSeconds(start)+injectionTime):Json(nullptr);
            result["captures"]=Json::array();result["pass"]=feederDrops==0&&(!renderer||injectionTime>0);
            if(renderer)renderer->stop();for(auto& capture:captures)capture->stop();
            for(auto& capture:captures) { auto report=analyze(*capture,injectionTime,clockSeconds(start),renderer!=nullptr);result["pass"]=result["pass"].get<bool>()&&report["pass"].get<bool>();result["captures"].push_back(std::move(report)); }
        }
        if(!output.empty()){std::ofstream file(utf8Path(output));if(!file)throw std::runtime_error("Could not write result file");file<<result.dump(2);}
        std::cout<<result.dump(2)<<'\n';
        return result.value("pass",true)?0:2;
    } catch(const std::exception& error) {
        result["pass"]=false;result["error"]=error.what();
        if(!output.empty()){std::ofstream file(utf8Path(output));file<<result.dump(2);}
        std::cerr<<result.dump(2)<<'\n';return 1;
    }
}
