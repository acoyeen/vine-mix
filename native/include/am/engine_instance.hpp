#pragma once
#include <windows.h>
#include <array>
#include <stdexcept>
#include <string>

namespace am {
// Keep the legacy lock for the whole process lifetime so either product name
// excludes the other before configuration or audio objects are constructed.
class EngineInstance {
public:
    explicit EngineInstance(const std::wstring& identity) {
        try {
            const std::array<std::wstring, 2> names{
                L"Local\\AudioMixer.Engine." + identity, L"Local\\VineMix.Engine." + identity};
            for (std::size_t i = 0; i < names.size(); ++i) {
                SetLastError(ERROR_SUCCESS);
                handles_[i] = CreateMutexW(nullptr, TRUE, names[i].c_str());
                const auto error = GetLastError();
                if (!handles_[i]) throw std::runtime_error("Cannot create the engine instance lock.");
                if (error == ERROR_ALREADY_EXISTS) {
                    CloseHandle(handles_[i]); handles_[i] = nullptr;
                    throw std::runtime_error("Another VineMix or Audio Mixer engine is already running for this Windows user.");
                }
            }
        } catch (...) { close(); throw; }
    }
    ~EngineInstance() { close(); }
    EngineInstance(const EngineInstance&) = delete;
    EngineInstance& operator=(const EngineInstance&) = delete;
private:
    void close() noexcept {
        for (auto& handle : handles_) if (handle) {
            ReleaseMutex(handle); CloseHandle(handle); handle = nullptr;
        }
    }
    std::array<HANDLE, 2> handles_{};
};
}
