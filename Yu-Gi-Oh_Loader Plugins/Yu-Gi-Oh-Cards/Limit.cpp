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

    // Each id table site is a 7-byte `lea <reg>, [game table]` (checked in the IDB). It is detoured (Detours, like every other hook)
    // to a stub in memory this DLL owns - mov <reg>, imm64 (our table) ; jmp qword ptr [rip+0] ; dq site + 7 - so the register
    // gets our table and execution carries on after the lea. The trampoline Detours builds is never used. Earlier versions wrote a
    // `mov reg, imm64 ; ret` over the start of the game's own table and patched the lea into a call to it, which the game undid
    // whenever it reloaded the table.
    constexpr size_t kLeaLength = 7;

    uint8_t* NewStub(size_t size)
    {
        static uint8_t* page = nullptr;
        static size_t used = 0;
        if (!page || used + size > 0x1000)
        {
            page = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
            used = 0;
            if (!page)
                return nullptr;
        }
        uint8_t* stub = page + used;
        used += (size + 15) & ~static_cast<size_t>(15);
        return stub;
    }

    bool DetourLeaToTable(uintptr_t site, const void* table, uint8_t movRegOpcode)
    {
        uint8_t* stub = NewStub(24);
        if (!stub)
            return false;
        const uintptr_t resume = site + kLeaLength;
        stub[0] = 0x48;                 // REX.W
        stub[1] = movRegOpcode;         // B8+reg: mov reg, imm64
        std::memcpy(&stub[2], &table, sizeof(void*));
        stub[10] = 0xFF;                // jmp qword ptr [rip+0]
        stub[11] = 0x25;
        std::memset(&stub[12], 0, 4);
        std::memcpy(&stub[16], &resume, sizeof(resume));
        FlushInstructionCache(GetCurrentProcess(), stub, 24);

        void* target = reinterpret_cast<void*>(site);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        LONG error = DetourAttach(&target, stub);
        if (error == NO_ERROR)
            error = DetourTransactionCommit();
        else
            DetourTransactionAbort();
        Logger::WriteLog(std::format("id table site 0x{:X} -> our table: {}", site, error == NO_ERROR ? "ok" : std::format("Detours error {}", error)),
            MODULE_NAME, error == NO_ERROR ? 0 : 2);
        return error == NO_ERROR;
    }
}

// lea rcx, g_iInternalIDs sites (the id lookups).
static const uintptr_t InternalIdCallSites[] = {
    0x14076D11E, 0x14076D09E, 0x14076D44B, 0x14076D4B5,
    0x14076D5D8, 0x14076D668, 0x14076D6B8,
};

// lea rdi, g_KonamiIds site (Setup_CardPropTable clears and fills the table through rdi).
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
    static bool attached = false;   // the stubs live in our memory, so once is enough even when the game reloads its tables
    if (attached)
        return;
    attached = true;
    for (uintptr_t site : InternalIdCallSites)
        DetourLeaToTable(site, Limits::InternalIDTable, 0xB9);   // mov rcx, imm64
}

void Limits::RedirectKonamiIDTable()
{
    // Attached once (the stubs and the lookup detour live in this DLL).
    static bool hooked = false;
    if (hooked)
        return;

    hooked = true;
    for (uintptr_t site : KonamiIdCallSites)
        DetourLeaToTable(site, Limits::KonamiIDTable, 0xBF);   // mov rdi, imm64

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
