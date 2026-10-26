#include "MatchSettingsPage.h"

#include <algorithm>
#include <string>

#include "BanListPicker.h"
#include "LiveSetting.h"
#include "Yu-Gi-Oh-RIX.h"

namespace
{
    constexpr const char* kPageName = "mp-match-settings";

    bool g_Registered = false;

    std::string Var(const char* name)
    {
        char buffer[64] = {};
        const int length = RIX::Functions().GetPageVar(name, buffer, sizeof(buffer));
        return length > 0 ? std::string(buffer, (std::min)(length, static_cast<int>(sizeof(buffer)) - 1)) : std::string();
    }

    void __cdecl ApplyFormat(int, void*) { LiveSetting::SetFormatKey(Var("mp.format").c_str()); }
}

namespace MatchSettingsPage
{
    void Register()
    {
        if (g_Registered || !RIX::Load())
            return;
        g_Registered = true;
        BanListPicker::Register();
        RIX::Functions().RegisterAction("mp.format.apply", &ApplyFormat, nullptr);
    }

    bool Open()
    {
        Register();
        if (!g_Registered)
            return false;
        RIX::Functions().SetPageVar("mp.format", LiveSetting::FormatKey());
        RIX::Functions().SetPageVar("banlist.page", "0");      // the picker it opens starts on page 1
        RIX::Functions().SetPageVar("banlist.preview", "");    // and previews the list in use
        return RIX::Functions().OpenPageFile(kPageName) != 0;
    }
}
