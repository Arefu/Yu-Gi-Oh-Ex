#include "CardsThatMakeYouDraw.h"
#include "Effects.h"
#include "Logger.h"
#include "Memory.h"
#include "File.h"
#include <Windows.h>
#include <shlwapi.h>
#include <algorithm>
#include <format>

using json = nlohmann::json;

namespace CardsThatMakeYouDraw {
    static std::vector<Item> CardTable(469);

    Operation stringToOperation(const std::string& op) {
        if (op == "Add") return Operation::Add;
        if (op == "Remove") return Operation::Remove;
        if (op == "Update") return Operation::Update;
        throw std::invalid_argument("Invalid operation string");
    }

    std::string operationToString(Operation op) {
        switch (op) {
        case Operation::Add: return "Add";
        case Operation::Remove: return "Remove";
        case Operation::Update: return "Update";
        default: return "";
        }
    }

    void to_json(json& j, const Item& c) {
        j = json{ {"CardID", c.CardID}, {"Count", c.Count} };
    }
    void from_json(const json& j, Item& c) {
        j.at("CardID").get_to(c.CardID);
        j.at("Count").get_to(c.Count);
    }
    void to_json(json& j, const OperationEntry& o) {
        j["Operation"] = operationToString(o.Operation);
        j["Cards"] = o.Cards;
    }
    void from_json(const json& j, OperationEntry& o) {
        o.Operation = stringToOperation(j.at("Operation").get<std::string>());
        j.at("Cards").get_to(o.Cards);
    }
    void to_json(json& j, const OperationList& b) {
        j["Operations"] = b.Operations;
    }
    void from_json(const json& j, OperationList& b) {
        j.at("Operations").get_to(b.Operations);
    }

    bool Validate(const json& j) {
        if (!j.contains("Operations") || !j["Operations"].is_array()) return false;

        for (const auto& opEntry : j["Operations"]) {
            if (!opEntry.contains("Operation") || !opEntry["Operation"].is_string()) return false;

            const std::string& opStr = opEntry["Operation"];
            if (opStr != "Add" && opStr != "Remove" && opStr != "Update") return false;

            if (!opEntry.contains("Cards") || !opEntry["Cards"].is_array() || opEntry["Cards"].empty()) return false;

            for (const auto& card : opEntry["Cards"]) {
                if (!card.contains("CardID") || !card["CardID"].is_number_integer()) return false;
                if (!card.contains("Count") || !card["Count"].is_number_integer() || card["Count"].get<int>() < 1) return false;
            }
        }
        return true;
    }

    int Setup(const std::string& Path) {
        Logger::WriteLog("Started Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);

        if (!PathFileExists(Path.c_str())) {
            Logger::WriteLog("FILE_NOT_FOUND: CardsThatMakeYouDraw", MODULE_NAME, 1);
            return ERROR_FILE_NOT_FOUND;
        }

        const auto Content = json::parse(FileIO::Read_FromEffectFile(Path));
        if (!Validate(Content)) {
            Logger::WriteLog("ERROR_FILE_CORRUPT: CardsThatMakeYouDraw Failed To Validate!", MODULE_NAME, 2);
            return ERROR_FILE_CORRUPT;
        }

        memcpy(CardTable.data(), reinterpret_cast<void*>(0x140B15110), sizeof(Item) * 469);
        Memory::PatchZeros(reinterpret_cast<void*>(0x140B15110), 469, true);

        for (const auto& operationEntry : Content["Operations"]) {
            std::string op = operationEntry["Operation"].get<std::string>();
            std::transform(op.begin(), op.end(), op.begin(), ::tolower);

            for (const auto& card : operationEntry["Cards"]) {
                const short CardID = card["CardID"].get<short>();
                const short Count = card["Count"].get<short>();

                if (op == "add") {
                    Logger::WriteLog(std::format("Add: CardID={}, Count={}", CardID, Count), MODULE_NAME, 0);
                    CardTable.emplace_back(Item{ CardID, Count });
                }
                else if (op == "remove") {
                    Logger::WriteLog(std::format("Remove: CardID={}", CardID), MODULE_NAME, 0);
                    std::erase_if(CardTable, [CardID](const auto& item) { return item.CardID == CardID; });
                }
                else if (op == "update") {
                    Logger::WriteLog(std::format("Update: CardID={}, Count={}", CardID, Count), MODULE_NAME, 0);
                    for (auto& item : CardTable) {
                        if (item.CardID == CardID) {
                            item.Count = Count;
                            break;
                        }
                    }
                }
            }
        }

        std::sort(CardTable.begin(), CardTable.end(), [](const auto& a, const auto& b) { return a.CardID < b.CardID; });

        Memory::EmplaceMOVSX(reinterpret_cast<void*>(0x14015EB16), Memory::X64Register::RDX, Memory::X64Register::RCX,
            reinterpret_cast<uint32_t>(CardTable.data()), true, 9);

        auto Position = Memory::EmplaceCALL(reinterpret_cast<void*>(0x14015EB48), 0x140B15110, true);
        Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140B15110),
            reinterpret_cast<uintptr_t>(CardTable.data()), Memory::X64Register::RDX, true);

        Memory::EmplaceRET(Position, true);
        Memory::InsertMOV(reinterpret_cast<void*>(0x14015EAFA), CardTable.size(), Memory::X64Register::R12, true, 0x6);
        Effects_FunctionTable::AdjustTable();
        Logger::WriteLog("Done Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);
        return 0;
    }
}
