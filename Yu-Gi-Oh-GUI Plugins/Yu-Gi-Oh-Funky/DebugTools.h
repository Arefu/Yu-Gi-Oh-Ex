#pragma once

// The "Debug Tools" ImGui window: save editing, screen jumping, the main menu buttons (through Yu-Gi-Oh-RIX) and duel settings.
namespace DebugTools
{
    // Reads [Yu-Gi-Oh-Funky] from Config.ini and, when DemoMenuButtons=1, adds two example buttons to the main menu.
    void Init();

    void Draw();
}
