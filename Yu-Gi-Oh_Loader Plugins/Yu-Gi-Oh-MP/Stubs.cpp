#include <Windows.h>
#include <detours.h>

#include <charconv>
#include <cstdio>
#include <format>
#include <iterator>
#include <string>
#include <string_view>

#include "Logger.h"
#include "Stubs.h"

namespace
{
    using Stubs::u64;

    // Copies game memory without crashing on a bad pointer. Kept free of C++ objects: MSVC won't mix __try with
    // object unwinding in one function (C2712).
    bool SafeRead(void* dst, const void* src, size_t size)
    {
        if (!src || !size)
            return false;
        __try
        {
            std::memcpy(dst, src, size);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    std::string SafeCString(const void* src)
    {
        char buf[97]{};
        if (!src)
            return "(null)";
        for (size_t i = 0; i + 1 < sizeof buf; ++i)
            if (!SafeRead(&buf[i], static_cast<const char*>(src) + i, 1) || buf[i] == 0)
                break;
        return std::format("\"{}\"", buf);
    }

    std::string SafeWString(const void* src)
    {
        if (!src)
            return "(null)";
        std::string out = "\"";
        for (size_t i = 0; i < 96; ++i)
        {
            wchar_t c = 0;
            if (!SafeRead(&c, static_cast<const wchar_t*>(src) + i, sizeof c) || c == 0)
                break;
            out += (c < 0x80) ? static_cast<char>(c) : '?';
        }
        return out + "\"";
    }

    std::string Smart(u64 v)
    {
        return v <= 0xFFFFFFFFull ? std::format("{}", v) : std::format("0x{:X}", v);
    }

    size_t ParseNumber(std::string_view text)
    {
        size_t value = 0;
        std::from_chars(text.data(), text.data() + text.size(), value);
        return value;
    }

    std::string FormatArg(std::string_view fmt, u64 v, const u64* args, size_t argCount)
    {
        if (fmt.empty() || fmt == "u64") return Smart(v);
        if (fmt == "ptr" || fmt == "hex") return std::format("0x{:X}", v);
        if (fmt == "i8")   return std::format("{}", static_cast<int8_t>(v));
        if (fmt == "u8")   return std::format("{}", static_cast<uint8_t>(v));
        if (fmt == "i16")  return std::format("{}", static_cast<int16_t>(v));
        if (fmt == "u16")  return std::format("{}", static_cast<uint16_t>(v));
        if (fmt == "i32")  return std::format("{}", static_cast<int32_t>(v));
        if (fmt == "u32")  return std::format("{}", static_cast<uint32_t>(v));
        if (fmt == "i64")  return std::format("{}", static_cast<int64_t>(v));
        if (fmt == "bool") return (v & 0xFF) ? "true" : "false";
        if (fmt == "msg")  return std::format("{}({:#x})/side{}", Stubs::DuelMsgName(v & 0xFFF), v & 0xFFF, (v >> 15) & 1);
        if (fmt == "evt")  return std::format("{}({})", Stubs::EventTypeName(static_cast<uint16_t>(v)), static_cast<uint16_t>(v));
        if (fmt == "cstr") return SafeCString(reinterpret_cast<const void*>(v));
        if (fmt == "wstr") return SafeWString(reinterpret_cast<const void*>(v));
        if (fmt == "float")
        {
            double d;
            std::memcpy(&d, &v, sizeof d);
            return std::format("{:.4f}", d);
        }
        if (fmt == "p32" || fmt == "p64")
        {
            u64 value = 0;
            if (!SafeRead(&value, reinterpret_cast<const void*>(v), fmt == "p32" ? 4 : 8))
                return std::format("0x{:X}->?", v);
            return std::format("0x{:X}->{}", v, Smart(value));
        }
        if (fmt.starts_with("hex@*") || fmt.starts_with("hex@"))
        {
            const bool indirect = fmt[4] == '*';
            const size_t k = ParseNumber(fmt.substr(indirect ? 5 : 4));
            u64 count = k < argCount ? args[k] : 0;
            if (indirect && !SafeRead(&count, reinterpret_cast<const void*>(count), sizeof count))
                count = 0;
            return Stubs::HexDump(reinterpret_cast<const void*>(v), static_cast<size_t>(count));
        }
        if (fmt.starts_with("hex"))
        {
            std::string_view rest = fmt.substr(3);
            const size_t plus = rest.find('+');
            const size_t count = ParseNumber(rest.substr(0, plus));
            const size_t offset = plus == std::string_view::npos ? 0 : ParseNumber(rest.substr(plus + 1));
            return Stubs::HexDump(v ? reinterpret_cast<const void*>(v + offset) : nullptr, count);
        }
        return Smart(v);
    }

    std::atomic<int> g_Attached = 0, g_Failed = 0;

    // Game code prints as its IDA address. Anything else is another plugin's detour sitting in the chain, so name it.
    std::string Caller(void* address)
    {
        HMODULE module = nullptr;
        if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                               static_cast<LPCSTR>(address), &module) && module && module != GetModuleHandleA(nullptr))
        {
            char path[MAX_PATH]{};
            GetModuleFileNameA(module, path, MAX_PATH);
            const char* file = strrchr(path, '\\');
            return std::format("{}+0x{:X}", file ? file + 1 : path, reinterpret_cast<uintptr_t>(address) - reinterpret_cast<uintptr_t>(module));
        }
        return std::format("0x{:X}", reinterpret_cast<uintptr_t>(address));
    }
}

