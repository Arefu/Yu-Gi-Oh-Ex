#pragma once

// The in-game plugin list. The loader keeps a [Yu-Gi-Oh-RIX] section in Config.ini (Name=1 or Name=0 for every plugin it found, and
// YGO-Ex/Name for the ones Yu-Gi-Oh-GUI starts) and only loads the ones set to 1. This adds a "Plugins" button to the main menu; pressing it swaps the menu for one button per plugin
// (press to switch it on or off, six to a page) with Next and Back. The choice is written to Config.ini and applies at the next launch.
namespace PluginMenu
{
    // Reads the list and adds the buttons. Does nothing when the section is empty (the loader has not listed anything yet).
    void Install();

    // Plugins per page, 3 to 5 (the Settings screen row changes it; it is kept in Config.ini as PluginsPerPage).
    int PerPage();
    void SetPerPage(int Count);
}
