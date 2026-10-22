#pragma once
#include "am/engine.hpp"
namespace am {
class ControlServer {
public:
    explicit ControlServer(Engine& engine) : engine_(engine) {}
    void run();
    // Dispatch an already-framed request independently of the pipe transport.
    Json handle(const Json& request);
private:
    Engine& engine_;
};
}
