#include <fstream>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>

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

    // The same name always gets the same colour.
    console_text_colors ModuleColor(const std::string& module)
    {
        static const console_text_colors palette[] =
        {
            console_text_colors::light_green, console_text_colors::light_blue, console_text_colors::light_magenta,
            console_text_colors::cyan, console_text_colors::green, console_text_colors::magenta,
        };

        size_t hash = 5381;
        for (unsigned char c : module)
            hash = hash * 33 + c;
        return palette[hash % (sizeof(palette) / sizeof(palette[0]))];
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
            std::cout << settextcolor(ModuleColor(module)) << module;

        std::cout << settextcolor(console_text_colors::white) << "] ";
    }
}

extern "C" __declspec(dllexport)
void WriteLog(std::string Message, std::string Module, int LogLevel)
{
    std::lock_guard<std::mutex> guard(g_Lock);

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

        WriteLog("Ready!", MODULE_NAME, 0);

        //  std::thread(ProcessInput).detach();
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
        if (g_File.is_open())
            g_File.flush();
        break;
    }
    return TRUE;
}
