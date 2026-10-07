#include <fstream>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>

#include <Windows.h>
#include <detours.h>

#include "conmanip.h"
using namespace conmanip;
console_out_context ctxout;
console_out conout(ctxout);

#define MODULE_NAME "Yu-Gi-Oh-Console"

// A line looks like:
//
//   12:34:56.789 INFO  [Yu-Gi-Oh-MoreCards] Loaded 1 card(s) from cards.json
//
// The time is bright white, the level and the message are coloured by severity, and the module name has a colour of its own (the same
// every time, so a plugin is easy to follow in a busy log). Only one thread writes at a
// time: the game and its plugins log from several threads, and unsynchronised writes mix up both the text and the colours.
namespace
{
    std::mutex g_Lock;

    // Lowest severity that is shown: 0 debug, 1 info, 2 warn, 3 error. Set with LogLevel= in Config.ini ([Yu-Gi-Oh-Console]).
    int g_MinRank = 1;

    // console.log next to the exe: every line the console gets (plain text, no colours), from FileLogLevel= (default debug), so it can be read
    // later. It starts empty each time the game does.
    int g_FileRank = 0;
    std::ofstream g_File;

    // Flushing every line forces a syscall each time; a burst of thousands of lines (e.g. loading a big cards.json)
    // made that look like the game had hung. Flush in batches instead, and immediately for errors so a crash doesn't lose them.
    constexpr int kFlushEvery = 200;
    int g_UnflushedFile = 0, g_UnflushedConsole = 0;

    // Split logs (WriteLogTo): a plugin that wants its own file (a duel record, a diagnostic trace) names it, and gets
    // <name>.log next to console.log. These are appended to, not cleared, so they keep history across runs; each run
    // starts with a "session" line. Nothing written here goes to the console window or console.log.
    // They are flushed in batches like console.log, and also whenever a line comes kSplitFlushMs or more after the last flush:
    // a split log is usually a slow record (a duel), and a game that's closed or killed mid-duel lost everything since the
    // last batch. That caps it at two flushes a second, so bursts stay cheap.
    constexpr unsigned long long kSplitFlushMs = 500;
    struct SplitLog
    {
        std::ofstream File;
        int Unflushed = 0;
        unsigned long long LastFlush = 0;
    };
    std::map<std::string, std::unique_ptr<SplitLog>> g_SplitLogs;

    int RankOf(int level)
    {
        switch (level)
        {
        case 69: return 0;
        case 1:  return 2;
        case 2:  return 3;
        default: return 1;
        }
    }

    int ParseRank(const char* value)
    {
        std::string text = value;
        for (char& c : text)
            c = static_cast<char>(tolower(static_cast<unsigned char>(c)));

        if (text == "debug" || text == "all") return 0;
        if (text == "warn" || text == "warning") return 2;
        if (text == "error" || text == "err") return 3;
        return 1;
    }

    void LoadLogLevel()
    {
        char exe[MAX_PATH];
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        folder = folder.substr(0, folder.find_last_of("\\/") + 1);
        const std::string path = folder + "Config.ini";

        char value[32] = {};
        GetPrivateProfileStringA("Yu-Gi-Oh-Console", "LogLevel", "info", value, sizeof(value), path.c_str());
        g_MinRank = ParseRank(value);

        GetPrivateProfileStringA("Yu-Gi-Oh-Console", "FileLogLevel", "debug", value, sizeof(value), path.c_str());
        g_FileRank = ParseRank(value);
        g_File.open(folder + "console.log", std::ios::out | std::ios::trunc);
    }
    struct LevelStyle
    {
        const char* Tag;
        console_text_colors TagColor;
        console_text_colors MessageColor;
    };

