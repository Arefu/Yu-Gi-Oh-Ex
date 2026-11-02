#include "QuickStart.h"

#include <Windows.h>
#include <detours.h>
#include "imgui.h"

#include <cstdint>
#include <cstdio>
#include <string>

#include "Yu-Gi-Oh-Core.h"
#include "Yu-Gi-Oh-RIX.h"

namespace
{
    constexpr const char* kSection = "Yu-Gi-Oh-Funky";
    constexpr int kFramesOnMenu = 15;       // let the main menu finish fading in before leaving it
    constexpr double kTitleWait = 0.4;      // ScreenTitle::Update takes no Play before this

    // RIX::ScreenTitle
    constexpr size_t kTitleSavePolling = 656;   // the early save read is still running
    constexpr size_t kTitlePlayed = 660;

    int g_Screen = 0;
    std::string g_Page;
    int g_Slot = 0;
    bool g_Pressed = false;     // Play was pressed for the player (or given up on)
    bool g_Done = false;        // reached the chosen screen
    int g_MenuFrames = 0;

    long long orig_LogoUpdate = 0x140805350;    // RIX::LoadingScreenLogo::Update
    long long orig_TitleUpdate = 0x1408676A0;   // RIX::ScreenTitle::Update

    const auto IsScreenActive = reinterpret_cast<char(__fastcall*)(int64_t)>(0x1408223A0);
    const auto GetScreenTime = reinterpret_cast<double(__fastcall*)(int64_t)>(0x140807F20);          // RIX::UI::GetScreenTime
    const auto GetCurrentProfile = reinterpret_cast<int(__fastcall*)(void*)>(0x140800690);           // YGO::SAVE::Get_CurrentProfileId
    const auto SetActiveController = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140800D70);   // Input_SetActiveController
    const auto GetSignIn = reinterpret_cast<int64_t(__fastcall*)()>(0x140866030);                    // RIX::ScreenSignIn::Get
    const auto SetSignInProfile = reinterpret_cast<void(__fastcall*)(int64_t, int, int64_t)>(0x140866440);   // RIX::ScreenSignIn::SetProfile
    const auto AfterPlay = reinterpret_cast<void(__fastcall*)()>(0x1408671A0);                       // what the title's Play calls before leaving
    const auto SetPendingScreen = reinterpret_cast<void(__fastcall*)(int64_t, int)>(0x1408085E0);    // RIX::UI::SetPendingScreen
    const auto SetFadeColour = reinterpret_cast<void(__fastcall*)(unsigned int)>(0x140866A60);       // RIX::SetFadeColour
    const auto Navigate = reinterpret_cast<char(__fastcall*)(int64_t, int, double, int, char)>(0x1408087A0);   // RIX::UI::NavigateToScreen

    void* InputManager()
    {
        const volatile uintptr_t address = 0x142924010;   // g_InputManager
        return reinterpret_cast<void*>(address);
    }

    bool Wanted() { return g_Screen != 0 || !g_Page.empty(); }

    std::string ConfigPath()
    {
        const char* folder = Core::Load() && Core::Functions().GameFolder ? Core::Functions().GameFolder() : ".\\";
        return std::string(folder) + "Config.ini";
    }

    // As Yu-Gi-Oh-SpeedHacks' SkipLogos: the first logo goes straight to the title, with a black fade that is over in a frame.
    void __fastcall Hook_LogoUpdate(int64_t self, int64_t ui, double)
    {
        *reinterpret_cast<uint8_t*>(self + 656) = 0;
        if (*reinterpret_cast<uint8_t*>(self + 788) != 0)   // already on its way
            return;
        SetFadeColour(0xFF000000);
        if (Navigate(ui, RIX_SCREEN_TITLE, 0.01, 272, 0))
            *reinterpret_cast<uint8_t*>(self + 788) = 1;
    }

    // What ScreenTitle::Update does when Play is clicked (a click uses controller 0).
    void __fastcall Hook_TitleUpdate(int64_t self, int64_t ui, double seconds)
    {
        reinterpret_cast<void(__fastcall*)(int64_t, int64_t, double)>(orig_TitleUpdate)(self, ui, seconds);

        if (g_Pressed || !IsScreenActive(self) || *reinterpret_cast<uint8_t*>(self + kTitleSavePolling) != 0 ||
            *reinterpret_cast<uint8_t*>(self + kTitlePlayed) != 0 || GetScreenTime(ui) < kTitleWait)
            return;
        g_Pressed = true;

        // No slot to go to: leave Play to the player, who picks one as usual. The chosen screen still opens from the main menu.
        if (!Core::Load() || !Core::Functions().QuickPlaySlot || Core::Functions().QuickPlaySlot(g_Slot) == 0)
            return;

        *reinterpret_cast<uint8_t*>(self + kTitlePlayed) = 1;
        const int profile = GetCurrentProfile(InputManager());
        SetActiveController(InputManager(), 0);
        SetSignInProfile(GetSignIn(), profile, 0);
        AfterPlay();
        SetPendingScreen(ui, RIX_SCREEN_SIGN_IN);
    }
}

