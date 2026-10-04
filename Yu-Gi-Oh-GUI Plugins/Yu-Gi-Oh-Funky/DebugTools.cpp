#include "DebugTools.h"

#include <Windows.h>
#include "imgui.h"

#include <cstring>
#include <map>
#include <string>
#include <vector>

#include "Yu-Gi-Oh-RIX.h"
#include "YuGiOh/YuGiOh-CARDS.h"
#include "YuGiOh/YuGiOh-DUEL.h"
#include "YuGiOh/YuGiOh-RIX.h"
#include "YuGiOh/YuGiOh-SAVE.h"
#include "YuGiOh/YuGiOh-GAME.h"
#include "YuGiOh/YuGiOh-UTIL.h"
#include "YuGiOh/YuGiOh-UI.h"
#include "DuelTest.h"
#include "UiWitchcraft.h"

namespace
{
    bool g_ShowTools = false;
    int g_KonamiId = 4007;
    int g_Copies = 3;

    struct NamedScreen { const char* Name; int Id; };
    constexpr NamedScreen kScreens[] =
    {
        { "Title", RIX_SCREEN_TITLE }, { "Sign in", RIX_SCREEN_SIGN_IN }, { "Main menu", RIX_SCREEN_MAIN_MENU }, { "Game loading", RIX_SCREEN_LOADING },
        { "Game begin", RIX_SCREEN_GAME_BEGIN }, { "Help and options", RIX_SCREEN_HELP_AND_OPTIONS }, { "Settings", RIX_SCREEN_SETTINGS },
        { "Video settings", RIX_SCREEN_VIDEO_SETTINGS }, { "Credits", RIX_SCREEN_GAME_CREDITS }, { "Controller settings", RIX_SCREEN_CONTROLLER_SETTINGS },
        { "How to play", RIX_SCREEN_HOW_TO_PLAY }, { "Statistics", RIX_SCREEN_STATISTICS }, { "Voices", RIX_SCREEN_VOICES },
        { "Pause menu", RIX_SCREEN_PAUSE_MENU }, { "Duelist challenge", RIX_SCREEN_DUELIST_CHALLENGE }, { "Campaign dialog", RIX_SCREEN_CAMPAIGN_DIALOG },
        { "Campaign select deck", RIX_SCREEN_CAMPAIGN_SELECT_DECK }, { "Tutorial list", RIX_SCREEN_TUTORIAL_LIST }, { "Deck editor", RIX_SCREEN_DECK_EDITOR },
        { "Swap cards", RIX_SCREEN_SWAP_CARDS }, { "Match result", RIX_SCREEN_MATCH_RESULT }, { "Game result", RIX_SCREEN_GAME_RESULT },
        { "Card shop", RIX_SCREEN_CARD_SHOP }, { "Battle pack", RIX_SCREEN_BATTLEPACK }, { "Battle pack draft", RIX_SCREEN_BATTLEPACK_DRAFT },
        { "Battle pack edit", RIX_SCREEN_BATTLEPACK_EDIT }, { "Player match", RIX_SCREEN_PLAYER_MATCH }, { "Live setting", RIX_SCREEN_LIVE_SETTING },
        { "Live session", RIX_SCREEN_LIVE_SESSION }, { "Live lobby", RIX_SCREEN_LIVE_LOBBY }, { "Leaderboard", RIX_SCREEN_LEADERBOARD },
        { "Invite landing", RIX_SCREEN_INVITE_LANDING }, { "Safety zone", RIX_SCREEN_SAFETY_ZONE }, { "Duel select", RIX_SCREEN_DUEL_SELECT },
        { "Select rung", RIX_SCREEN_SELECT_RUNG }, { "Score review", RIX_SCREEN_SCORE_REVIEW },
    };

    constexpr const char* kVanillaItems[YGO::RIX::MMI_VANILLA_COUNT] =
    {
        "Single player menu", "Solo duel", "Duelist challenge", "Multiplayer menu", "Multiplayer A", "Multiplayer B", "Leaderboard",
        "Battle pack", "Deck editor", "Card shop", "Help and options", "Tutorials", "Quit game",
    };

