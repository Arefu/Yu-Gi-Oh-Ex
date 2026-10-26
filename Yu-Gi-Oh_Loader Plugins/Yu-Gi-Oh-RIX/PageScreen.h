#pragma once
#include <string>

#include "Yu-Gi-Oh-RIX.h"

// The page engine: page files with "version": 2 (Yu-Gi-Oh-Ex\pages\<name>.json) open on a screen of RIX's own (id 102), built from the
// game's art, with controls of our own (buttons, options, toggles, sliders, lists), game widgets (digits, card details), actions,
// bindings to Config.ini / page variables, and conditions. docs/PageEngine.md has the file format.
namespace PageScreen
{
    // Opens the page when its file is a version 2 page (true); false = not one (the caller opens it the version 1 way) or not found.
    bool TryOpen(const std::string& name);

    void SetVar(const std::string& name, const std::string& value);
    std::string GetVar(const std::string& name);

    // A list source plugins register: the callback fills the list with RIX_PageListAdd while a page using it is built.
    void RegisterList(const std::string& name, RIX_PageListCallback callback, void* user);
    void ListAdd(void* list, const wchar_t* label, const wchar_t* description, const char* value);

    // A value plugins supply: "game:<name>.<argument>" in pages calls it (RIX_RegisterPageValue).
    void RegisterValue(const std::string& name, RIX_PageValueCallback callback, void* user);

    // A folder of pages a plugin ships (RIX_AddPageFolder), looked in after the mods and Yu-Gi-Oh-Ex\pages.
    void AddFolder(const std::wstring& folder);

    void Install();
}