std::string Stubs::HexDump(const void* address, size_t size, size_t cap)
{
    if (!address)
        return "(null)";
    const size_t shown = size < cap ? size : cap;
    uint8_t bytes[512];
    if (shown > sizeof bytes || !SafeRead(bytes, address, shown))
        return std::format("0x{:X}[{}]?", reinterpret_cast<uintptr_t>(address), size);

    std::string out = std::format("[{}]", size);
    for (size_t i = 0; i < shown; ++i)
        out += std::format(" {:02X}", bytes[i]);
    if (shown < size)
        out += " ...";
    return out;
}

// DuelNetEventType, docs/MultiplayerSystem.md "All 34 event types".
const char* Stubs::EventTypeName(unsigned type)
{
    static const char* const names[] = {
        "Noop", "Abort", "DuelOverNotify", "DuelOverAck", "TurnPlayerSync", "EngineMsg", "EngineMsgAck", "AnimCue",
        "ResolutionScope", "SelectionList", "SelectionListReset", "PromptPass", "PromptAnswer", "CommandChosen",
        "PromptPassInChain", "PromptAnswerInChain", "SetActionRecord", "SelectedIndex", "RegisterCandidate",
        "EventRecordStart", "EventRecordUpdate", "HandlerDone", "TargetSelectRequest", "TargetSelectResult",
        "EffectCheckRequest", "EffectCheckResult", "CostRequest", "CostResult", "RemotePromptRequest",
        "RemotePromptAnswer", "ListPromptRequest", "ListPromptAnswer", "Surrender", "Noop2" };
    return type < std::size(names) ? names[type] : "Unknown";
}