    const char* ScreenName(int id)
    {
        for (const NamedScreen& screen : kScreens)
        {
            if (screen.Id == id)
                return screen.Name;
        }
        return "(unnamed)";
    }

    std::wstring ToWide(const char* text)
    {
        int length = MultiByteToWideChar(CP_UTF8, 0, text, -1, nullptr, 0);
        std::wstring wide(length > 0 ? length - 1 : 0, L'\0');
        if (length > 1)
            MultiByteToWideChar(CP_UTF8, 0, text, -1, wide.data(), length);
        return wide;
    }

    std::string ToUtf8(const wchar_t* text)
    {
        int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
        std::string utf8(length > 0 ? length - 1 : 0, '\0');
        if (length > 1)
            WideCharToMultiByte(CP_UTF8, 0, text, -1, utf8.data(), length, nullptr, nullptr);
        return utf8;
    }

    // ---- the two demo buttons (DemoMenuButtons=1) ----

    void __cdecl OnDebugToolsButton(int, void*)
    {
        g_ShowTools = !g_ShowTools;
    }

    // The bundled ImGui predates SeparatorText.
    void Section(const char* title)
    {
        ImGui::Separator();
        ImGui::TextUnformatted(title);
    }

    // ---- save ----

    // The live player section, or null before a profile is loaded (the game guards the same way).
    uint8_t* PlayerSection()
    {
        if (YGO::SAVE::Get_CurrentProfileId(YGO::SAVE::g_SaveSystem) == 0xFFFFFFFFu)
            return nullptr;
        return YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
    }

    void DrawSave()
    {
        uint8_t* section = PlayerSection();
        if (!section)
        {
            ImGui::TextWrapped("No profile is loaded yet. Sign in first.");
            return;
        }

        using namespace YGO::SAVE::PlayerSection;
        auto& wallet = *reinterpret_cast<uint64_t*>(section + Wallet);
        ImGui::InputScalar("Points (DP)", ImGuiDataType_U64, &wallet);
        if (ImGui::Button("+10,000"))
            wallet += 10000;
        ImGui::SameLine();
        if (ImGui::Button("+1,000,000"))
            wallet += 1000000;

        Section("Main menu unlocks (PlayerSection + 2964)");
        auto& flags = *reinterpret_cast<uint32_t*>(section + MenuUnlockFlags);
        ImGui::CheckboxFlags("Duelist challenge", &flags, DUELIST_CHALLENGE);
        ImGui::CheckboxFlags("Battle packs", &flags, BATTLE_PACKS);
        ImGui::CheckboxFlags("Card shop", &flags, CARD_SHOP);
        if (ImGui::Button("Unlock all three"))
            flags |= DUELIST_CHALLENGE | BATTLE_PACKS | CARD_SHOP;
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("The greyed-out buttons on the main menu; leave the menu and come back to see it.");

        Section("Characters");
        int unlocked = 0;
        for (size_t i = 0; i < CharacterCount; ++i)
            unlocked += (section[UnlockedCharacters + (i >> 3)] >> (i & 7)) & 1;
        ImGui::Text("%d of %d unlocked", unlocked, static_cast<int>(CharacterCount));
        if (ImGui::Button("Unlock all characters"))
            memset(section + UnlockedCharacters, 0xFF, CharacterCount / 8);

        Section("Cards");
        ImGui::InputInt("Konami id", &g_KonamiId);
        g_KonamiId = g_KonamiId < 3900 ? 3900 : (g_KonamiId > 19999 ? 19999 : g_KonamiId);
        if (g_KonamiId <= 14968)
            ImGui::Text("%ls", YGO::CARDS::Get_CardNameFromKonamiId(static_cast<short>(g_KonamiId)));

        uint8_t* table = YGO::SAVE::Get_CardUnlockTable(YGO::SAVE::CURRENT_PROFILE);
        if (table)
        {
            ImGui::Text("Saved copies: %d%s", table[g_KonamiId] & 7, (table[g_KonamiId] & 8) ? " (seen)" : "");
            ImGui::SliderInt("Copies", &g_Copies, 0, 3);
            if (ImGui::Button("Set copies"))
                table[g_KonamiId] = static_cast<uint8_t>((table[g_KonamiId] & ~7) | g_Copies | (g_Copies ? 8 : 0));
            ImGui::SameLine();
            if (ImGui::Button("3 of every game card"))
            {
                for (int id = 3900; id <= 14968; ++id)
                    table[id] = 3 | 8;
            }
            if (ImGui::IsItemHovered())
                ImGui::SetTooltip("Writes the saved card table. The trunk reads it when the game starts or the profile loads.");
        }
    }

