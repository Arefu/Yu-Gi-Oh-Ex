#include <Windows.h>

#include "Save.h"

// The save redirect (savegame-ex.dat) lives in Yu-Gi-Oh-Core now, so it is on whether or not this plugin is. What is left here are the two
// small helpers the card code uses.

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
