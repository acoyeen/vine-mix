#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <windows.h>
#include <sddl.h>
#include <array>
#include <cmath>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <vector>

namespace am {
namespace { constexpr std::size_t maximumSettingsBytes = 262144; }
void from_json(const Json& value, Configuration& config) {
    if (!value.is_object()) throw std::runtime_error("Settings must be a JSON object.");
    const auto version = value.at("version").get<int>();
    if (version != 1 && version != 2) throw std::runtime_error("This configuration version is not supported.");
    if (!value.at("microphones").is_array() || value.at("microphones").size() != 2
        || !value.at("mainOutputs").is_array() || value.at("mainOutputs").size() != 2
        || !value.at("sources").is_array() || value.at("sources").size() != 3)
        throw std::runtime_error("Settings require exactly two microphones, two A outputs, and three sources.");
    value.at("version").get_to(config.version); value.at("revision").get_to(config.revision);
    value.at("microphones").get_to(config.microphones); value.at("mainOutputs").get_to(config.mainOutputs);
    value.at("virtualInputCapture").get_to(config.virtualInputCapture);
    value.at("virtualInputPlayback").get_to(config.virtualInputPlayback);
    value.at("virtualOutputRender").get_to(config.virtualOutputRender);
    value.at("virtualOutputCapture").get_to(config.virtualOutputCapture);
    value.at("sources").get_to(config.sources); value.at("busA").get_to(config.busA); value.at("busB").get_to(config.busB);
    value.at("startAtLogin").get_to(config.startAtLogin);
    if (version == 1) migrateLegacyGraph(config);
    else value.at("graph").get_to(config.graph);
}

void migrateLegacyGraph(Configuration& config) {
    config.graph = {};
    const auto node = [&](const char* id, const char* kind, const char* name, double x, double y,
        const DeviceBinding& binding, float gain, bool muted) {
        GraphNode value; value.id = id; value.kind = kind; value.name = name; value.x = x; value.y = y;
        value.device = binding; value.gainDb = gain; value.muted = muted;
        config.graph.nodes.push_back(std::move(value));
    };
    // Match GraphDefaults.FromLegacy. Version-2 graphs bypass this layout factory.
    node("mic1", "microphone", "Microphone 1", 48, 60, config.microphones[0], 0, config.sources[0].muted);
    node("mic2", "microphone", "Microphone 2", 48, 390, config.microphones[1], 0, config.sources[1].muted);
    node("app-input", "virtualInput", "App Audio", 48, 720, config.virtualInputCapture, 0, config.sources[2].muted);
    node("mix-a", "mix", "What I hear", 448, 100, {}, config.busA.gainDb, config.busA.muted);
    node("mix-b", "mix", "What others hear", 448, 550, {}, config.busB.gainDb, config.busB.muted);
    node("main-output-1", "output", "Listening output 1", 848, 60, config.mainOutputs[0], 0, false);
    node("main-output-2", "output", "Listening output 2", 848, 390, config.mainOutputs[1], 0, false);
    node("virtual-output", "virtualOutput", "Virtual microphone", 848, 720, config.virtualOutputRender, 0, false);
    constexpr const char* ids[]{"mic1", "mic2", "app-input"};
    for (std::size_t i = 0; i < 3; ++i) {
        config.graph.edges.push_back({std::string(ids[i]) + "-a", ids[i], "mix-a", "audio", config.sources[i].a.gainDb, !config.sources[i].a.enabled});
        config.graph.edges.push_back({std::string(ids[i]) + "-b", ids[i], "mix-b", "audio", config.sources[i].b.gainDb, !config.sources[i].b.enabled});
    }
    config.graph.edges.push_back({"main-output-1-in", "mix-a", "main-output-1", "audio", 0, false});
    config.graph.edges.push_back({"main-output-2-in", "mix-a", "main-output-2", "audio", 0, false});
    config.graph.edges.push_back({"virtual-output-in", "mix-b", "virtual-output", "audio", 0, false});
    config.version = 2;
}
Configuration defaultConfiguration() {
    Configuration config;
    // New microphones capture stereo when supported. Loaded legacy settings
    // still pass through migrateLegacyGraph with their exact saved channel.
    for (auto& microphone : config.microphones) microphone.channel = -1;
    migrateLegacyGraph(config); return config;
}

std::wstring widen(const std::string& text) {
    if (text.empty()) return {};
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (!length) throw std::runtime_error("Invalid UTF-8 text.");
    std::wstring result(length, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), result.data(), length);
    return result;
}
std::string narrow(const std::wstring& text) {
    if (text.empty()) return {};
    const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
        text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (!length) return {};
    std::string result(length, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), result.data(), length, nullptr, nullptr);
    return result;
}
std::string currentUserSid() {
    HANDLE token{};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        throw std::runtime_error("Cannot determine the current Windows user.");
    DWORD bytes{};
    GetTokenInformation(token, TokenUser, nullptr, 0, &bytes);
    std::vector<unsigned char> storage(bytes);
    if (!GetTokenInformation(token, TokenUser, storage.data(), bytes, &bytes)) {
        CloseHandle(token);
        throw std::runtime_error("Cannot read the current Windows user.");
    }
    CloseHandle(token);
    LPWSTR sid{};
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(storage.data())->User.Sid, &sid))
        throw std::runtime_error("Cannot format the current Windows user.");
    const auto result = narrow(sid);
    LocalFree(sid);
    return result;
}
std::wstring pipeName() { return L"\\\\.\\pipe\\AudioMixer.Control." + widen(currentUserSid()); }
std::filesystem::path applicationDataDirectory() {
    const DWORD required = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (!required) throw std::runtime_error("LOCALAPPDATA is unavailable.");
    std::wstring value(required, L'\0');
    GetEnvironmentVariableW(L"LOCALAPPDATA", value.data(), required);
    value.resize(required - 1);
    return std::filesystem::path(value) / L"AudioMixer";
}
void validateConfiguration(const Configuration& config) {
    if (config.version != 2) throw std::runtime_error("This configuration version is not supported.");
    auto checkGain = [](float value) {
        if (!std::isfinite(value) || value < -60.f || value > 12.f)
            throw std::runtime_error("Volume must be between -60 and +12 dB.");
    };
    auto checkBinding = [](const DeviceBinding& device) {
        if (device.id.size() > 4096 || device.stableId.size() > 4096 || device.name.size() > 1024)
            throw std::runtime_error("The device identifier is too long.");
        if (device.channel < -1 || device.channel > 255)
            throw std::runtime_error("The selected microphone channel is invalid.");
        // Validate encoding before a worker can attempt to open a device.
        (void)widen(device.id); (void)widen(device.stableId); (void)widen(device.name);
    };
    for (const auto& source : config.sources) { checkGain(source.a.gainDb); checkGain(source.b.gainDb); }
    checkGain(config.busA.gainDb); checkGain(config.busB.gainDb);
    for (const auto& device : config.microphones) checkBinding(device);
    for (const auto& device : config.mainOutputs) checkBinding(device);
    checkBinding(config.virtualInputCapture); checkBinding(config.virtualInputPlayback);
    checkBinding(config.virtualOutputRender); checkBinding(config.virtualOutputCapture);
    const auto same = [](const DeviceBinding& a, const DeviceBinding& b) {
        return (!a.id.empty() && a.id == b.id) || (!a.stableId.empty() && a.stableId == b.stableId);
    };
    for (std::size_t i = 0; i < config.graph.nodes.size(); ++i) {
        const auto& node = config.graph.nodes[i];
        checkBinding(node.device); (void)widen(node.id); (void)widen(node.name); (void)widen(node.app.name);
        (void)widen(node.app.executablePath);
        if (node.app.name.size() > 1024 || node.app.executablePath.size() > 32768)
            throw std::runtime_error("The saved application identity is too long.");
        if (node.kind == "appInput" && node.app.processId && node.app.executablePath.empty())
            throw std::runtime_error("Select the application again so its executable identity can be saved safely.");
        // Input nodes may share capture while retaining independent processing.
        // Sinks remain exclusive: multiple writers would duplicate audio and
        // conflict with the direct virtual microphone's exclusive stream.
        for (std::size_t j = 0; j < i; ++j) {
            const auto& previous = config.graph.nodes[j];
            const bool render = node.kind == "output" || node.kind == "virtualOutput";
            const bool otherRender = previous.kind == "output" || previous.kind == "virtualOutput";
            if (render && otherRender && same(node.device, previous.device))
                throw std::runtime_error("This output device already has a node. Connect another cable to that node instead.");
        }
    }
    (void)compileGraph(config.graph);
}
ConfigurationStore::ConfigurationStore(std::filesystem::path path)
    : path_(path.empty() ? applicationDataDirectory() / L"settings.json" : std::move(path)) {}
