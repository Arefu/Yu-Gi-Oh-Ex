#include "HookDiagnostics.h"
#include <iostream>
#include <vector>

static std::vector<std::pair<std::string, int>> messages;
static void __cdecl Log(std::string text, std::string, int level) { messages.emplace_back(text, level); }
static void Check(bool condition, const char* name)
{
    if (!condition) { std::cerr << "FAIL " << name << '\n'; ExitProcess(1); }
    std::cout << "PASS " << name << '\n';
}
__declspec(noinline) static int Target(int value) { volatile int result = value; result += 1; return result; }
static int (*original)(int) = Target;
static int Hook(int value) { return original(value) + 10; }

int main()
{
    Check(DetourTransactionBegin() == NO_ERROR, "open competing transaction");
    Check(!HookDiagnostics::Begin("nested test", "test", Log) && messages.back().second == 2 &&
        messages.back().first.find("transaction begin") != std::string::npos, "begin failure identifies stage and error");
    DetourTransactionAbort();
    Check(HookDiagnostics::Begin("invalid test", "test", Log), "open invalid-hook test");
    PVOID invalid = nullptr;
    Check(DetourAttach(&invalid, Hook) != NO_ERROR, "invalid target rejected");
    Check(HookDiagnostics::Commit("invalid test", "test", Log) != NO_ERROR && messages.back().second == 2 &&
        messages.back().first.find("original-pointer slot") != std::string::npos, "commit failure identifies group and pointer slot");
    Check(Target(1) == 2, "failed transaction leaves function unchanged");
    Check(HookDiagnostics::Begin("valid test", "test", Log), "open valid-hook test");
    Check(DetourAttach(reinterpret_cast<PVOID*>(&original), Hook) == NO_ERROR, "attach valid target");
    Check(HookDiagnostics::Commit("valid test", "test", Log) == NO_ERROR && messages.back().second == 0 &&
        messages.back().first == "valid test: hooks installed", "success message only after commit");
    Check(Target(1) == 12, "installed hook runs");
    DetourTransactionBegin(); DetourDetach(reinterpret_cast<PVOID*>(&original), Hook); DetourTransactionCommit();
    Check(Target(1) == 2, "test hook removed");
}
