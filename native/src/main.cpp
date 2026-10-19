#include "am/configuration.hpp"
#include "am/control_server.hpp"
#include "am/engine.hpp"
#include <windows.h>
#include <chrono>
#include <atomic>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>

namespace { std::atomic<am::Engine*> currentEngine{nullptr};
SRWLOCK consoleLock = SRWLOCK_INIT;
BOOL WINAPI consoleControl(DWORD type) {
    if (type == CTRL_C_EVENT || type == CTRL_BREAK_EVENT || type == CTRL_CLOSE_EVENT || type == CTRL_SHUTDOWN_EVENT) {
        AcquireSRWLockShared(&consoleLock);
        if (auto* engine = currentEngine.load(std::memory_order_acquire)) engine->requestStop();
        ReleaseSRWLockShared(&consoleLock);
        return TRUE;
    }
    return FALSE;
}
struct ConsoleRegistration {
    explicit ConsoleRegistration(am::Engine& engine) {
        currentEngine.store(&engine, std::memory_order_release);
        SetConsoleCtrlHandler(consoleControl, TRUE);
    }
    ~ConsoleRegistration() {
        AcquireSRWLockExclusive(&consoleLock);
        currentEngine.store(nullptr, std::memory_order_release);
        ReleaseSRWLockExclusive(&consoleLock);
        SetConsoleCtrlHandler(consoleControl, FALSE);
    }
};
}
int wmain(int argc, wchar_t** argv) {
    SetConsoleOutputCP(CP_UTF8);
    try {
        std::filesystem::path settings;
        bool listDevices = false;
        for (int i = 1; i < argc; ++i) {
            const std::wstring option = argv[i];
            if (option == L"--list-devices") listDevices = true;
            else if (option == L"--settings" && i + 1 < argc) settings = argv[++i];
            else if (option == L"--help") {
                std::cout << "Audio Mixer Engine\n  --list-devices   Enumerate endpoints without opening audio streams\n"
                    "  --settings FILE Use a separate configuration file\n"; return 0;
            } else throw std::runtime_error("Unknown argument. Use --help.");
        }
        if (listDevices) {
            am::DeviceCatalog catalog;
            // Catalog enumeration runs on its COM owner thread. No capture or
            // playback stream is opened by this diagnostic command.
            std::this_thread::sleep_for(std::chrono::milliseconds(750));
            std::cout << am::Json(catalog.enumerate()).dump(2) << '\n'; return 0;
        }
        const auto mutexName = L"Local\\AudioMixer.Engine." + am::widen(am::currentUserSid());
        HANDLE instance = CreateMutexW(nullptr, TRUE, mutexName.c_str());
        if (!instance) throw std::runtime_error("Cannot create the engine instance lock.");
        if (GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(instance); return 0; }
        struct CloseInstance { HANDLE value; ~CloseInstance() { ReleaseMutex(value); CloseHandle(value); } } cleanup{instance};
        am::Engine engine(settings);
        ConsoleRegistration registration(engine);
        engine.start();
        am::ControlServer server(engine);
        server.run();
        engine.stop();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "Audio Mixer: " << error.what() << '\n'; return 1;
    }
}
