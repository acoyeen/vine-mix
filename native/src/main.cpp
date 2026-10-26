#include "am/configuration.hpp"
#include "am/control_server.hpp"
#include "am/engine.hpp"
#include "am/engine_instance.hpp"
#include "am/version.hpp"
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
        std::filesystem::path validation;
        bool listDevices = false;
        bool version = false;
        for (int i = 1; i < argc; ++i) {
            const std::wstring option = argv[i];
            if (option == L"--list-devices") listDevices = true;
            else if (option == L"--settings" && i + 1 < argc) settings = argv[++i];
            else if (option == L"--validate-settings" && i + 1 < argc) validation = argv[++i];
            else if (option == L"--version") version = true;
            else if (option == L"--help") {
                std::cout << "VineMix Engine\n  --list-devices   Enumerate endpoints without opening audio streams\n"
                    "  --settings FILE Use a separate configuration file\n"
                    "  --validate-settings FILE Validate settings without starting audio or writing files\n"
                    "  --version       Print the release version without loading settings\n"; return 0;
            } else throw std::runtime_error("Unknown argument. Use --help.");
        }
        if (version) { std::cout << "VineMix " << am::releaseVersion << '\n'; return 0; }
        if (!validation.empty()) {
            am::ConfigurationStore store(validation);
            const auto configuration = store.load();
            std::cout << am::Json{{"valid", store.loadState() == "loaded"}, {"settingsState", store.loadState()},
                {"warning", store.warning()}, {"version", am::releaseVersion}, {"revision", configuration.revision}}.dump() << '\n';
            return store.loadState() == "loaded" ? 0 : 2;
        }
        if (listDevices) {
            am::DeviceCatalog catalog;
            // Catalog enumeration runs on its COM owner thread. No capture or
            // playback stream is opened by this diagnostic command.
            std::this_thread::sleep_for(std::chrono::milliseconds(750));
            std::cout << am::Json(catalog.enumerate()).dump(2) << '\n'; return 0;
        }
        am::EngineInstance instance(am::widen(am::currentUserSid()));
        am::Engine engine(settings);
        ConsoleRegistration registration(engine);
        engine.start();
        am::ControlServer server(engine);
        server.run();
        engine.stop();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "VineMix: " << error.what() << '\n'; return 1;
    }
}
