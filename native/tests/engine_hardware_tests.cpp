// Opt-in shared-mode hardware qualification. All graph nodes are always muted.
// It has no control server and uses a new temporary configuration, so the user's
// desktop/engine and ordinary Windows playback remain independent.
#include "am/engine.hpp"
#include <windows.h>
#include <psapi.h>
#include <chrono>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <thread>
using namespace am;
using Clock=std::chrono::steady_clock;
void require(bool value,const char* text){if(!value)throw std::runtime_error(text);}
GraphNode& node(Configuration& c,const std::string& id){for(auto& n:c.graph.nodes)if(n.id==id)return n;throw std::runtime_error("Unknown node");}
Json path(const Json& snapshot,const std::string& id){for(const auto& n:snapshot.at("status").at("nodes"))if(n.at("id")==id)return n.at("path");throw std::runtime_error("Missing path");}
bool running(const Json& s,const std::string& id){return path(s,id).at("state")=="Running";}
void muted(const Configuration& c){for(const auto& n:c.graph.nodes)require(n.muted,"A hardware test node was not muted");}
int main(int argc,char** argv){
    Json result{{"test","Muted hardware worker and engine qualification"},{"passed",false},{"status","running"},{"checks",Json::array()},
        {"scope","Real shared-mode drivers, silence only, private temporary settings, no control pipe or Windows defaults changes. Missing assignments are not physical unplug events."}};
    std::string a1,a2,vb,vac,output;bool run=false;int seconds=30;int cycles=3;
    try{
        for(int i=1;i<argc;++i){const std::string key=argv[i];if(key=="--run-muted")run=true;else{
            require(i+1<argc,"Missing option value");const std::string value=argv[++i];
            if(key=="--a1")a1=value;else if(key=="--a2")a2=value;else if(key=="--vb-capture")vb=value;else if(key=="--vac-render")vac=value;
            else if(key=="--output")output=value;else if(key=="--seconds")seconds=std::stoi(value);else if(key=="--cycles")cycles=std::stoi(value);else throw std::runtime_error("Unknown option");
        }}
        require(run&&!a1.empty()&&!a2.empty()&&!vb.empty()&&!vac.empty()&&!output.empty(),"Explicit --run-muted, endpoint IDs and --output are required");
        require(seconds>=5&&seconds<=28800&&cycles>=0&&cycles<=30,"Invalid duration or cycle count");
        SetPriorityClass(GetCurrentProcess(),BELOW_NORMAL_PRIORITY_CLASS);
        const auto resultPath=std::filesystem::path(widen(output));std::filesystem::create_directories(resultPath.parent_path());
        const auto settings=resultPath.parent_path()/("muted-hardware-"+std::to_string(GetCurrentProcessId())+"-"+std::to_string(GetTickCount64())+".json");
        require(!std::filesystem::exists(settings),"Temporary test settings already exist");
        Configuration config=defaultConfiguration();for(auto& n:config.graph.nodes){n.muted=true;n.device={};n.app={};}
        node(config,"app-input").device.id=vb;node(config,"main-output-1").device.id=a1;node(config,"main-output-2").device.id=a2;node(config,"virtual-output").device.id=vac;
        muted(config);ConfigurationStore(settings).save(config);Engine engine(settings);engine.start();
        auto waitFor=[&](auto predicate,const char* reason){const auto start=Clock::now();Json s;do{
            s=engine.snapshot();if(predicate(s))return std::chrono::duration<double,std::milli>(Clock::now()-start).count();
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }while(Clock::now()-start<std::chrono::seconds(7));result["failedSnapshot"]=s.at("status");throw std::runtime_error(reason);};
        auto allReady=[](const Json& s){return running(s,"app-input")&&running(s,"main-output-1")&&running(s,"main-output-2")&&running(s,"virtual-output");};
        const double startupMs=waitFor(allReady,"Not all real assigned paths opened");result["startupMs"]=startupMs;
        result["checks"].push_back("VB capture and two physical outputs plus VAC render opened together in shared mode");
        const auto initial=engine.snapshot();result["initial"]=initial.at("status");
        auto commit=[&]{muted(config);engine.configure(config,config.revision);++config.revision;};
        result["reopenMs"]=Json::array();
        // Nonexistent endpoint IDs exercise actual WASAPI resolution failure,
        // preserving sibling streams. They never disable a Windows device.
        const std::string absent1="{0.0.0.00000000}.{ffffffff-ffff-ffff-ffff-fffffffffff1}";
        const std::string absent2="{0.0.0.00000000}.{ffffffff-ffff-ffff-ffff-fffffffffff2}";
        for(int i=0;i<cycles;++i){
            node(config,"main-output-1").device.id=absent1;commit();
            waitFor([](const Json& s){return path(s,"main-output-1").at("state")=="Waiting"&&running(s,"main-output-2")&&running(s,"virtual-output");},"First missing assignment interrupted sibling outputs");
            const auto before=engine.snapshot().at("status").at("audioFrames").get<std::uint64_t>();
            node(config,"main-output-2").device.id=absent2;commit();
            waitFor([&](const Json& s){return path(s,"main-output-2").at("state")=="Waiting"&&running(s,"virtual-output")&&s.at("status").at("audioFrames").get<std::uint64_t>()>before+4800;},"No-output timeline or virtual output stalled");
            node(config,"main-output-1").device.id=a1;node(config,"main-output-2").device.id=a2;commit();
            result["reopenMs"].push_back(waitFor(allReady,"Returning assignments did not reopen"));
        }
        if(cycles){result["checks"].push_back("Missing A1 and both missing A assignments left independent VAC and processing running");result["checks"].push_back("Real physical streams reopened after each restored assignment");}
        result["soakSecondsRequested"]=seconds;result["samples"]=Json::array();
        const auto soakStart=Clock::now();std::uint64_t previous{};std::uint64_t sampleIndex=0;
        const std::uint64_t sampleEvery=seconds>600?120:10; // bounded to 480 observations for an eight-hour run
        auto save=[&]{std::ofstream stream(resultPath,std::ios::binary|std::ios::trunc);stream<<result.dump(2)<<'\n';};
        while(Clock::now()-soakStart<std::chrono::seconds(seconds)){
            auto s=engine.snapshot();require(allReady(s),"An assigned real hardware path stopped running during the muted observation");
            const auto frames=s.at("status").at("audioFrames").get<std::uint64_t>();require(!previous||frames>previous,"Audio processing heartbeat stalled");previous=frames;
            for(const auto& n:s.at("status").at("nodes")){
                require(n.at("peak").get<double>()==0,"A muted graph emitted nonzero samples");
                require(n.at("path").at("queueMs").get<double>()<=200.1,"A worker queue exceeded its bounded capacity");
            }
            if(sampleIndex++%sampleEvery==0){PROCESS_MEMORY_COUNTERS_EX memory{};memory.cb=sizeof(memory);GetProcessMemoryInfo(GetCurrentProcess(),reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory));DWORD handles{};GetProcessHandleCount(GetCurrentProcess(),&handles);
                result["samples"].push_back({{"seconds",std::chrono::duration<double>(Clock::now()-soakStart).count()},{"audioFrames",frames},{"privateBytes",memory.PrivateUsage},{"handles",handles},{"paths",s.at("status").at("nodes")}});save();}
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
        }
        result["soakSecondsObserved"]=std::chrono::duration<double>(Clock::now()-soakStart).count();result["final"]=engine.snapshot().at("status");engine.stop();
        result["checks"].push_back("All node meters stayed zero, queues stayed bounded, heartbeat advanced through the timed observation");
        result["passed"]=true;result["status"]="complete";save();std::cout<<result.dump(2)<<'\n';return 0;
    }catch(const std::exception& error){result["status"]="failed";result["error"]=error.what();if(!output.empty()){std::ofstream stream(std::filesystem::path(widen(output)));stream<<result.dump(2)<<'\n';}std::cerr<<result.dump(2)<<'\n';return 1;}
}