void QuickStart::Install()
{
    const std::string ini = ConfigPath();
    char page[128] = {};
    GetPrivateProfileStringA(kSection, "StartPage", "", page, sizeof(page), ini.c_str());
    g_Page = page;
    g_Screen = static_cast<int>(GetPrivateProfileIntA(kSection, "StartScreen", 0, ini.c_str()));
    g_Slot = static_cast<int>(GetPrivateProfileIntA(kSection, "StartSlot", 0, ini.c_str()));
    if (!Wanted())
        return;

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_LogoUpdate, Hook_LogoUpdate);
    DetourAttach(&(PVOID&)orig_TitleUpdate, Hook_TitleUpdate);
    DetourTransactionCommit();
}

void QuickStart::Tick()
{
    if (g_Done || !Wanted() || !RIX::Load())
        return;
    if (RIX::Functions().GetCurrentScreenId() != RIX_SCREEN_MAIN_MENU)
    {
        g_MenuFrames = 0;
        return;
    }
    if (++g_MenuFrames < kFramesOnMenu)
        return;
    g_Done = true;
    if (!g_Page.empty() && RIX::Functions().OpenPageFile)
        RIX::Functions().OpenPageFile(g_Page.c_str());
    else if (g_Screen != RIX_SCREEN_MAIN_MENU)
        RIX::Functions().GotoScreen(g_Screen);
}

void QuickStart::DrawTool(int selectedScreen, const char* selectedName)
{
    ImGui::Separator();
    const std::string ini = ConfigPath();
    char savedPage[128] = {};
    GetPrivateProfileStringA(kSection, "StartPage", "", savedPage, sizeof(savedPage), ini.c_str());
    const int savedScreen = static_cast<int>(GetPrivateProfileIntA(kSection, "StartScreen", 0, ini.c_str()));
    if (savedPage[0])
        ImGui::Text("The next start opens page: %s", savedPage);
    else if (savedScreen)
        ImGui::Text("The next start opens screen: %d", savedScreen);
    else
        ImGui::TextDisabled("The next start goes through the title as usual.");

    char label[96];
    std::snprintf(label, sizeof(label), "Start on %s", selectedName);
    if (ImGui::Button(label))
    {
        WritePrivateProfileStringA(kSection, "StartScreen", std::to_string(selectedScreen).c_str(), ini.c_str());
        WritePrivateProfileStringA(kSection, "StartPage", nullptr, ini.c_str());
    }
    if (ImGui::IsItemHovered())
        ImGui::SetTooltip("From the next start: skips the logos and the title, loads your last save and opens this screen.");

    static char page[128] = {};
    ImGui::SetNextItemWidth(200);
    ImGui::InputText("Page", page, sizeof(page));
    ImGui::SameLine();
    if (ImGui::Button("Start on this page") && page[0])
        WritePrivateProfileStringA(kSection, "StartPage", page, ini.c_str());

    if (ImGui::Button("Start normally"))
    {
        WritePrivateProfileStringA(kSection, "StartScreen", nullptr, ini.c_str());
        WritePrivateProfileStringA(kSection, "StartPage", nullptr, ini.c_str());
    }
}
