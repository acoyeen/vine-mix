#include "am/control_server.hpp"
#include <windows.h>
#include <sddl.h>
#include <algorithm>
#include <array>
#include <stdexcept>
#include <string>
#include <vector>

namespace am {
namespace {
constexpr std::size_t maximumMessageBytes = 256 * 1024;
class Handle {
public:
    explicit Handle(HANDLE value = nullptr) : value_(value) {}
    ~Handle() { if (value_ && value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); }
    HANDLE get() const noexcept { return value_; }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
private:
    HANDLE value_;
};
bool completeIo(HANDLE pipe, OVERLAPPED& operation, Engine& engine, DWORD& bytes) {
    while (!engine.stopping()) {
        const DWORD wait = WaitForSingleObject(operation.hEvent, 200);
        if (wait == WAIT_OBJECT_0) return GetOverlappedResult(pipe, &operation, &bytes, FALSE) != FALSE;
        if (wait != WAIT_TIMEOUT) break;
    }
    CancelIoEx(pipe, &operation);
    // The event and OVERLAPPED storage must remain alive until cancellation
    // completes; cancellation is not itself completion.
    GetOverlappedResult(pipe, &operation, &bytes, TRUE);
    return false;
}
bool readChunk(HANDLE pipe, void* target, DWORD capacity, DWORD& count, Engine& engine, HANDLE event) {
    OVERLAPPED operation{}; operation.hEvent = event; ResetEvent(event);
    if (ReadFile(pipe, target, capacity, &count, &operation)) return true;
    return GetLastError() == ERROR_IO_PENDING && completeIo(pipe, operation, engine, count);
}
bool writeAll(HANDLE pipe, const std::string& text, Engine& engine, HANDLE event) {
    std::size_t offset{};
    while (offset < text.size()) {
        DWORD count{};
        OVERLAPPED operation{}; operation.hEvent = event; ResetEvent(event);
        const DWORD requested = static_cast<DWORD>(std::min<std::size_t>(text.size() - offset, 32768));
        if (!WriteFile(pipe, text.data() + offset, requested, &count, &operation)) {
            if (GetLastError() != ERROR_IO_PENDING || !completeIo(pipe, operation, engine, count)) return false;
        }
        if (!count) return false;
        offset += count;
    }
    return true;
}
}
Json ControlServer::handle(const Json& request) {
    const Json requestId = request.value("requestId", Json(""));
    Json response{{"protocolVersion", 1}, {"requestId", requestId}, {"ok", true}};
    try {
        if (!request.is_object() || request.at("protocolVersion").get<int>() != 1)
            throw std::runtime_error("Unsupported control protocol version.");
        const auto command = request.at("command").get<std::string>();
        if (command == "snapshot") response["snapshot"] = engine_.snapshot();
        else if (command == "configure") {
            engine_.configure(request.at("configuration").get<Configuration>(), request.at("expectedRevision").get<std::uint64_t>());
            response["snapshot"] = engine_.snapshot();
        } else if (command == "retry") { engine_.retry(); response["snapshot"] = engine_.snapshot(); }
        else if (command == "shutdown") { response["message"] = "Audio engine stopped."; }
        else throw std::runtime_error("Unknown control command.");
    } catch (const std::exception& error) { response["ok"] = false; response["error"] = error.what(); }
    return response;
}
void ControlServer::run() {
    // A protected DACL allows only this signed-in user and SYSTEM. Remote pipe
    // clients are rejected even if they could otherwise authenticate.
    PSECURITY_DESCRIPTOR descriptor{};
    const auto sddl = L"D:P(A;;GA;;;SY)(A;;GA;;;" + widen(currentUserSid()) + L")";
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr))
        throw std::runtime_error("Cannot secure the local control connection.");
    struct FreeDescriptor { PSECURITY_DESCRIPTOR value; ~FreeDescriptor() { LocalFree(value); } } cleanup{descriptor};
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
    const auto name = pipeName();
    while (!engine_.stopping()) {
        Handle pipe(CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 65536, 65536, 0, &security));
        if (pipe.get() == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot create the local control connection.");
        Handle event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        if (!event.get()) throw std::runtime_error("Cannot create the control event.");
        OVERLAPPED operation{}; operation.hEvent = event.get();
        DWORD ignored{};
        const bool connected = ConnectNamedPipe(pipe.get(), &operation) != FALSE;
        if (!connected) {
            const auto error = GetLastError();
            if (error != ERROR_PIPE_CONNECTED &&
                !(error == ERROR_IO_PENDING && completeIo(pipe.get(), operation, engine_, ignored))) continue;
        }
        std::string pending;
        pending.reserve(8192);
        std::array<char, 8192> buffer{};
        bool keepReading = true;
        while (!engine_.stopping() && keepReading) {
            DWORD received{};
            if (!readChunk(pipe.get(), buffer.data(), static_cast<DWORD>(buffer.size()), received, engine_, event.get()) || !received) break;
            pending.append(buffer.data(), received);
            if (pending.size() > maximumMessageBytes) break;
            for (;;) {
                const auto newline = pending.find('\n');
                if (newline == std::string::npos) break;
                auto line = pending.substr(0, newline); pending.erase(0, newline + 1);
                if (!line.empty() && line.back() == '\r') line.pop_back();
                bool shutdown = false;
                Json response;
                try {
                    const auto request = Json::parse(line);
                    response = handle(request);
                    shutdown = response.value("ok", false) && request.value("command", std::string{}) == "shutdown";
                } catch (const std::exception& error) {
                    response = Json{{"protocolVersion", 1}, {"requestId", ""}, {"ok", false}, {"error", error.what()}};
                }
                if (!writeAll(pipe.get(), response.dump() + "\n", engine_, event.get())) { keepReading = false; break; }
                if (shutdown) { engine_.requestStop(); keepReading = false; break; }
            }
        }
        CancelIoEx(pipe.get(), nullptr);
        DisconnectNamedPipe(pipe.get());
    }
}
}
