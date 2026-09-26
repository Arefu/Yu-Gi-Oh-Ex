#include <Windows.h>
#include <cstring>
#include <format>
#include <vector>

#include "Detours.h"
#include "Limit.h"
#include "Logger.h"

namespace
{
    struct BytePatch
    {
        uintptr_t address;
        std::vector<unsigned char> bytes;
    };

    // Page protection is deliberately not restored: several of these addresses started life as
    // non-executable data and must stay executable once they hold a jump stub.
    void ApplyBytePatch(const BytePatch& patch)
    {
        void* target = reinterpret_cast<void*>(patch.address);
        DWORD oldProtect = 0;

        if (!VirtualProtect(target, patch.bytes.size(), PAGE_EXECUTE_READWRITE, &oldProtect))
            return;

        std::memcpy(target, patch.bytes.data(), patch.bytes.size());
    }

    void ApplyBytePatches(const std::vector<BytePatch>& patches)
    {
        for (const auto& patch : patches)
            ApplyBytePatch(patch);
    }

    // mov <reg>, imm64 ; ret - a stub that hands a pointer to our table to a `call` site.
    void EmplacePointerStub(uintptr_t at, const void* ptr, uint8_t regOpcodeByte)
    {
        unsigned char stub[11];
        stub[0] = 0x48;
        stub[1] = regOpcodeByte;
        std::memcpy(&stub[2], &ptr, sizeof(void*));
        stub[10] = 0xC3;

        void* target = reinterpret_cast<void*>(at);
        DWORD oldProtect = 0;
        if (!VirtualProtect(target, sizeof(stub), PAGE_EXECUTE_READWRITE, &oldProtect))
            return;

        std::memcpy(target, stub, sizeof(stub));
    }

    // Only use this on a genuine CALL site (E8 xx xx xx xx).
    void PatchCallTarget(uintptr_t at, uintptr_t dest)
    {
        void* target = reinterpret_cast<void*>(at);
        DWORD oldProtect = 0;
        if (!VirtualProtect(target, 7, PAGE_EXECUTE_READWRITE, &oldProtect))
            return;

        int32_t rel = static_cast<int32_t>(dest - (at + 5));
        unsigned char callBytes[7];
        callBytes[0] = 0xE8;
        std::memcpy(&callBytes[1], &rel, sizeof(rel));
        callBytes[5] = 0x90;
        callBytes[6] = 0x90;
        std::memcpy(target, callBytes, sizeof(callBytes));
    }
}

static const uintptr_t InternalIdCallSites[] = {
    0x14076D11E, 0x14076D09E, 0x14076D44B, 0x14076D4B5,
    0x14076D5D8, 0x14076D668, 0x14076D6B8,
};

static const uintptr_t KonamiIdCallSites[] = {
    0x14076C0A9,
};

// Internal id -> Konami id. The original reads a table with a 0x27B6 bound check; the bound
// is widened so custom internal ids resolve too.
static uintptr_t orig_Get_KonamiIdFromInternalId = 0x14076D7F0;

int64_t __fastcall Limits::Get_KonamiIndexLookup(unsigned int a1)
{
    if (a1 >= kWidenedIdLimit)
        return 0;

    return static_cast<int64_t>(Limits::KonamiIDTable[a1]);
}

void Limits::RedirectInternalIDTable()
{
    Logger::WriteLog("RedirectInternalIDTable: writing stub and patching call sites", MODULE_NAME, 0);

    EmplacePointerStub(kInternalCardIdLocation, Limits::InternalIDTable, 0xB9);

    for (uintptr_t site : InternalIdCallSites)
    {
        PatchCallTarget(site, kInternalCardIdLocation);
        Logger::WriteLog(std::format("patched internal id call site 0x{:X}", site), MODULE_NAME, 0);
    }
}

