#include "../src/Backend/LoginHandler/PluginReadiness.h"
#include <cstdio>
int main() {
    if (!PluginsCanStart(true, false, true, true, true)) return 1;
    if (!PluginsCanStart(true, false, true, false, false)) return 2; // no fast-startup dependency
    if (PluginsCanStart(false, false, true, true, true)) return 3; // wrong character
    if (PluginsCanStart(true, true, true, true, true)) return 4; // zoning
    if (PluginsCanStart(true, false, false, true, true)) return 5; // no engine
    if (PluginsCanStart(true, false, true, true, false)) return 6; // startup still rendering
    std::puts("PASS: game-thread readiness requires character, engine, no zoning, completed active startup.");
}