    LevelStyle StyleFor(int level)
    {
        switch (level)
        {
        case 0:  return { "INFO ", console_text_colors::light_cyan, console_text_colors::white };
        case 1:  return { "WARN ", console_text_colors::light_yellow, console_text_colors::light_yellow };
        case 2:  return { "ERROR", console_text_colors::light_red, console_text_colors::light_red };
        case 69: return { "DEBUG", console_text_colors::light_magenta, console_text_colors::white };
        default: return { "LOG  ", console_text_colors::light_green, console_text_colors::white };
        }
    }

    // Each module name gets its own 24-bit colour the first time it logs (the 16 console colours ran out and names shared one). The hues
    // walk the colour wheel by the golden ratio from a random start each run, so every new name lands far from the ones before it.
    // Called under g_Lock.
    std::string ModuleColor(const std::string& module)
    {
        static std::map<std::string, std::string> colors;
        static double hue = (GetTickCount64() % 1000) / 1000.0;
        auto found = colors.find(module);
        if (found != colors.end())
            return found->second;

        hue = hue + 0.61803398875;
        hue -= static_cast<int>(hue);
        // HSV (hue, 0.6, 1.0) to RGB: bright and readable on the black console. Hues within ~40 degrees of red are skipped: red reads as an error.
        const double h = (0.11 + hue * 0.78) * 6.0, s = 0.6, v = 1.0;
        const int sector = static_cast<int>(h) % 6;
        const double fraction = h - static_cast<int>(h);
        const double p = v * (1 - s), q = v * (1 - s * fraction), t = v * (1 - s * (1 - fraction));
        double r = v, g = t, b = p;
        switch (sector)
        {
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        case 5: r = v; g = p; b = q; break;
        }
        std::string code = "\x1b[38;2;" + std::to_string(static_cast<int>(r * 255)) + ";" + std::to_string(static_cast<int>(g * 255)) + ";" +
            std::to_string(static_cast<int>(b * 255)) + "m";
        return colors.emplace(module, std::move(code)).first->second;
    }

    std::string Timestamp()
    {
        SYSTEMTIME now;
        GetLocalTime(&now);

        char text[16];
        snprintf(text, sizeof(text), "%02d:%02d:%02d.%03d", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
        return text;
    }

    void WriteModule(const std::string& module, int level)
    {
        std::cout << settextcolor(console_text_colors::white) << "[";   // the tag before it left its own colour on (red for ERROR)
        if (level == 69)
        {
            // Debug lines keep the rainbow module name.
            static const console_text_colors rainbow[] =
            {
                console_text_colors::red, console_text_colors::yellow, console_text_colors::green, console_text_colors::cyan,
                console_text_colors::blue, console_text_colors::magenta, console_text_colors::white,
            };
            for (size_t i = 0; i < module.length(); ++i)
                std::cout << settextcolor(rainbow[i % 7]) << module[i];
        }
        else
            std::cout << ModuleColor(module) << module << "\x1b[0m";

        std::cout << settextcolor(console_text_colors::white) << "] ";
    }
}

// Anything on the console that did not come through WriteLog is dropped. The game and Steam print things of their own (Steam's "Setting breakpad
// minidump AppID" and "Steam_SetMinidumpSteamID: Caching Steam ID: ..." lines, which give away the user's Steam ID, are two) and they would
// otherwise mix with the log. WriteLog marks its own thread while it writes; the console write calls (WriteFile / WriteConsole on a character
// device) are hooked and refuse everything else. Files and pipes are never touched, and each thread has its own mark, so output from another
// thread while WriteLog runs is still dropped.
namespace
{
    thread_local bool t_InWriteLog = false;

    bool IsConsoleHandle(HANDLE handle)
    {
        return GetFileType(handle) == FILE_TYPE_CHAR;
    }

    decltype(&WriteFile) orig_WriteFile = WriteFile;
    decltype(&WriteConsoleA) orig_WriteConsoleA = WriteConsoleA;
    decltype(&WriteConsoleW) orig_WriteConsoleW = WriteConsoleW;