void Limits::RedirectKonamiIDTable()
{
    Logger::WriteLog("RedirectKonamiIDTable: writing stub and patching call sites", MODULE_NAME, 0);

    EmplacePointerStub(kKonamiCardIdLocation, Limits::KonamiIDTable, 0xBF);

    for (uintptr_t site : KonamiIdCallSites)
    {
        PatchCallTarget(site, kKonamiCardIdLocation);
        Logger::WriteLog(std::format("patched konami id call site 0x{:X}", site), MODULE_NAME, 0);
    }

    // The stubs are rewritten every time the game reloads its tables; the hook is attached once.
    static bool hooked = false;
    if (hooked)
        return;

    hooked = true;
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    LONG err = DetourAttach(&(PVOID&)orig_Get_KonamiIdFromInternalId, Limits::Get_KonamiIndexLookup);
    LONG commitErr = DetourTransactionCommit();
    Logger::WriteLog(std::format("DetourAttach Get_KonamiIdFromInternalId: {}, commit: {}", err, commitErr), MODULE_NAME, err == 0 && commitErr == 0 ? 0 : 2);
}

void Limits::RemoveVanillaCountLimit()
{
    static const BytePatch cmpEax = { 0x140753BB1, { 0x90, 0x90, 0x90, 0x90, 0x90 } };
    static const BytePatch jnb = { 0x140753BB6, { 0x90, 0x90 } };
    ApplyBytePatch(cmpEax);
    ApplyBytePatch(jnb);
}

void Limits::WidenTrunkScanBounds()
{
    static const std::vector<BytePatch> patches = {
        { 0x1407F9B66, { 0xB8, 0x30, 0x10, 0x01, 0x00 } },
        { 0x1407F9D14, { 0x48, 0x81, 0xC4, 0x30, 0x10, 0x01, 0x00 } },
        { 0x1407F9B7D, { 0x48, 0x89, 0x84, 0x24, 0x00, 0x10, 0x01, 0x00 } },
        { 0x1407F9BA7, {
            0x48, 0x89, 0xAC, 0x24, 0x68, 0x10, 0x01, 0x00, 0x48, 0x8D, 0x4C, 0x24, 0x40, 0x48, 0x89, 0xB4,
            0x24, 0x28, 0x10, 0x01, 0x00, 0x33, 0xD2, 0x48, 0x89, 0xBC, 0x24, 0x20, 0x10, 0x01, 0x00, 0x41,
            0xB8, 0xFF, 0xFF, 0x00, 0x00, 0x4C, 0x89, 0xB4, 0x24, 0x18, 0x10, 0x01, 0x00, 0x4C, 0x89, 0xBC,
            0x24, 0x10, 0x10, 0x01, 0x00
        } },
        { 0x1407F9CB6, { 0x81, 0xFD, 0xFF, 0xFF, 0x00, 0x00 } },
        { 0x1407F9CC3, {
            0x4C, 0x8B, 0xBC, 0x24, 0x10, 0x10, 0x01, 0x00, 0x4C, 0x8B, 0xB4, 0x24, 0x18, 0x10, 0x01, 0x00,
            0x48, 0x8B, 0xBC, 0x24, 0x20, 0x10, 0x01, 0x00, 0x0F, 0xB7, 0x08, 0xB8, 0x28, 0x00, 0x00, 0x00,
            0x48, 0x8B, 0xB4, 0x24, 0x28, 0x10, 0x01, 0x00, 0x48, 0x8B, 0xAC, 0x24, 0x68, 0x10, 0x01, 0x00
        } },
        { 0x1407F9D04, { 0x48, 0x8B, 0x8C, 0x24, 0x00, 0x10, 0x01, 0x00 } },
        { 0x1408C0061, { 0x81, 0xFB, 0xFF, 0xFF, 0x00, 0x00 } },
        { 0x1408C0025, { 0x42, 0x89, 0x14, 0x80, 0x90, 0x90, 0x90, 0x90 } },
        { 0x1408BFF49, { 0xBB, 0xFF, 0xFF, 0x00, 0x00 } },
        { 0x1408BEFCD, { 0x90, 0x90 } },
        { 0x1408BEFD9, { 0x4C, 0x63, 0x04, 0x90, 0x90, 0x90, 0x90, 0x90 } },
    };
    ApplyBytePatches(patches);
}

void Limits::WidenIllustrationLoadBounds()
{
    // Disabled: widening these is unsafe against the fixed-size inline arrays they index.
    Logger::WriteLog("WidenIllustrationLoadBounds: skipped", MODULE_NAME, 0);
}

void Limits::ApplyAll()
{
    RedirectInternalIDTable();
    RedirectKonamiIDTable();
    RemoveVanillaCountLimit();
    WidenTrunkScanBounds();
    WidenIllustrationLoadBounds();
}
