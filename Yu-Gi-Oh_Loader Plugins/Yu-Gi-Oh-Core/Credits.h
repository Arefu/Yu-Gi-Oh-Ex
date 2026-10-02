#pragma once
#include <string>

// Adds text to the game's Credits screen (Help & Options > Credits) at runtime; credits.dat in the archives is not touched.
//
// The screen reads main/ui/credits/credits.dat (UTF-16 with a BOM) into a string and turns it into lines, one per '\n':
//   [Heading]   a heading (drawn in the heading colour, brackets removed)
//   *name*      the picture main/ui/credits/name (it has to exist: the game does not check)
//   (empty)     a 32 pixel gap
//   anything    a line of text
// Core hooks the step that turns the string into lines and puts its own text before the game's credits, in this order:
//   - the built-in thanks (Credits.cpp kBuiltIn);
//   - Yu-Gi-Oh-Ex\credits.json (read every time the screen is built, so edits show the next time Credits is opened):
//       { "plugins": true,                        a "Yu-Gi-Oh-Ex Plugins" section naming every plugin that is loaded (default true)
//         "sections": [ { "title": "My Mod", "lines": [ "Someone", "Someone Else" ] },
//                       { "image": "wwise_white" },
//                       { "lines": [ "Text with no heading" ] } ] }
//   - other plugins, through Core_AddCredits (Yu-Gi-Oh-Core.h);
//   - the plugin list (unless "plugins" is false);
//   - then the game's own credits.
namespace Credits
{
    bool Install();

    // A section from a plugin: Heading may be empty, Lines are separated by '\n'. Both UTF-8. Kept until the game closes.
    void Add(const std::string& Heading, const std::string& Lines);
}
