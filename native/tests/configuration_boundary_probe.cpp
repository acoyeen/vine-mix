#include "am/configuration.hpp"
#include "am/graph.hpp"
#include <iostream>
#include <string>

// Deliberately inert: stdin/stdout fixtures only. No engine, control server,
// settings store, device inventory, audio workers or process launch is used.
int main(int argc, char** argv) {
    if (argc == 2 && std::string(argv[1]) == "--fresh-defaults") {
        try {
            const auto configuration = am::defaultConfiguration();
            am::validateConfiguration(configuration);
            (void)am::compileGraph(configuration.graph);
            std::cout << am::Json(configuration).dump() << '\n';
            return 0;
        } catch (...) { return 1; }
    }
    if (argc != 2 || std::string(argv[1]) != "--stdio-fixtures") return 2;
    std::string line;
    while (std::getline(std::cin, line)) {
        try {
            auto config = am::Json::parse(line).get<am::Configuration>();
            am::validateConfiguration(config);
            const auto plan = am::compileGraph(config.graph);
            auto nodes = am::Json::array(), edges = am::Json::array();
            for (std::size_t i = 0; i < plan.nodeCount; ++i) nodes.push_back(plan.nodes[i].gainDb);
            for (std::size_t i = 0; i < plan.edgeCount; ++i) edges.push_back(plan.edges[i].gainDb);
            am::PathState known, unknown;
            known.state = "Waiting"; known.reason = "device_disabled"; known.message = "Reworded fixture diagnostic";
            unknown.state = "Waiting"; unknown.reason = "future_reason"; unknown.message = "Device disabled in Windows Sound settings";
            std::cout << am::Json{{"ok", true}, {"configuration", config},
                {"planNodeGains", nodes}, {"planEdgeGains", edges},
                {"statusFixtures", am::Json::array({am::Json(known), am::Json(unknown)})}}.dump() << std::endl;
        } catch (const std::exception& error) {
            std::cout << am::Json{{"ok", false}, {"error", error.what()}}.dump() << std::endl;
        }
    }
    return std::cin.bad() ? 1 : 0;
}