    BOOL WINAPI Hook_WriteFile(HANDLE file, LPCVOID buffer, DWORD count, LPDWORD written, LPOVERLAPPED overlapped)
    {
        if (!t_InWriteLog && IsConsoleHandle(file))
        {
            if (written)
                *written = count;
            return TRUE;
        }
        return orig_WriteFile(file, buffer, count, written, overlapped);
    }

    BOOL WINAPI Hook_WriteConsoleA(HANDLE console, const VOID* buffer, DWORD count, LPDWORD written, LPVOID reserved)
    {
        if (!t_InWriteLog)
        {
            if (written)
                *written = count;
            return TRUE;
        }
        return orig_WriteConsoleA(console, buffer, count, written, reserved);
    }

    BOOL WINAPI Hook_WriteConsoleW(HANDLE console, const VOID* buffer, DWORD count, LPDWORD written, LPVOID reserved)
    {
        if (!t_InWriteLog)
        {
            if (written)
                *written = count;
            return TRUE;
        }
        return orig_WriteConsoleW(console, buffer, count, written, reserved);
    }

    void HideForeignConsoleOutput()
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_WriteFile, Hook_WriteFile);
        DetourAttach(&(PVOID&)orig_WriteConsoleA, Hook_WriteConsoleA);
        DetourAttach(&(PVOID&)orig_WriteConsoleW, Hook_WriteConsoleW);
        DetourTransactionCommit();
    }

    struct WriteLogMark
    {
        WriteLogMark() { t_InWriteLog = true; }
        ~WriteLogMark() { t_InWriteLog = false; }
    };
}

extern "C" __declspec(dllexport)
void WriteLog(std::string Message, std::string Module, int LogLevel)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    WriteLogMark mark;

    const int rank = RankOf(LogLevel);
    const bool toConsole = rank >= g_MinRank;
    const bool toFile = g_File.is_open() && rank >= g_FileRank;
    if (!toConsole && !toFile)
        return;

    if (Module.empty() && toConsole)
    {
        // Somebody logged without saying who they are.
        std::cout << settextcolor(console_text_colors::light_white) << Timestamp() << " " << settextcolor(console_text_colors::light_yellow) << "WARN ";
        WriteModule(MODULE_NAME, 1);
        std::cout << settextcolor(console_text_colors::light_yellow) << "A log line has no module name. Please set one.\n";
    }
    if (Module.empty())
        Module = "(unknown)";

    const LevelStyle style = StyleFor(LogLevel);

    // The prefix (time, level, "[module] ") as blank space, so the lines of a multi-line message stay under the first one.
    const size_t prefixWidth = Timestamp().length() + 1 + 5 + 1 + 1 + Module.length() + 2;

    std::istringstream lines(Message);
    std::string line;
    bool first = true;
    while (std::getline(lines, line) || first)
    {
        if (!line.empty() && line.back() == '\r')
            line.pop_back();

        if (toFile)
        {
            if (first)
                g_File << Timestamp() << " " << style.Tag << " [" << Module << "] " << line << "\n";
            else
                g_File << std::string(prefixWidth, ' ') << line << "\n";
        }

        if (toConsole)
        {
            if (first)
            {
                std::cout << settextcolor(console_text_colors::light_white) << Timestamp() << " "
                    << settextcolor(style.TagColor) << style.Tag << " ";
                WriteModule(Module, LogLevel);
            }
            else
                std::cout << std::string(prefixWidth, ' ');

            std::cout << settextcolor(style.MessageColor) << line << "\n";
        }
        first = false;
        if (lines.eof())
            break;
    }

    if (toFile && (++g_UnflushedFile >= kFlushEvery || rank >= 3))
    {
        g_File.flush();
        g_UnflushedFile = 0;
    }

    if (toConsole)
    {
        std::cout << settextcolor(console_text_colors::white);
        if (++g_UnflushedConsole >= kFlushEvery || rank >= 3)
        {
            std::cout.flush();
            g_UnflushedConsole = 0;
        }
        ctxout.restore(console_cleanup_options::restore_attibutes);
    }
}

