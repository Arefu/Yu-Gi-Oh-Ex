#include "../../Yu-Gi-Oh-GUI Plugins/Yu-Gi-Oh-BetterCardShop/Packs.cpp"
#include <iostream>

// Run the actual installed x64 patch against a synthetic shop screen. No game,
// profile or save is accessed. Reserve the game's patch address in this process.
int main()
{
    auto* region = static_cast<uint8_t*>(VirtualAlloc(reinterpret_cast<void*>(0x140850000),
        0x10000, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    if (!region) return 1;
    auto* site = region + 0xBCA5;
    const uint8_t original[] = {0x48, 0x69, 0xC8, 0xD8, 0, 0, 0, 0x48, 0x89, 0xC8, 0xC3};
    std::memcpy(site, original, sizeof(original));
    site[0] = 0x90;
    if (FixScrolledPurchaseIndex()) return 2; // Unsupported bytes must stay untouched.
    if (site[0] != 0x90) return 3;
    std::memcpy(site, original, sizeof(original));
    if (!FixScrolledPurchaseIndex()) return 4;

    // Windows x64 arguments: ECX = selection, RDX = synthetic screen.
    // Preserve RDI, put the selection in RAX, call the patch, return RCX.
    uint8_t wrapper[] = {0x57, 0x48, 0x89, 0xD7, 0x48, 0x63, 0xC1,
        0xE8, 0, 0, 0, 0, 0x5F, 0xC3};
    const int32_t relative = static_cast<int32_t>(site - (region + 12));
    std::memcpy(wrapper + 8, &relative, sizeof(relative));
    std::memcpy(region, wrapper, sizeof(wrapper));
    FlushInstructionCache(GetCurrentProcess(), region, 0x10000);
    auto invoke = reinterpret_cast<int64_t(*)(int, void*)>(region);
    alignas(8) uint8_t screen[0x1200]{};
    int checks = 0;
    for (int total = 1; total <= 128; ++total)
        for (int first = 0; first <= (std::max)(0, total - 6); ++first)
            for (int visible = 0; visible < (std::min)(6, total - first); ++visible)
            {
                *reinterpret_cast<int*>(screen + 0x11B8) = first;
                if (invoke(first + visible, screen) != visible * 0xD8) return 5;
                ++checks;
            }
    std::cout << checks << " purchase-index cases passed; unsupported bytes rejected\n";
    VirtualFree(region, 0, MEM_RELEASE);
}
