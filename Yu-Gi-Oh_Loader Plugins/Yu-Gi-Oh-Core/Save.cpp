#include <Windows.h>
#include <array>
#include <cstring>
#include <filesystem>
#include <format>
#include <fstream>
#include <mutex>
#include <vector>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"

// The save is a 44008 byte blob. The game writes it to Steam Cloud as "savegame.dat" and reads
// it back through ISteamRemoteStorage. The three functions that do that are always replaced with
// versions that use a local file (savegame-ex.dat in the game folder) and drive the game's save
// manager the same way, so the Steam save is never written and vanilla never sees custom cards.
constexpr size_t kSaveSize = 44008;

constexpr const char* kIniSection = "Yu-Gi-Oh-Core";
constexpr const char* kSteamSaveName = "savegame.dat";

constexpr uintptr_t kSteamContextInitIat = 0x1409F9930;    // SteamInternal_ContextInit (import)
constexpr uintptr_t kSteamRemoteStorageContext = 0x140C8EA50;

// ISteamRemoteStorage vtable slots (byte offsets 8, 104 and 120).
constexpr size_t kFileReadSlot = 1;
constexpr size_t kFileExistsSlot = 13;
constexpr size_t kGetFileSizeSlot = 15;

// Save manager result codes: 1 = loaded / written, 2 = no file, 4 = read failed, 7 = write failed.
constexpr int kResultOk = 1;
constexpr int kResultNoFile = 2;
constexpr int kResultWriteFailed = 7;

static uintptr_t orig_QuerySaveFileExists = 0x1408DA190;
static uintptr_t orig_ReadSaveFile = 0x1408D9F00;
static uintptr_t orig_WriteSaveFile = 0x1408DA240;

static constexpr uintptr_t Manager_SetPendingOp = 0x1408717B0;
static constexpr uintptr_t Manager_SetResultCode = 0x1408717D0;
static constexpr uintptr_t Manager_ProcessReadBuffer = 0x1408717E0;
static constexpr uintptr_t AllocReadBuffer = 0x1408D9450;
static constexpr uintptr_t FinalizeChecksum = 0x1407F8280;
static constexpr uintptr_t GameFree = 0x14090D2F4;

// The game's byte vector: begin, end, end of capacity.
struct GameBuffer
{
    unsigned char* Begin;
    unsigned char* End;
    unsigned char* Capacity;
};

// g_SavePath is the file the game reads and writes now; Save slots (SaveSlots.cpp) switch it. g_PrimarySavePath is the file
// GameSaveName names (slot 1), the only one that is ever seeded from the Steam save. The path is read on the game's save thread
// and changed on the UI thread, so it is only touched under g_PathLock.
static std::filesystem::path g_SavePath;
static std::filesystem::path g_PrimarySavePath;
static std::mutex g_PathLock;
static std::string g_IniPath;

static std::filesystem::path CurrentPath()
{
    std::lock_guard<std::mutex> guard(g_PathLock);
    return g_SavePath;
}

// ---------------------------------------------------------------------
// Volume shared by every save slot
// ---------------------------------------------------------------------

// The settings block at 0x14: +0 music volume, +4 sound effects / ambient volume (both 0..10, what ApplySavedAudioSettings reads),
// +8 and +12 unknown (left per slot). The volume of the last save read or written is put into every save the game reads, so it
// carries across slots. The header's checksum is at 12 (CRC-32, table 0xEDB88320, start 0xFFFFFFFF, no final inversion, over the
// file with the field zeroed).
constexpr uint32_t kSaveMagic = 0x54CE29F9;
constexpr size_t kChecksumOffset = 12;
constexpr size_t kVolumeOffset = 0x14;
constexpr size_t kVolumeSize = 8;

static std::mutex g_VolumeLock;
static bool g_VolumeKnown = false;
static unsigned char g_Volume[kVolumeSize];

static bool IsSave(const unsigned char* data, size_t size)
{
    uint32_t magic;
    std::memcpy(&magic, data, sizeof(magic));
    return size == kSaveSize && magic == kSaveMagic;
}

static uint32_t SaveChecksum(const unsigned char* data, size_t size)
{
    static const auto table = []
    {
        std::array<uint32_t, 256> t{};
        for (uint32_t i = 0; i < 256; ++i)
        {
            uint32_t c = i;
            for (int k = 0; k < 8; ++k)
                c = (c & 1) ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            t[i] = c;
        }
        return t;
    }();

    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < size; ++i)
    {
        const unsigned char b = (i >= kChecksumOffset && i < kChecksumOffset + 4) ? 0 : data[i];
        crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
    }
    return crc;
}

static void RememberVolume(const unsigned char* save)
{
    std::lock_guard<std::mutex> guard(g_VolumeLock);
    std::memcpy(g_Volume, save + kVolumeOffset, kVolumeSize);
    g_VolumeKnown = true;
}

