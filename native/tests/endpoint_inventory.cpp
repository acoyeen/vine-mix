// Read-only Core Audio inventory; does not initialize, stop, or route a stream.
#include <windows.h>
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <audiopolicy.h>
#include <functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>
#include <nlohmann/json.hpp>
#include <iostream>
#include <string>
using Microsoft::WRL::ComPtr;
using Json = nlohmann::json;
std::string utf8(const wchar_t* value) {
    if (!value) return {};
    const int size = WideCharToMultiByte(CP_UTF8,0,value,-1,nullptr,0,nullptr,nullptr);
    if (size <= 1) return {};
    std::string text(size,'\0'); WideCharToMultiByte(CP_UTF8,0,value,-1,text.data(),size,nullptr,nullptr); text.pop_back(); return text;
}
std::string id(IMMDevice* device) { LPWSTR text{}; if (FAILED(device->GetId(&text))) return {}; auto result=utf8(text); CoTaskMemFree(text); return result; }
int main() {
    const HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED); if (FAILED(init)) return 1;
    int result=0;
    {
        ComPtr<IMMDeviceEnumerator> enumerator;
        if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)))) return 2;
        Json report; report["defaults"]=Json::array(); report["endpoints"]=Json::array();
        for (auto flow:{eRender,eCapture}) for(auto role:{eConsole,eMultimedia,eCommunications}) {
            ComPtr<IMMDevice> device; const auto hr=enumerator->GetDefaultAudioEndpoint(flow,role,&device);
            report["defaults"].push_back({{"direction",flow==eRender?"render":"capture"},{"role",static_cast<int>(role)},{"id",SUCCEEDED(hr)?id(device.Get()):""},{"hresult",static_cast<unsigned long>(hr)}});
        }
        ComPtr<IMMDeviceCollection> collection;
        if (SUCCEEDED(enumerator->EnumAudioEndpoints(eAll,DEVICE_STATE_ACTIVE,&collection))) {
            UINT count{}; collection->GetCount(&count);
            for(UINT i=0;i<count;++i) {
                ComPtr<IMMDevice> device; if(FAILED(collection->Item(i,&device))) continue;
                Json item{{"id",id(device.Get())}};
                ComPtr<IMMEndpoint> endpoint; EDataFlow flow=eAll; if(SUCCEEDED(device.As(&endpoint))) endpoint->GetDataFlow(&flow);
                item["direction"]=flow==eRender?"render":"capture";
                ComPtr<IPropertyStore> properties;
                if(SUCCEEDED(device->OpenPropertyStore(STGM_READ,&properties))) { PROPVARIANT value; PropVariantInit(&value); if(SUCCEEDED(properties->GetValue(PKEY_Device_FriendlyName,&value))&&value.vt==VT_LPWSTR) item["name"]=utf8(value.pwszVal); PropVariantClear(&value); }
                ComPtr<IAudioEndpointVolume> volume;
                if(SUCCEEDED(device->Activate(__uuidof(IAudioEndpointVolume),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(volume.GetAddressOf())))) {
                    float scalar{}; BOOL mute{}; volume->GetMasterVolumeLevelScalar(&scalar); volume->GetMute(&mute); item["volume"]=scalar;item["muted"]=mute!=FALSE;
                }
                item["sessions"]=Json::array(); ComPtr<IAudioSessionManager2> manager;
                if(SUCCEEDED(device->Activate(__uuidof(IAudioSessionManager2),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(manager.GetAddressOf())))) {
                    ComPtr<IAudioSessionEnumerator> sessions;
                    if(SUCCEEDED(manager->GetSessionEnumerator(&sessions))) {int size{};sessions->GetCount(&size);for(int j=0;j<size;++j) {
                        ComPtr<IAudioSessionControl> control; if(FAILED(sessions->GetSession(j,&control)))continue;
                        ComPtr<IAudioSessionControl2> extended; if(FAILED(control.As(&extended)))continue;
                        DWORD pid{};AudioSessionState state{};extended->GetProcessId(&pid);control->GetState(&state);
                        Json entry{{"pid",pid},{"state",static_cast<int>(state)}}; ComPtr<ISimpleAudioVolume> sessionVolume;
                        if(SUCCEEDED(control.As(&sessionVolume))) {float scalar{};BOOL mute{};sessionVolume->GetMasterVolume(&scalar);sessionVolume->GetMute(&mute);entry["volume"]=scalar;entry["muted"]=mute!=FALSE;}
                        item["sessions"].push_back(entry);
                    }}
                }
                report["endpoints"].push_back(item);
            }
        } else result=3;
        std::cout<<report.dump(2)<<'\n';
    }
    CoUninitialize();return result;
}
