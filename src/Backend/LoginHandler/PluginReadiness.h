#pragma once

// A current game-thread sample, not an elapsed-time guess.
inline bool PluginsCanStart(bool sameCharacter, bool teleporting, bool hasEngine,
    bool startupActive, bool startupComplete)
{
    return sameCharacter && !teleporting && hasEngine && (!startupActive || startupComplete);
}