namespace
{
    std::string ExeFolder()
    {
        char exe[MAX_PATH];
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        return folder.substr(0, folder.find_last_of("\\/") + 1);
    }

    // "Duels" -> "Duels.log". Only a plain file name is allowed (no folders), so a plugin can't write outside the game folder.
    std::string SplitLogFileName(std::string name)
    {
        for (char& c : name)
            if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                c = '_';
        if (name.empty())
            name = "unnamed";
        if (name.find('.') == std::string::npos)
            name += ".log";
        return name;
    }

    SplitLog* OpenSplitLog(const std::string& name)
    {
        const std::string file = SplitLogFileName(name);
        auto& slot = g_SplitLogs[file];
        if (!slot)
        {
            slot = std::make_unique<SplitLog>();
            slot->File.open(ExeFolder() + file, std::ios::out | std::ios::app);
            SYSTEMTIME now;
            GetLocalTime(&now);
            char date[64];
            snprintf(date, sizeof(date), "%04d-%02d-%02d %02d:%02d:%02d", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond);
            if (slot->File.is_open())
                slot->File << Timestamp() << " INFO  [" << MODULE_NAME << "] session " << date << "\n";
        }
        return slot->File.is_open() ? slot.get() : nullptr;
    }
}

// Writes a line to <File>.log next to console.log instead of the console (see g_SplitLogs). Same line format as console.log,
// every level is written. Levels as WriteLog.
extern "C" __declspec(dllexport)
void WriteLogTo(std::string File, std::string Message, std::string Module, int LogLevel)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    SplitLog* log = OpenSplitLog(File);
    if (!log)
        return;

    if (Module.empty())
        Module = "(unknown)";
    const LevelStyle style = StyleFor(LogLevel);
    const size_t prefixWidth = Timestamp().length() + 1 + 5 + 1 + 1 + Module.length() + 2;

    std::istringstream lines(Message);
    std::string line;
    bool first = true;
    while (std::getline(lines, line) || first)
    {
        if (!line.empty() && line.back() == '\r')
            line.pop_back();
        if (first)
            log->File << Timestamp() << " " << style.Tag << " [" << Module << "] " << line << "\n";
        else
            log->File << std::string(prefixWidth, ' ') << line << "\n";
        first = false;
        if (lines.eof())
            break;
    }

    const unsigned long long now = GetTickCount64();
    if (++log->Unflushed >= kFlushEvery || RankOf(LogLevel) >= 3 || now - log->LastFlush >= kSplitFlushMs)
    {
        log->File.flush();
        log->Unflushed = 0;
        log->LastFlush = now;
    }
}

// Flushes <File>.log now, e.g. at the end of a duel so a viewer opened while the game runs sees all of it.
extern "C" __declspec(dllexport)
void FlushLogTo(std::string File)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    auto it = g_SplitLogs.find(SplitLogFileName(File));
    if (it != g_SplitLogs.end() && it->second)
    {
        it->second->File.flush();
        it->second->Unflushed = 0;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        LoadLogLevel();
        AllocConsole();

        FILE* consoleOut;
        freopen_s(&consoleOut, "CONOUT$", "w", stdout);
        freopen_s(&consoleOut, "CONOUT$", "w", stderr);
        freopen_s(&consoleOut, "CONIN$", "r", stdin);

        SetWindowText(GetConsoleWindow(), L"Yu-Gi-Oh! Console");
        {
            // 24-bit colour escapes for the module names (ModuleColor).
            HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
            DWORD mode = 0;
            if (GetConsoleMode(output, &mode))
                SetConsoleMode(output, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }
        HideForeignConsoleOutput();

        WriteLog("Ready!", MODULE_NAME, 0);

        //  std::thread(ProcessInput).detach();
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
        if (g_File.is_open())
            g_File.flush();
        for (auto& entry : g_SplitLogs)
            if (entry.second)
                entry.second->File.flush();
        break;
    }
    return TRUE;
}
