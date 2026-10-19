#pragma once
#include "am/engine.hpp"
namespace am {
class ControlServer {
public:
    explicit ControlServer(Engine& engine) : engine_(engine) {}
    void run();
private:
    Json handle(const Json& request);
    Engine& engine_;
};
}
