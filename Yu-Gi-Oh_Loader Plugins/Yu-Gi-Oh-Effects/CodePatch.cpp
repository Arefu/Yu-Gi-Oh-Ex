#include <Windows.h>
#include <detours.h>
#include <cstring>
#include <format>

#include "CodePatch.h"
#include "Logger.h"

void* CodePatch::AllocNearImage(size_t size)
{
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(kImageBase);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(kImageBase + dos->e_lfanew);
    uintptr_t address = (kImageBase + nt->OptionalHeader.SizeOfImage + 0xFFFF) & ~static_cast<uintptr_t>(0xFFFF);
    for (; address < kImageBase + 0x7FFF0000; address += 0x10000)
    {
        if (void* memory = VirtualAlloc(reinterpret_cast<void*>(address), size, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE))
            return memory;
    }
    return nullptr;
}

bool CodePatch::WriteBytes(uintptr_t address, const void* bytes, size_t size)
{
    DWORD old = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), size, PAGE_EXECUTE_READWRITE, &old))
        return false;
    std::memcpy(reinterpret_cast<void*>(address), bytes, size);
    VirtualProtect(reinterpret_cast<void*>(address), size, old, &old);
    FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(address), size);
    return true;
}

namespace
{
    // pushfq; push the volatile registers and xmm0-5; call the refresh function; restore; jmp to the Detours trampoline (the original prologue).
    // Nothing the caller can see changes, so callers built with whole-program register assumptions are safe.
    constexpr uint8_t kHead[] = {
        0x9C,                                       // pushfq
        0x50, 0x51, 0x52,                           // push rax, rcx, rdx
        0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53, // push r8, r9, r10, r11
        0x48, 0x81, 0xEC, 0x88, 0x00, 0x00, 0x00,   // sub rsp, 0x88 (shadow 0x20 + xmm 0x60 + 8: rsp 16-aligned for the call)
        0xF3, 0x0F, 0x7F, 0x44, 0x24, 0x20,         // movdqu [rsp+0x20], xmm0
        0xF3, 0x0F, 0x7F, 0x4C, 0x24, 0x30,         // movdqu [rsp+0x30], xmm1
        0xF3, 0x0F, 0x7F, 0x54, 0x24, 0x40,         // movdqu [rsp+0x40], xmm2
        0xF3, 0x0F, 0x7F, 0x5C, 0x24, 0x50,         // movdqu [rsp+0x50], xmm3
        0xF3, 0x0F, 0x7F, 0x64, 0x24, 0x60,         // movdqu [rsp+0x60], xmm4
        0xF3, 0x0F, 0x7F, 0x6C, 0x24, 0x70,         // movdqu [rsp+0x70], xmm5
        0x48, 0xB8,                                 // mov rax, imm64 (the refresh function, written after)
    };
    constexpr uint8_t kTail[] = {
        0xFF, 0xD0,                                 // call rax
        0xF3, 0x0F, 0x6F, 0x44, 0x24, 0x20,         // movdqu xmm0, [rsp+0x20]
        0xF3, 0x0F, 0x6F, 0x4C, 0x24, 0x30,
        0xF3, 0x0F, 0x6F, 0x54, 0x24, 0x40,
        0xF3, 0x0F, 0x6F, 0x5C, 0x24, 0x50,
        0xF3, 0x0F, 0x6F, 0x64, 0x24, 0x60,
        0xF3, 0x0F, 0x6F, 0x6C, 0x24, 0x70,
        0x48, 0x81, 0xC4, 0x88, 0x00, 0x00, 0x00,   // add rsp, 0x88
        0x41, 0x5B, 0x41, 0x5A, 0x41, 0x59, 0x41, 0x58, // pop r11, r10, r9, r8
        0x5A, 0x59, 0x58,                           // pop rdx, rcx, rax
        0x9D,                                       // popfq
        0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,         // jmp qword ptr [rip+0] -> the slot that follows
    };
    static_assert(sizeof(kHead) == 57 && sizeof(kTail) == 63, "entry stub layout");
    constexpr size_t kStubSlot = sizeof(kHead) + sizeof(void*) + sizeof(kTail);   // the jump slot, the stub's last 8 bytes

    uint8_t* g_StubPage = nullptr;
    size_t g_StubUsed = 0;

    uint8_t* NewEntryStub(void(__cdecl* refresh)())
    {
        constexpr size_t kSize = kStubSlot + sizeof(void*);
        if (!g_StubPage || g_StubUsed + kSize > 0x1000)
        {
            g_StubPage = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
            g_StubUsed = 0;
            if (!g_StubPage)
                return nullptr;
        }
        uint8_t* stub = g_StubPage + g_StubUsed;
        g_StubUsed += (kSize + 15) & ~static_cast<size_t>(15);
        std::memcpy(stub, kHead, sizeof(kHead));
        std::memcpy(stub + sizeof(kHead), &refresh, sizeof(void*));
        std::memcpy(stub + sizeof(kHead) + sizeof(void*), kTail, sizeof(kTail));
        std::memset(stub + kStubSlot, 0, sizeof(void*));
        return stub;
    }
}

// The stub's last 8 bytes are its jump slot: handed to Detours as the pointer it fills with the trampoline when the transaction commits.
bool CodePatch::AttachRefresh(uintptr_t entry, void(__cdecl* refresh)(), const char* label)
{
    uint8_t* stub = NewEntryStub(refresh);
    if (!stub)
        return false;
    PVOID* slot = reinterpret_cast<PVOID*>(stub + kStubSlot);
    *slot = reinterpret_cast<PVOID>(entry);
    FlushInstructionCache(GetCurrentProcess(), stub, kStubSlot + sizeof(void*));
    const LONG error = DetourAttach(slot, stub);
    if (error != NO_ERROR)
        Logger::WriteLog(std::format("{}: DetourAttach 0x{:X} failed ({})", label, entry, error), MODULE_NAME, 2);
    return error == NO_ERROR;
}
