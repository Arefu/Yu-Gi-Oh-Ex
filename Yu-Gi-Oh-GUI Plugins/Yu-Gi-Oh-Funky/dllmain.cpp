#include <Windows.h>
#include "imgui.h"

#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <string>
#include <detours.h>
#include <iostream>

#include "YuGiOh/YuGiOh-DUEL.h"
#include "YuGiOh/YuGiOh-UTIL.h"
#include "YuGiOh/YuGiOh-CARDS.h"
#include "YuGiOh//YuGiOh-GAME.h"
#include "YuGiOh//YuGiOh-UI.h"

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
    ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
}

int KonamiId = 3900;
int AnimationId = 1;
int ArugmentOne = 0, a3 = 0, a4 = 0;
extern "C" __declspec(dllexport) void ProcessWindow()
{
    ImGui::Begin("Yu-Gi-Oh! YGO::DUEL Function Testing");

    if (ImGui::CollapsingHeader("Functions", ImGuiTreeNodeFlags_DefaultOpen))
    {
        ImGui::Text("YGO::DUEL::Get_DuelStuck() %d", YGO::DUEL::Get_DuelStuck());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns whether the duel is stuck/frozen");

        if (ImGui::Button("YGO::DUEL::Set_DuelStuck()"))
            YGO::DUEL::Set_DuelStuck();
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Freezes the current duel");

        ImGui::Text("YGO::DUEL::Get_IsDuelMultiplayer() %d", YGO::DUEL::Get_IsDuelMultiplayer());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns whether the duel is multiplayer");

        if (ImGui::Button("YGU::DUEL::Set_IsDuelMultiplayer()"))
            YGO::DUEL::Set_IsDuelMultiplayer(true);
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Sets the duel to multiplayer mode");

        ImGui::Text("YGO::DUEL::Get_IsRoundBasedDuel() %d", YGO::DUEL::Get_IsRoundBasedDuel());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns whether the duel is round-based");

        if (ImGui::Button("YGO::DUEL::Set_IsRoundBasedDuel()"))
            YGO::DUEL::Set_IsRoundBasedDuel(true);
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Sets the current duel to round-based");

        ImGui::Text("YGO::DUEL::Get_IsTutorialDuel() %d", YGO::DUEL::Get_IsTutorialDuel());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns whether the duel is a tutorial duel");

        if (ImGui::Button("YGO::DUEL::Set_IsTutorialDuel()"))
            YGO::DUEL::Set_IsTutorialDuel(true);
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Sets the current duel to a tutorial duel");

        ImGui::Text("YGO::DUEL::Get_NumberOfPlayers() %d", YGO::DUEL::Get_NumberOfPlayers());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns the number of players in the duel (1 for single player, 2 for multiplayer, 4 for tag duel)");

        ImGui::Text("YGO::DUEL::Get_DuelTimeLimit() %d", YGO::DUEL::Get_DuelTimeLimit());
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Returns the duel time limit in milliseconds (0 for no limit)");

        if (ImGui::Button("YGO::DUEL::Set_DuelTimeLimit(300000)"))
        {
            auto asf = reinterpret_cast<int(*)(short CardId)>(0x14015EA90);
            *reinterpret_cast<short*>(0x14349BC80) = 4844;
            std::cout << asf(4844);
        }
    }

    ImGui::End();

    ImGui::Begin("Yu-Gi-Oh! YGO::CARDS Function Testing");

    ImGui::InputInt("Konami ID", &KonamiId, 1, 0x1);
    if (KonamiId < 3900)
        KonamiId = 3900;
    if (ImGui::CollapsingHeader("Card Properties")) {
        ImGui::Text("Get_CardNameFromKonamiId() %ls", YGO::CARDS::Get_CardNameFromKonamiId(KonamiId));
        ImGui::TextWrapped("Get_CardDescriptionFromKonamiId() %ls", YGO::CARDS::Get_CardDescriptionFromKonamiId(KonamiId));
        ImGui::Text("Get_EffectiveAttackFromKonamiId() %d", YGO::CARDS::Get_EffectiveAttackFromKonamiId(KonamiId));
        ImGui::Text("Get_EffectiveDefenseFromKonamiId() %d", YGO::CARDS::Get_EffectiveDefenseFromKonamiId(KonamiId));
        ImGui::Text("Get_CardLevelFromKonamiId() %d", YGO::CARDS::Get_CardLevelFromKonamiId(KonamiId));
        ImGui::Text("Get_CardTypeFromKonamiId() %d", YGO::CARDS::Get_CardTypeFromKonamiId(KonamiId));
        ImGui::Text("Get_CardLimitedStatus() %d", YGO::CARDS::Get_CardLimitedStatusFromKonamiId(KonamiId));

        ImGui::Text("Get_InternalIdFromKonamiId() %d", YGO::CARDS::Get_InternalIdFromKonamiId(KonamiId));
        ImGui::Text("Is_ValidCardId() %d", YGO::CARDS::Is_ValidCardId(KonamiId));

        ImGui::Text("Get_XX() %d", YGO::CARDS::Is_CardKonamiIdLinkedToInternalId(KonamiId));
    }

    ImGui::End();

    ImGui::Begin("Yu-Gi-Oh! YGO::UI Function Testing");

    ImGui::InputInt("Animation ID", &AnimationId, 1, 0x1);
    ImGui::InputInt("Argument One", &ArugmentOne, 1, 0x1);
    ImGui::InputInt("a3", &a3, 1, 0x1);
    ImGui::InputInt("a4", &a4, 1, 0x1);
    if (ImGui::Button("YGO::UI::DrawDuelAnimation()"))
        YGO::UI::Draw_DuelAnimationFromId(static_cast<YGO::UI::Animations>(AnimationId), ArugmentOne, a3, a4);
    if (ImGui::IsItemHovered())
    {
        switch (AnimationId)
        {
        case YGO::UI::Animations::DUEL:

            ImGui::SetTooltip("Plays the DUEL animation that plays at the start of a duel");
            break;
        case YGO::UI::Animations::YOU_WIN:
            ImGui::SetTooltip("Plays the YOU WIN/LOSE/DRAW animation that plays at the end of a duel and ends the duel, use ArgumentOne to select which plays");
            break;
        default:
            ImGui::SetTooltip("Unknown Animation ID");
            break;
        }
    }

    ImGui::Text("g_iCurrentDuelAnimation %d", YGO::UI::g_iCurrentDuelAnimation);
    ImGui::Text("g_iPreviousDuelAnimation %d", YGO::UI::g_iPreviousDuelAnimation);
    ImGui::Text("g_iActiveDuelAnimation %d", YGO::UI::g_iActiveDuelAnimation);

    ImGui::End();

    ImGui::Begin("Yu-Gi-Oh! Global Variable Testing");
    ImGui::Text("YGO::DUEL::bIsDuelMultiplayer %d", YGO::DUEL::bIsDuelMultiplayer);
    ImGui::Text("YGO::DUEL::bIsRoundBasedDuel %d", YGO::DUEL::bIsRoundBasedDuel);
    ImGui::Text("YGO::DUEL::iDuelTimeLimit %d", YGO::DUEL::iDuelTimeLimit);

    ImGui::Text("YGO::DUEL::bIsTagDuel %d", YGO::DUEL::bIsTagDuel);
    ImGui::Text("YGO::DUEL::bIsTutorialDuel %d", YGO::DUEL::bIsTutorialDuel);
    ImGui::Text("YGO::DUEL::iTutorialDuelIndex %d", YGO::DUEL::iTutorialDuelIndex);
    ImGui::End();
}

extern "C" _declspec(dllexport) void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    }
}

extern "C" _declspec(dllexport) void ProcessConfig()
{
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
