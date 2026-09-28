#pragma once
#include <cstddef>
#include <cstdint>

#include "YuGiOh-CARDS.h"

// Card passwords, read from the game's own data: bin\CARD_Pass.bin in YGO_2020.dat. The game ships the file but never reads it. It is one
// uint32 password per INTERNAL card id (index 0 up to 10165; the tokens at the start have 0, Blue-Eyes White Dragon is internal 101 =
// 89631139). Traced in YuGiOh.exe.i64 (YGO::GAME::LoadFileFromArchives and friends).
//
// Header only on purpose: every plugin that includes it gets the same code and needs no other plugin at run time. The file is loaded
// through the game's own loader (Load_FileContent), so a plugin that hooks that loader for bin\CARD_Pass.bin (Yu-Gi-Oh-BetterLoad's loose
// files, or custom cards adding passwords for internal ids above the game's) is seen here too.
namespace YGO
{
    namespace ARCHIVE
    {
        // The loaded archives (the game only ever has one, YGO_2020): a static array and its count.
        inline void** const Archives = reinterpret_cast<void**>(0x1429241A0);
        inline const uint32_t& ArchiveCount = *reinterpret_cast<uint32_t*>(0x1429241A8);

        // Archive_GetFileSize: the size in the TOC, or -1. Load_FileContent: the file in a new game heap buffer (free with the game's _impl_free),
        // plus Padding zero bytes. Names are matched without case and with / or \.
        inline auto GetFileSize = reinterpret_cast<int64_t(__fastcall*)(void* Archive, const char* Name)>(0x14080D290);
        inline auto LoadFileContent = reinterpret_cast<void*(__fastcall*)(void* Archive, const char* Name, size_t Padding)>(0x14080DDE0);

        // A file from whichever archive has it, or null. Only call it once the game has loaded its data (the main menu is up).
        inline void* LoadFile(const char* Name, size_t* Size)
        {
            for (uint32_t i = 0; i < ArchiveCount; ++i)
            {
                void* archive = Archives[i];
                const int64_t size = archive ? GetFileSize(archive, Name) : -1;
                if (size < 0)
                    continue;
                void* data = LoadFileContent(archive, Name, 0);
                if (!data)
                    continue;
                if (Size)
                    *Size = static_cast<size_t>(size);
                return data;
            }
            return nullptr;
        }
    }

    namespace CARDS
    {
        // The whole CARD_Pass.bin, loaded the first time it is asked for and kept.
        inline const uint32_t* PasswordTable(size_t* Count)
        {
            static const uint32_t* table = nullptr;
            static size_t count = 0;
            if (!table)
            {
                size_t size = 0;
                table = static_cast<const uint32_t*>(ARCHIVE::LoadFile("bin\\CARD_Pass.bin", &size));
                count = table ? size / sizeof(uint32_t) : 0;
            }
            if (Count)
                *Count = count;
            return table;
        }

        // The card id (Konami id) whose password this is, or 0 when no card has it.
        inline uint16_t FindCardByPassword(uint32_t Password)
        {
            if (Password == 0)
                return 0;
            size_t count = 0;
            const uint32_t* table = PasswordTable(&count);
            // The internal id goes in ecx whole (custom cards' internal ids can pass 32767), hence the unsigned prototype.
            auto propsOf = reinterpret_cast<CARD_PROPS*(__fastcall*)(unsigned int)>(Get_CardPropsFromInternalId);
            for (size_t internal = 0; table && internal < count; ++internal)
            {
                if (table[internal] != Password)
                    continue;
                const CARD_PROPS* props = propsOf(static_cast<unsigned int>(internal));
                if (props && props->KonamiID != 0)
                    return static_cast<uint16_t>(props->KonamiID);
            }
            return 0;
        }
    }
}
