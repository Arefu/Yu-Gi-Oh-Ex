#include <Windows.h>

#include "Save.h"
#include "Yu-Gi-Oh-Core.h"

// The save redirect (savegame-ex.dat) lives in Yu-Gi-Oh-Core now, so it is on whether or not this plugin is. What is left here are the small
// helpers the card code uses.

// Get_CardUnlockTable(profile): the saved card ownership table of a profile.
static constexpr uintptr_t Get_CardUnlockTable = 0x1407F8130;

const std::string& Save::GameFolder()
{
    static const std::string folder = []
    {
        char path[MAX_PATH]{};
        GetModuleFileNameA(nullptr, path, MAX_PATH);
        std::string full(path);
        return full.substr(0, full.find_last_of('\\') + 1);
    }();
    return folder;
}

unsigned char* Save::GetCardUnlockTable(unsigned int profile)
{
    return reinterpret_cast<unsigned char*(__fastcall*)(unsigned int)>(Get_CardUnlockTable)(profile);
}

int Save::GetExtension(const char* tag, void* out, int size)
{
    return Core::Load() && Core::Functions().GetSaveExtension ? Core::Functions().GetSaveExtension(tag, out, size) : 0;
}

void Save::SetExtension(const char* tag, const void* data, int size)
{
    if (Core::Load() && Core::Functions().SetSaveExtension)
        Core::Functions().SetSaveExtension(tag, data, size);
}
