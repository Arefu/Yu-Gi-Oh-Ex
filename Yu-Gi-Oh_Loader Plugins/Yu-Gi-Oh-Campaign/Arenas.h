#pragma once

// New duel arenas, from Yu-Gi-Oh-Ex/arenas.json (WolfX Campaign > Arenas):
//
//   { "arenas": [ { "id": 100, "folder": "my_arena", "name": { "E": "My Arena" } } ] }
//
// The game: Arena_LoadBackground (0x14076F200) is the only reader of g_ArenaFolderTable (0x140A52290, .rdata): {int id, char* folder}
// entries ended by a null folder (14 game arenas: 1-8, 13-18), and it loads "arenas/<folder>/animlist.txt" for the duel's arena id
// (an unknown id falls back to classic). Here the table is copied next to the exe with the new arenas added, and the function's two
// references (lea at 0x14076F246, cmp at 0x14076F25C) point at the copy. A duelist's or story duel's "arena" can then name a new id;
// its files (arenas/<folder>/...) go in the Ex patch.
namespace Arenas
{
    // Reads arenas.json and moves the table; call once at start (no Detours transaction needed: it patches two operands).
    void Install();
}