    // ---- screens ----

    void DrawScreens()
    {
        int current = RIX::Load() ? RIX::Functions().GetCurrentScreenId() : -1;
        ImGui::Text("Current screen: %d (%s)", current, ScreenName(current));

        static int selected = 2;
        if (ImGui::BeginCombo("Go to", kScreens[selected].Name))
        {
            for (int i = 0; i < static_cast<int>(std::size(kScreens)); ++i)
            {
                if (ImGui::Selectable(kScreens[i].Name, i == selected))
                    selected = i;
            }
            ImGui::EndCombo();
        }
        if (ImGui::Button("Go"))
            RIX::Functions().GotoScreen(kScreens[selected].Id);
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Uses the game's own screen change. Some screens need state the game sets up first and may not work from here.");
    }

    // ---- main menu ----

    void DrawMainMenu()
    {
        if (!RIX::Load())
        {
            ImGui::TextWrapped("Yu-Gi-Oh-RIX.dll is not loaded, so the menu can't be changed.");
            return;
        }

        RIX::Api& api = RIX::Functions();
        const bool open = api.IsMainMenuOpen() != 0;
        ImGui::Text("Main menu is %s", open ? "open" : "not open");

        if (ImGui::CollapsingHeader("Game buttons", ImGuiTreeNodeFlags_DefaultOpen))
        {
            if (!open)
                ImGui::BeginDisabled();
            for (int item = 0; item < YGO::RIX::MMI_VANILLA_COUNT; ++item)
            {
                if (item % 3)
                    ImGui::SameLine();
                if (ImGui::Button(kVanillaItems[item]))
                    api.PressMainMenuItem(item);
            }
            if (!open)
                ImGui::EndDisabled();
        }

        if (ImGui::CollapsingHeader("Added buttons", ImGuiTreeNodeFlags_DefaultOpen))
        {
            struct Edit { char Label[64]; char Description[128]; int Page; bool Loaded = false; };
            static std::map<int, Edit> edits;

            for (int index = 0; index < api.GetMainMenuButtonCount(); ++index)
            {
                int id = api.GetMainMenuButtonIdAt(index);
                RIX_ButtonDesc desc{};
                if (id < 0 || !api.GetMainMenuButton(id, &desc))
                    continue;

                Edit& edit = edits[id];
                if (!edit.Loaded)
                {
                    strncpy_s(edit.Label, ToUtf8(desc.Label).c_str(), _TRUNCATE);
                    strncpy_s(edit.Description, ToUtf8(desc.Description).c_str(), _TRUNCATE);
                    edit.Page = desc.Page;
                    edit.Loaded = true;
                }

                ImGui::PushID(id);
                ImGui::Text("Button %d", id);
                ImGui::InputText("Label", edit.Label, sizeof(edit.Label));
                ImGui::InputText("Description", edit.Description, sizeof(edit.Description));
                ImGui::Combo("Page", &edit.Page, "Main\0Single player\0Multiplayer\0");
                if (ImGui::Button("Apply"))
                {
                    std::wstring label = ToWide(edit.Label), description = ToWide(edit.Description);
                    desc.Label = label.c_str();
                    desc.Description = description.c_str();
                    desc.Page = edit.Page;
                    api.UpdateMainMenuButton(id, &desc);
                }
                ImGui::SameLine();
                if (ImGui::Button("Remove"))
                {
                    api.RemoveMainMenuButton(id);
                    edits.erase(id);
                }
                ImGui::PopID();
                ImGui::Separator();
            }

            static char newLabel[64] = "New button";
            static char newDescription[128] = "";
            static int newPage = RIX_PAGE_MAIN;
            static int newMenu = RIX_MENU_MAIN;
            ImGui::Combo("New in menu", &newMenu, "Main menu Options menu ");
            ImGui::InputText("New label", newLabel, sizeof(newLabel));
            ImGui::InputText("New description", newDescription, sizeof(newDescription));
            ImGui::Combo("New page", &newPage, "Main\0Single player\0Multiplayer\0");
            if (ImGui::Button("Add button"))
            {
                std::wstring label = ToWide(newLabel), description = ToWide(newDescription);
                RIX_ButtonDesc desc = RIX::Describe(label.c_str(), description.c_str(), newPage, &OnDebugToolsButton, nullptr,
                    newMenu == RIX_MENU_OPTIONS ? RIX_OPTION_SETTINGS : RIX_ITEM_DECK_EDITOR, newMenu);
                api.AddMainMenuButton(&desc);
            }
            if (ImGui::IsItemHovered())
                ImGui::SetTooltip("The game builds the menu once, so a new button shows up the next time it is built (restart the game).");
        }
    }

