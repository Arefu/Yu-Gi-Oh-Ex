#pragma once

// Where the game's files come from, in the order [Yu-Gi-Oh-Core] FileOrder says (Config.ini):
//
//   * loose files - <game>\<FolderName>\<path>, when LooseLoading=1 (FolderName default YGO_2020; Loading.h).
//   * WolfX's patch archive - the game's files WolfX changed, kept next to the game's archive instead of inside it, so YGO_2020.dat / .toc
//     are never written (deleting the two patch files undoes every change). Mounted whenever it exists, nothing to switch on:
//       <game>\YGO_2020-Ex.toc   "UB\n", then per file: u32 length of the path, the path (e.g. bin\CARD_Prop.bin), u64 size, u64 offset
//       <game>\YGO_2020-Ex.dat   the files, each padded to 4 bytes
//     PatchArchive names it (default YGO_2020-Ex; empty = off).
//   * the mods that are on (Yu-Gi-Oh-Mods.h, docs/Mods.md), the last in the load order first, each with the same two kinds of file:
//       <game>\Mods\<id>\YGO_2020\<path>          loose files (always served; listed once at the first file the game asks for)
//       <game>\Mods\<id>\YGO_2020-Ex.toc / .dat   a patch archive like WolfX's
//   * the game's own archive - anything none of them has.
//
// The game folder's own files beat every mod, a later mod beats an earlier one. Inside one of those: FileOrder = loose (default): a loose
// file wins over the patch; patch: the patch wins over loose files. WolfX reads the same settings, so what it shows is what the game loads
// (it doesn't show the mods).
//
// How (names as in the IDB): the game keeps its archives in g_Archives (0x1429241A0), but that holds ONE pointer (g_ArchiveCount is right
// after it), so the patch can't be added to the list. Instead the patch is mounted the way the game mounts YGO_2020 (Archives_Mount
// 0x14080E1F0: a 184-byte archive object, its constructor 0x14080C3A0, FS::LoadDAT 0x14080D3D0, which reads the binary "UB" toc), kept
// aside, and the per-archive functions are sent to it for the files it has:
//   Load_FileContent  0x14080DDE0  whole files (bins, pictures, small .zibs) - also serves the loose files
//   Archive_OpenEntry 0x14080DD40  files read as streams: the 600 MB card art .zibs (CardArt_OpenZibs -> Zib_OpenStream -> Stream_Open),
//                                  decks.zib, ... The entry handle remembers its archive, so every later read follows it.
//   Archive_GetFileSize 0x14080D290, Archive_HasFile 0x14080D270  so sizes match the file served (LoadFileFromArchives allocates by it)
// A loose file can only be served whole (there is no archive object for the loose folder), so a stream always comes from the patch or
// the game's archive.
namespace Patch
{
    bool Install();
}