// A save file about to be handed to the game: the first one read sets the shared volume, later ones get it (and a new checksum).
static void ShareVolume(std::vector<unsigned char>& file)
{
    if (!IsSave(file.data(), file.size()))
        return;

    std::lock_guard<std::mutex> guard(g_VolumeLock);
    if (!g_VolumeKnown)
    {
        std::memcpy(g_Volume, file.data() + kVolumeOffset, kVolumeSize);
        g_VolumeKnown = true;
        return;
    }
    if (std::memcmp(file.data() + kVolumeOffset, g_Volume, kVolumeSize) == 0)
        return;

    std::memcpy(file.data() + kVolumeOffset, g_Volume, kVolumeSize);
    const uint32_t crc = SaveChecksum(file.data(), file.size());
    std::memcpy(file.data() + kChecksumOffset, &crc, sizeof(crc));
    Logger::WriteLog("Volume carried over from the last save", MODULE_NAME, 0);
}

void Save::ApplySharedVolume(unsigned char* blob)
{
    std::lock_guard<std::mutex> guard(g_VolumeLock);
    if (blob && g_VolumeKnown)
        std::memcpy(blob + kVolumeOffset, g_Volume, kVolumeSize);
}

// A setting from [Yu-Gi-Oh-Core], or the default.
static int ReadIntSetting(const char* key, int fallback)
{
    return static_cast<int>(GetPrivateProfileIntA(kIniSection, key, static_cast<UINT>(fallback), g_IniPath.c_str()));
}
static std::mutex g_SeedLock;
static bool g_Seeded = false;

static void SetPendingOp(int64_t manager, int op)
{
    reinterpret_cast<void(__fastcall*)(int64_t, int)>(Manager_SetPendingOp)(manager, op);
}

static void SetResultCode(int64_t manager, int code)
{
    reinterpret_cast<void(__fastcall*)(int64_t, int)>(Manager_SetResultCode)(manager, code);
}

// Blocks of 0x1000 bytes or more carry the pointer they were allocated at in the 8 bytes before them.
static void FreeGameBuffer(GameBuffer& buffer)
{
    unsigned char* block = buffer.Begin;
    if (!block)
        return;

    if (buffer.Capacity - block >= 0x1000)
        block = *reinterpret_cast<unsigned char**>(block - sizeof(void*));

    reinterpret_cast<void(__fastcall*)(void*)>(GameFree)(block);
    buffer = {};
}

// ---------------------------------------------------------------------
// The Steam Cloud copy (only used to seed the local save once)
// ---------------------------------------------------------------------

static void* SteamRemoteStorage()
{
    using ContextInit_t = void** (__fastcall*)(void*);
    ContextInit_t contextInit = *reinterpret_cast<ContextInit_t*>(kSteamContextInitIat);
    void** context = contextInit(reinterpret_cast<void*>(kSteamRemoteStorageContext));
    return context ? *context : nullptr;
}

static bool ReadSteamSave(std::vector<unsigned char>& data)
{
    void* storage = SteamRemoteStorage();
    if (!storage)
        return false;

    void** vtable = *reinterpret_cast<void***>(storage);
    auto fileExists = reinterpret_cast<bool(__fastcall*)(void*, const char*)>(vtable[kFileExistsSlot]);
    auto getFileSize = reinterpret_cast<int(__fastcall*)(void*, const char*)>(vtable[kGetFileSizeSlot]);
    auto fileRead = reinterpret_cast<int(__fastcall*)(void*, const char*, void*, int)>(vtable[kFileReadSlot]);

    if (!fileExists(storage, kSteamSaveName))
        return false;

    int size = getFileSize(storage, kSteamSaveName);
    if (size <= 0)
        return false;

    data.resize(size);
    return fileRead(storage, kSteamSaveName, data.data(), size) == size;
}

// ---------------------------------------------------------------------
// The local save
// ---------------------------------------------------------------------

static bool WriteLocalSave(const std::filesystem::path& path, const unsigned char* data, size_t size)
{
    std::filesystem::path temp = path;
    temp += ".tmp";

    {
        std::ofstream file(temp, std::ios::binary | std::ios::trunc);
        if (!file.write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(size)))
            return false;
    }

    std::error_code error;
    std::filesystem::rename(temp, path, error);
    return !error;
}

static bool ReadLocalSave(const std::filesystem::path& path, std::vector<unsigned char>& data)
{
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file)
        return false;

    std::streamsize size = file.tellg();
    if (size <= 0)
        return false;

    data.resize(static_cast<size_t>(size));
    file.seekg(0);
    return static_cast<bool>(file.read(reinterpret_cast<char*>(data.data()), size));
}

