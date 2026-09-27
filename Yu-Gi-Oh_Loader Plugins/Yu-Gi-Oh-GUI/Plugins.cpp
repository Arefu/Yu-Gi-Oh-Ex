#include "imgui.h"
#include "imgui_internal.h"
#include "Logger.h"
#include "Plugins.h"
#include "Yu-Gi-Oh-Core.h"
#include <set>
#include <string>
#include <Windows.h>

// A plugin that faults must not take the game down with it: its entry points are called guarded (SEH), and the failure is logged.
static bool CallGuarded(FARPROC entry)
{
    __try
    {
        reinterpret_cast<void(__stdcall*)()>(entry)();
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

static bool CallInputGuarded(FARPROC entry, HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    __try
    {
        reinterpret_cast<void(__stdcall*)(HWND, UINT, WPARAM, LPARAM)>(entry)(hWnd, msg, wParam, lParam);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

static std::set<HMODULE> g_HaveContext;   // plugins that were handed the ImGui context
static std::set<HMODULE> g_Failed;        // plugins that faulted and are not called any more

void PluginManager::ProcessGui()
{
    if (!Core::Load())
        return;

    ImGuiContext* context = ImGui::GetCurrentContext();
    const int count = Core::Functions().GetPluginCount();
    for (int i = 0; i < count; ++i)
    {
        CorePluginInfo info = {};
        info.Size = sizeof(info);
        if (!Core::Functions().GetPluginInfo(i, &info) || !info.Gui || !info.Loaded || !info.Module || g_Failed.count(info.Module))
            continue;

        if (g_HaveContext.insert(info.Module).second)
        {
            if (auto SetContext = GetProcAddress(info.Module, "SetContext"))
                reinterpret_cast<void(__stdcall*)(ImGuiContext*)>(SetContext)(context);
        }

        auto Entry = GetProcAddress(info.Module, "ProcessWindow");
        if (!Entry)
            continue;

        const int depth = context->CurrentWindowStack.Size;
        if (!CallGuarded(Entry))
        {
            // Close whatever windows it left open so the frame can still end, then stop calling it.
            while (context->CurrentWindowStack.Size > depth)
                ImGui::End();
            Logger::WriteLog(std::string(info.Name) + " crashed while drawing its window and is not drawn any more", MODULE_NAME, 2);
            g_Failed.insert(info.Module);
        }
    }
}

void PluginManager::ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (!Core::Load())
        return;

    const int count = Core::Functions().GetPluginCount();
    for (int i = 0; i < count; ++i)
    {
        CorePluginInfo info = {};
        info.Size = sizeof(info);
        if (!Core::Functions().GetPluginInfo(i, &info) || !info.Gui || !info.Loaded || !info.Module || g_Failed.count(info.Module))
            continue;

        if (auto Entry = GetProcAddress(info.Module, "ProcessInput"))
        {
            if (!CallInputGuarded(Entry, hWnd, msg, wParam, lParam))
                g_Failed.insert(info.Module);
        }
    }
}