Configuration ConfigurationStore::load() {
    // A store can be reloaded after external repair. Do not carry an old
    // recovery warning or invalid-primary flag into the next successful load.
    warning_.clear();
    primaryValid_ = true;
    legacySource_.clear();
    const auto read = [this](const std::filesystem::path& path) {
        if (std::filesystem::file_size(path) > maximumSettingsBytes) throw std::runtime_error("Settings file is too large.");
        std::ifstream stream(path, std::ios::binary);
        if (!stream) throw std::runtime_error("Cannot open settings.");
        const auto document = Json::parse(stream);
        auto configuration = document.get<Configuration>();
        validateConfiguration(configuration);
        if (document.at("version").get<int>() == 1) legacySource_ = path;
        return configuration;
    };
    auto backup = path_; backup += L".bak";
    try {
        // A missing primary is recoverable too (for example after an
        // interrupted replacement). Only a genuinely new store gets defaults.
        if (!std::filesystem::exists(path_) && !std::filesystem::exists(backup))
            return defaultConfiguration();
        return read(path_);
    }
    catch (const std::exception&) {
        primaryValid_ = false;
        try {
            auto configuration = read(backup);
            warning_ = "Settings were missing or damaged. The last good configuration was restored.";
            return configuration;
        } catch (const std::exception&) {
            warning_ = "Settings could not be read. Safe unassigned defaults are active; the original file was preserved.";
            return defaultConfiguration();
        }
    }
}
void ConfigurationStore::save(const Configuration& configuration) {
    validateConfiguration(configuration);
    // A successful commit must also be readable on the next launch. Individual
    // string limits alone do not bound the size of a complete graph document.
    const auto text = Json(configuration).dump(2) + "\n";
    if (text.size() > maximumSettingsBytes)
        throw std::runtime_error("Settings are too large to save. Shorten saved application or device details.");
    // A bare --settings filename lives in the current directory, whose empty
    // parent path is not a directory that create_directories can create.
    if (const auto parent = path_.parent_path(); !parent.empty())
        std::filesystem::create_directories(parent);
    if (!legacySource_.empty()) {
        auto preserved = path_; preserved += L".v1.bak";
        if (!std::filesystem::exists(preserved) && !CopyFileW(legacySource_.c_str(), preserved.c_str(), TRUE))
            throw std::runtime_error("Cannot preserve the original mixer settings before upgrading the canvas.");
    }
    auto temporary = path_; temporary += L".new";
    auto backup = path_; backup += L".bak";
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot save settings to your local application folder.");
    DWORD written{};
    const bool success = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr)
        && written == text.size() && FlushFileBuffers(file);
    CloseHandle(file);
    if (!success) throw std::runtime_error("Settings could not be written completely.");
    if (std::filesystem::exists(path_)) {
        if (!primaryValid_) {
            auto damaged = path_; damaged += L".corrupt-" + std::to_wstring(GetTickCount64());
            if (!CopyFileW(path_.c_str(), damaged.c_str(), TRUE))
                throw std::runtime_error("Cannot preserve the damaged settings file.");
        }
        if (!ReplaceFileW(path_.c_str(), temporary.c_str(), primaryValid_ ? backup.c_str() : nullptr, REPLACEFILE_IGNORE_MERGE_ERRORS,
            nullptr, nullptr)) throw std::runtime_error("Cannot replace settings; the previous file was preserved.");
    } else if (!MoveFileExW(temporary.c_str(), path_.c_str(), MOVEFILE_WRITE_THROUGH)) {
        throw std::runtime_error("Cannot commit the settings file.");
    }
    primaryValid_ = true;
    legacySource_.clear();
}
}