    // ---- duel ----

    void DrawDuel()
    {
        int lifePoints = YGO::DUEL::Get_StartingLifePoints();
        if (ImGui::InputInt("Starting life points", &lifePoints, 500, 1000))
            YGO::DUEL::Set_StartingLifePoints(lifePoints < 1 ? 1 : lifePoints);
        if (ImGui::IsItemHovered())
            ImGui::SetTooltip("Applies to the next duel.");

        ImGui::Separator();
        DuelTest::Draw();
    }

    // ---- the game's own functions and variables, for poking at them ----

    int g_TestKonamiId = 3900;
    int g_AnimationId = 1;
    int g_AnimationArg = 0, g_AnimationA3 = 0, g_AnimationA4 = 0;

    void DrawDuelFunctions()
    {
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
    }
    }

    void DrawCardFunctions()
    {
    ImGui::InputInt("Konami ID", &g_TestKonamiId, 1, 0x1);
    if (g_TestKonamiId < 3900)
        g_TestKonamiId = 3900;
    if (ImGui::CollapsingHeader("Card Properties")) {
        ImGui::Text("Get_CardNameFromKonamiId() %ls", YGO::CARDS::Get_CardNameFromKonamiId(g_TestKonamiId));
        ImGui::TextWrapped("Get_CardDescriptionFromKonamiId() %ls", YGO::CARDS::Get_CardDescriptionFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_EffectiveAttackFromKonamiId() %d", YGO::CARDS::Get_EffectiveAttackFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_RawAttackFromKonamiId() %d", YGO::CARDS::Get_RawAttackFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_EffectiveDefenseFromKonamiId() %d", YGO::CARDS::Get_EffectiveDefenseFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_RawDefenseFromKonamiId() %d", YGO::CARDS::Get_RawDefenseFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardAttributeFromKonamiId() %d", YGO::CARDS::Get_CardAttributeFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_SpellTrapCardProperty() %d", YGO::CARDS::Get_SpellTrapCardProperty(g_TestKonamiId));
        ImGui::Text("Get_CardLimitedStatusFromKonamiId() %d", YGO::CARDS::Get_CardLimitedStatusFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardLevelFromKonamiId() %d", YGO::CARDS::Get_CardLevelFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardTypeFromKonamiId() %d", YGO::CARDS::Get_CardTypeFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardTypeFromFullCardPropsByKonamiId() %d", YGO::CARDS::Get_CardTypeFromFullCardPropsByKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardMonsterTypeFromKonamiId() %d", YGO::CARDS::Get_CardMonsterTypeFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardSameFromKonamiId() %d", YGO::CARDS::Get_CardSameFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CurrentCardId() %d", YGO::CARDS::Get_CurrentCardId(g_TestKonamiId));
        ImGui::Text("Get_InternalIdFromKonamiId() %d", YGO::CARDS::Get_InternalIdFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_CardPendulumScaleFromKonamiId() %d", YGO::CARDS::Get_CardPendulumScaleFromKonamiId(g_TestKonamiId));
        ImGui::Text("Get_SpellTrapCardPropertyFromFullCardProps() %d", YGO::CARDS::Get_SpellTrapCardPropertyFromFullCardProps(g_TestKonamiId));

        ImGui::Text("Get_Something() %d", YGO::CARDS::Get_Something(g_TestKonamiId));
    }
    }

    void DrawUiFunctions()
    {
    ImGui::InputInt("Animation ID", &g_AnimationId, 1, 0x1);
    ImGui::InputInt("Argument One", &g_AnimationArg, 1, 0x1);
    ImGui::InputInt("a3", &g_AnimationA3, 1, 0x1);
    ImGui::InputInt("a4", &g_AnimationA4, 1, 0x1);
    if (ImGui::Button("YGO::UI::DrawDuelAnimation()"))
        YGO::UI::Draw_DuelAnimationFromId(static_cast<YGO::UI::Animations>(g_AnimationId), g_AnimationArg, g_AnimationA3, g_AnimationA4);
    if (ImGui::IsItemHovered())
    {
        switch (g_AnimationId)
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
    }

    void DrawGlobals()
    {
    ImGui::Text("YGO::DUEL::bIsDuelMultiplayer %d", YGO::DUEL::bIsDuelMultiplayer);
    ImGui::Text("YGO::DUEL::bIsRoundBasedDuel %d", YGO::DUEL::bIsRoundBasedDuel);
    ImGui::Text("YGO::DUEL::iDuelTimeLimit %d", YGO::DUEL::iDuelTimeLimit);

    ImGui::Text("YGO::DUEL::bIsTagDuel %d", YGO::DUEL::bIsTagDuel);
    ImGui::Text("YGO::DUEL::bIsTutorialDuel %d", YGO::DUEL::bIsTutorialDuel);
    ImGui::Text("YGO::DUEL::iTutorialDuelIndex %d", YGO::DUEL::iTutorialDuelIndex);
    }
}

namespace DebugTools
{
    void Init()
    {
        DuelTest::Install();

        if (!RIX::Load())
            return;

        // A named action menu files can call: { "action": { "call": "funky.toggleTools" } }
        RIX::Functions().RegisterAction("funky.toggleTools", &OnDebugToolsButton, nullptr);

        if (GetPrivateProfileIntA("Yu-Gi-Oh-Funky", "DemoMenuButtons", 0, ".\\Config.ini") == 0)
            return;

        RIX_ButtonDesc tools = RIX::Describe(L"Debug Tools", L"Show or hide the Yu-Gi-Oh-Funky debug window.", RIX_PAGE_MAIN, &OnDebugToolsButton);
        RIX::AddMainMenuButton(&tools);
    }

    void Draw()
    {
        DuelTest::LateInstall();   // once; every frame calls Draw, the window or not
        if (!g_ShowTools)
            return;

        if (ImGui::Begin("Yu-Gi-Oh! Debug Tools", &g_ShowTools))
        {
            if (ImGui::BeginTabBar("tools"))
            {
                if (ImGui::BeginTabItem("Save")) { DrawSave(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Screens")) { DrawScreens(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Main menu")) { DrawMainMenu(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Duel")) { DrawDuel(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Duel functions")) { DrawDuelFunctions(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Cards")) { DrawCardFunctions(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("UI")) { DrawUiFunctions(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("Globals")) { DrawGlobals(); ImGui::EndTabItem(); }
                if (ImGui::BeginTabItem("UI Witchcraft")) { UiWitchcraft::Draw(); ImGui::EndTabItem(); }
                ImGui::EndTabBar();
            }
        }
        ImGui::End();
    }
}