// DuelMsgCode (enum in the IDB; docs/MultiplayerSystem.md "Engine message codes"). Many names are provisional there.
const char* Stubs::DuelMsgName(unsigned code)
{
    struct Entry { unsigned Code; const char* Name; };
    static const Entry names[] = {
        { 0x01, "DuelStart" },
        { 0x02, "TurnStart" },
        { 0x03, "EndOfTurn" },
        { 0x04, "NextTurn" },
        { 0x05, "DuelResult" },
        { 0x06, "SetDuelFlagBit" },
        { 0x07, "Anim03Cue" },
        { 0x08, "ShowMessage" },
        { 0x09, "ShowBanner" },
        { 0x0A, "Phase_Draw" },
        { 0x0B, "Phase_Standby" },
        { 0x0C, "Phase_Main1" },
        { 0x0D, "Phase_Battle" },
        { 0x0E, "Phase_Main2" },
        { 0x0F, "Phase_End" },
        { 0x10, "Anim34_SideValue" },
        { 0x11, "ChainLinkResolve" },
        { 0x12, "Handler12" },
        { 0x13, "SetField38" },
        { 0x14, "BattleStep2" },
        { 0x15, "RefreshAll15" },
        { 0x16, "Handler16" },
        { 0x17, "Anim0C_Summon" },
        { 0x18, "Anim0C_Summon2" },
        { 0x19, "ZoneFlag19" },
        { 0x1A, "Handler1A" },
        { 0x1B, "Handler1B" },
        { 0x1C, "Handler1C" },
        { 0x1D, "BattleStep4" },
        { 0x1E, "BattleStep7" },
        { 0x1F, "Handler1F" },
        { 0x20, "ZoneFlag20" },
        { 0x21, "SetZoneBit21" },
        { 0x22, "DeclareLoss" },
        { 0x23, "LP_Gain" },
        { 0x24, "LP_Damage" },
        { 0x25, "LP_Set" },
        { 0x27, "CardToPileAnim" },
        { 0x28, "Handler28" },
        { 0x29, "SetCardBit29" },
        { 0x2B, "Handler2B" },
        { 0x2C, "Handler2C" },
        { 0x2D, "Handler2D" },
        { 0x2E, "ShowCardActivation" },
        { 0x2F, "ShowCardEffect" },
        { 0x30, "Handler30" },
        { 0x31, "Handler31" },
        { 0x32, "Handler32" },
        { 0x33, "Handler33" },
        { 0x34, "Handler34" },
        { 0x35, "FlagMarker_Add" },
        { 0x36, "FlagMarker_Remove" },
        { 0x37, "FlagMarker_Bulk" },
        { 0x38, "RenumberCard" },
        { 0x39, "ZoneValueSetOrAdd" },
        { 0x3A, "Handler3A" },
        { 0x3B, "Handler3B" },
        { 0x3C, "Handler3C" },
        { 0x3D, "Handler3D" },
        { 0x3E, "Handler3E" },
        { 0x3F, "Handler3F" },
        { 0x40, "Handler40" },
        { 0x41, "AttackDeclare" },
        { 0x42, "ChangePosition" },
        { 0x43, "Handler43" },
        { 0x44, "Handler44" },
        { 0x45, "Handler45" },
        { 0x47, "Handler47" },
        { 0x48, "Handler48" },
        { 0x49, "Anim2B_Zone" },
        { 0x4A, "Anim2E_Zone" },
        { 0x4B, "Handler4B" },
        { 0x4C, "Handler4C" },
        { 0x4D, "MaterialAnimByFrame" },
        { 0x4E, "ChainHistoryWrite" },
        { 0x4F, "Handler4F" },
        { 0x50, "HandShuffle" },
        { 0x51, "Handler51" },
        { 0x52, "Handler52" },
        { 0x53, "Handler53" },
        { 0x54, "Card7609Flags" },
        { 0x55, "Anim51_Hand" },
        { 0x56, "DeckShuffle" },
        { 0x57, "Handler57" },
        { 0x59, "CardToPile" },
        { 0x5A, "DrawFromPile" },
        { 0x5B, "DeckFlag4Toggle" },
        { 0x5C, "Noop5C" },
        { 0x5D, "RefreshDecksAndGraves" },
        { 0x5E, "Anim3D" },
        { 0x5F, "Anim3E" },
        { 0x60, "Anim3F" },
        { 0x61, "Anim41_DestinyBoard" },
        { 0x62, "Handler62" },
        { 0x63, "Anim4A" },
        { 0x64, "Anim47_Idle" },
        { 0x68, "Prompt68" },
        { 0x69, "Anim43" },
        { 0x6A, "ActionRecordFlags" },
        { 0x6B, "Anim5A" },
        { 0x6C, "Anim5B" },
        { 0x6D, "HighWordPrefix" },
    };
    for (const Entry& e : names)
        if (e.Code == code)
            return e.Name;
    return "Unknown";
}

void Stubs::LogCall(const char* name, const char* params, const u64* args, size_t argCount, uint32_t call, int level,
                    void* returnAddress, bool after, u64 result)
{
    std::string line = std::format("[stub] {}(", name);

    std::string_view spec = params;
    for (size_t i = 0; i < argCount; ++i)
    {
        const size_t comma = spec.find(',');
        std::string_view item = spec.substr(0, comma);
        spec = comma == std::string_view::npos ? std::string_view{} : spec.substr(comma + 1);

        const size_t colon = item.find(':');
        const std::string_view argName = item.substr(0, colon);
        const std::string_view fmt = colon == std::string_view::npos ? std::string_view{} : item.substr(colon + 1);

        if (i) line += ", ";
        line += std::format("{}={}", argName.empty() ? std::format("a{}", i + 1) : std::string(argName), FormatArg(fmt, args[i], args, argCount));
    }
    line += ")";
    if (after)
        line += std::format(" -> {}", Smart(result));
    line += " from " + Caller(returnAddress);
    line += std::format(" [{}/{}]", call, kMaxLogged);

    Logger::WriteLog(line, MODULE_NAME, level);
    if (call == kMaxLogged)
        Logger::WriteLog(std::format("[stub] {}: logged {} calls, further calls not logged", name, kMaxLogged), MODULE_NAME, level);
}

// Each stub gets its own transaction: a failed attach (a function too short to patch, say) only loses that stub.
bool Stubs::Attach(void** original, void* hook, const char* name)
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    LONG error = DetourAttach(original, hook);
    if (error == NO_ERROR)
        error = DetourTransactionCommit();
    else
        DetourTransactionAbort();

    if (error != NO_ERROR)
    {
        ++g_Failed;
        Logger::WriteLog(std::format("[stub] could not hook {} (Detours error {})", name, error), MODULE_NAME, 1);
        return false;
    }
    ++g_Attached;
    return true;
}
