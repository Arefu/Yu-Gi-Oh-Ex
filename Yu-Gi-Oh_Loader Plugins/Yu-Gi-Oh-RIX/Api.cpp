// The functions other plugins call, declared in Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h. Plain C so any language can use them.
#include <Windows.h>
#include <cstdint>
#include <format>
#include <memory>
#include <string>

#define RIX_EXPORTS
#include "Yu-Gi-Oh-RIX.h"
#include "Pages.h"
#include "Logger.h"
#include "MainMenu.h"
#include "PluginMenu.h"

#include "YuGiOh/YuGiOh-RIX.h"

extern "C"
{
    int __cdecl RIX_GetVersion(void) { return RIX_API_VERSION; }

    int __cdecl RIX_RegisterPluginOptions(const char* key, const wchar_t* label, const wchar_t* description, RIX_ButtonCallback callback, void* user)
    {
        return PluginMenu::RegisterOptions(key, label, description, callback, user) ? 1 : 0;
    }

    int __cdecl RIX_AddMainMenuButton(const RIX_ButtonDesc* button) { return button ? Menu::Add(*button) : -1; }

    int __cdecl RIX_RemoveMainMenuButton(int id) { return Menu::Remove(id) ? 1 : 0; }

    int __cdecl RIX_UpdateMainMenuButton(int id, const RIX_ButtonDesc* button) { return button && Menu::Update(id, *button) ? 1 : 0; }

    int __cdecl RIX_GetMainMenuButton(int id, RIX_ButtonDesc* out) { return out && Menu::Get(id, *out) ? 1 : 0; }

    int __cdecl RIX_RegisterAction(const char* name, RIX_ButtonCallback callback, void* user)
    {
        return name && *name && callback && Menu::RegisterAction(name, callback, user) ? 1 : 0;
    }

    int __cdecl RIX_GetMainMenuButtonCount(void) { return Menu::ActiveCount(); }

    int __cdecl RIX_GetMainMenuButtonIdAt(int index) { return Menu::ActiveIdAt(index); }

    int __cdecl RIX_PressMainMenuItem(int item) { return Menu::Press(item) ? 1 : 0; }

    int __cdecl RIX_IsMainMenuOpen(void) { return Menu::IsOpen() ? 1 : 0; }

    int __cdecl RIX_GetCurrentScreenId(void) { return Menu::IsOpen() ? RIX_SCREEN_MAIN_MENU : YGO::RIX::CurrentScreenId(); }

    int __cdecl RIX_GotoScreen(int screenId)
    {
        if (screenId < 0)
            return 0;

        // Go through the game's own GotoScreen from the screen that is showing, so the target remembers where it was entered from
        // (its Back button uses that). Without a screen object to go from, fall back to a plain navigation.
        void* from = Menu::CallbackSource();
        if (!from)
            from = YGO::RIX::FindScreenObject(YGO::RIX::CurrentScreenId());
        if (from)
        {
            const int fromId = *reinterpret_cast<int*>(static_cast<char*>(from) + 56);
            const char result = YGO::RIX::GotoScreenFrom(from, screenId);
            void* target = YGO::RIX::FindScreenObject(screenId);
            Logger::WriteLog(std::format("Go to screen {} from screen object {:p} (own id {}, current {}): result {}, its Back will go to {}",
                screenId, from, fromId, YGO::RIX::CurrentScreenId(), static_cast<int>(result),
                target ? *reinterpret_cast<int*>(static_cast<char*>(target) + 60) : -1), MODULE_NAME, 0);
            return result ? 1 : 0;
        }

        return YGO::RIX::NavigateToScreen(YGO::RIX::GetUI(), screenId, 0.15, 273, 1) ? 1 : 0;
    }

    int __cdecl RIX_SetMainMenuItemAction(int item, RIX_ButtonCallback callback, void* user)
    {
        return Menu::SetVanillaAction(item, callback, user) ? 1 : 0;
    }

    int __cdecl RIX_OpenPage(const RIX_PageDesc* page)
    {
        return page && Pages::Open(*page) ? 1 : 0;
    }

    int __cdecl RIX_ClosePage(void)
    {
        return Pages::Close() ? 1 : 0;
    }

    int __cdecl RIX_UpdatePageButton(int index, const RIX_PageButton* button)
    {
        return button && Pages::UpdateButton(index, *button) ? 1 : 0;
    }

    // ---- widgets on a screen: one handle per node, owning the node's reference and (for text) the string the game keeps reading.
    struct WidgetHandle
    {
        YGO::RIX::SharedNode Node{};
        void* Screen = nullptr;
        std::wstring Text;
    };

    void* __cdecl RIX_AddImage(void* screen, const char* resource, const char* sprite, int z, float x, float y, float width, float height)
    {
        if (!screen || !resource || !sprite)
            return nullptr;
        auto handle = std::make_unique<WidgetHandle>();
        handle->Screen = screen;
        handle->Node = YGO::RIX::Dfx::AddImage(YGO::RIX::ScreenRoot(screen), resource, sprite, z, x, y, width, height);
        return handle->Node.Node ? handle.release() : nullptr;
    }

    void* __cdecl RIX_AddText(void* screen, const wchar_t* text, int z, float x, float y, float width, float pixelSize, int align, uint32_t colour)
    {
        if (!screen || !text)
            return nullptr;
        auto handle = std::make_unique<WidgetHandle>();
        handle->Screen = screen;
        handle->Text = text;
        const auto alignment = align == RIX_ALIGN_CENTRE ? YGO::RIX::Dfx::TextAlign::Centre
                             : align == RIX_ALIGN_RIGHT ? YGO::RIX::Dfx::TextAlign::Right : YGO::RIX::Dfx::TextAlign::Left;
        handle->Node = YGO::RIX::Dfx::AddText(YGO::RIX::ScreenRoot(screen), handle->Text.c_str(), z, x, y, width, pixelSize, alignment, colour);
        return handle->Node.Node ? handle.release() : nullptr;
    }

    void __cdecl RIX_RemoveNode(void* node)
    {
        std::unique_ptr<WidgetHandle> handle(static_cast<WidgetHandle*>(node));
        if (handle)
            YGO::RIX::Dfx::RemoveImage(YGO::RIX::ScreenRoot(handle->Screen), handle->Node);
    }
}