// The first time the local save is asked for and doesn't exist, it starts as a copy of the
// Steam save (if there is one). After that the two are never synced. Only slot 1 (the GameSaveName file)
// is seeded: the other save slots start as new profiles.
static void SeedFromSteamOnce()
{
    std::lock_guard<std::mutex> guard(g_SeedLock);
    if (g_Seeded)
        return;

    const std::filesystem::path path = CurrentPath();
    if (path != g_PrimarySavePath)
        return;   // not seeded yet: slot 1 can still be seeded if it is picked later
    g_Seeded = true;

    std::error_code error;
    if (std::filesystem::exists(path, error))
        return;

    // SeedFromGameSave=0 (optional) starts the local save as a brand-new profile instead of a copy of the Steam save.
    if (ReadIntSetting("SeedFromGameSave", 1) == 0)
    {
        Logger::WriteLog(std::format("SeedFromGameSave=0: {} will start as a new profile", path.string()), MODULE_NAME, 0);
        return;
    }

    std::vector<unsigned char> steamSave;
    if (!ReadSteamSave(steamSave))
    {
        Logger::WriteLog(std::format("No Steam save to copy, {} will start as a new profile made by the game", path.string()), MODULE_NAME, 0);
        return;
    }

    if (WriteLocalSave(path, steamSave.data(), steamSave.size()))
        Logger::WriteLog(std::format("Copied the Steam save ({} bytes) to {}", steamSave.size(), path.string()), MODULE_NAME, 0);
    else
        Logger::WriteLog(std::format("Could not write {}", path.string()), MODULE_NAME, 2);
}

// ---------------------------------------------------------------------
// The redirected save functions. Each one mirrors what the game's own version tells the
// save manager, so the load / save state machines behave as they do with Steam.
// ---------------------------------------------------------------------

static void __fastcall Hook_QuerySaveFileExists(int64_t manager)
{
    SeedFromSteamOnce();

    std::error_code error;
    if (std::filesystem::exists(CurrentPath(), error))
    {
        SetPendingOp(manager, 1);
        SetResultCode(manager, 0);
    }
    else
    {
        SetResultCode(manager, kResultNoFile);
    }
}

static void __fastcall Hook_ReadSaveFile(int64_t manager)
{
    SeedFromSteamOnce();

    const std::filesystem::path path = CurrentPath();
    std::vector<unsigned char> file;
    if (!ReadLocalSave(path, file))
    {
        SetResultCode(manager, kResultNoFile);
        SetPendingOp(manager, 0);
        return;
    }
    ShareVolume(file);

    GameBuffer buffer{};
    reinterpret_cast<void(__fastcall*)(GameBuffer*, size_t)>(AllocReadBuffer)(&buffer, file.size());
    std::memcpy(buffer.Begin, file.data(), file.size());

    int result = static_cast<int>(reinterpret_cast<int64_t(__fastcall*)(int64_t, GameBuffer*)>(Manager_ProcessReadBuffer)(manager, &buffer));
    SetResultCode(manager, result);
    SetPendingOp(manager, 0);

    FreeGameBuffer(buffer);
    Logger::WriteLog(std::format("Read {} ({} bytes), result {}", path.string(), file.size(), result), MODULE_NAME, 0);
}

static void __fastcall Hook_WriteSaveFile(int64_t manager)
{
    SetPendingOp(manager, 2);
    SetResultCode(manager, 0);

    // The blob sits 20 bytes into the manager; FinalizeChecksum bumps the save counter and stores the CRC32.
    unsigned char* blob = reinterpret_cast<unsigned char*>(manager + 20);
    reinterpret_cast<void(__fastcall*)(void*)>(FinalizeChecksum)(blob);
    RememberVolume(blob);   // a volume change in Settings is saved like this; it is what the next slot gets

    const std::filesystem::path path = CurrentPath();
    bool ok = WriteLocalSave(path, blob, kSaveSize);
    SetResultCode(manager, ok ? kResultOk : kResultWriteFailed);
    SetPendingOp(manager, 0);

    if (!ok)
        Logger::WriteLog(std::format("Could not write {}", path.string()), MODULE_NAME, 2);
}

const std::filesystem::path& Save::PrimarySavePath()
{
    return g_PrimarySavePath;
}

std::filesystem::path Save::CurrentSavePath()
{
    return CurrentPath();
}

void Save::SetSavePath(const std::filesystem::path& path)
{
    {
        std::lock_guard<std::mutex> guard(g_PathLock);
        if (g_SavePath == path)
            return;
        g_SavePath = path;
    }
    Logger::WriteLog(std::format("The game now reads and writes {}", path.string()), MODULE_NAME, 0);
}

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

bool Save::Install()
{
    g_IniPath = Save::GameFolder() + "Config.ini";
    char name[MAX_PATH];
    GetPrivateProfileStringA(kIniSection, "GameSaveName", "savegame-ex.dat", name, MAX_PATH, g_IniPath.c_str());
    g_PrimarySavePath = std::filesystem::path(Save::GameFolder()) / (name[0] ? name : "savegame-ex.dat");
    g_SavePath = g_PrimarySavePath;

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    DetourAttach(&(PVOID&)orig_QuerySaveFileExists, Hook_QuerySaveFileExists);
    DetourAttach(&(PVOID&)orig_ReadSaveFile, Hook_ReadSaveFile);
    DetourAttach(&(PVOID&)orig_WriteSaveFile, Hook_WriteSaveFile);

    LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Using local save {} (Steam save untouched): {}", g_PrimarySavePath.string(), err), MODULE_NAME, err == 0 ? 0 : 2);
    return err == 0;
}
