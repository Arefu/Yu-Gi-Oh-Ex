#include <Windows.h>
#include <shlwapi.h>
#include <json.hpp>
#include <iostream>
#include <vector>
#include <string>
#include "detours.h"
#include "Logger.h"
#include "Memory.h"
#include "File.h"

using json = nlohmann::json;

enum class CardsThatMakeYouDraw_Operation {
	Add,
	Remove,
	Update
};

inline CardsThatMakeYouDraw_Operation stringToOperation(const std::string& op) {
	if (op == "Add") return CardsThatMakeYouDraw_Operation::Add;
	if (op == "Remove") return CardsThatMakeYouDraw_Operation::Remove;
	if (op == "Update") return CardsThatMakeYouDraw_Operation::Update;
	throw std::invalid_argument("Invalid operation string");
}

inline std::string operationToString(CardsThatMakeYouDraw_Operation op) {
	switch (op) {
	case CardsThatMakeYouDraw_Operation::Add: return "Add";
	case CardsThatMakeYouDraw_Operation::Remove: return "Remove";
	case CardsThatMakeYouDraw_Operation::Update: return "Update";
	}
	return "";
}

struct CardsThatMakeYouDraw_Item {
	short CardID;
	short Count;
};

struct OperationEntry {
	CardsThatMakeYouDraw_Operation Operation;
	std::vector<CardsThatMakeYouDraw_Item> Cards;
};

struct OperationList {
	std::vector<OperationEntry> Operations;
};

inline void to_json(json& j, const CardsThatMakeYouDraw_Item& c) {
	j = json{ {"CardID", c.CardID}, {"Count", c.Count} };
}
inline void from_json(const json& j, CardsThatMakeYouDraw_Item& c) {
	j.at("CardID").get_to(c.CardID);
	j.at("Count").get_to(c.Count);
}
inline void to_json(json& j, const OperationEntry& o) {
	j["Operation"] = operationToString(o.Operation);
	j["Cards"] = o.Cards;
}
inline void from_json(const json& j, OperationEntry& o) {
	o.Operation = stringToOperation(j.at("Operation").get<std::string>());
	j.at("Cards").get_to(o.Cards);
}
inline void to_json(json& j, const OperationList& b) {
	j["Operations"] = b.Operations;
}
inline void from_json(const json& j, OperationList& b) {
	j.at("Operations").get_to(b.Operations);
}
bool Validate(const json& j) {
	if (!j.contains("Operations") || !j["Operations"].is_array())
		return false;

	for (const auto& opEntry : j["Operations"]) {
		if (!opEntry.contains("Operation") || !opEntry["Operation"].is_string())
			return false;

		std::string opStr = opEntry["Operation"];
		if (opStr != "Add" && opStr != "Remove" && opStr != "Update")
			return false;

		if (!opEntry.contains("Cards") || !opEntry["Cards"].is_array() || opEntry["Cards"].empty())
			return false;

		for (const auto& card : opEntry["Cards"]) {
			if (!card.contains("CardID") || !card["CardID"].is_number_integer())
				return false;
			if (!card.contains("Count") || !card["Count"].is_number_integer() || card["Count"].get<int>() < 1)
				return false;
		}
	}
	return true;
}

static std::vector<CardsThatMakeYouDraw_Item> CardsThatMakeYouDraw(469);

int Setup_CardsThatMakeYouDraw(std::string Path)
{
	Logger::WriteLog("Started Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);
	if (PathFileExists(Path.c_str()) == FALSE)
	{
		Logger::WriteLog("FILE_NOT_FOUND: CardsThatMakeYouDraw", MODULE_NAME, 1);
		return ERROR_FILE_NOT_FOUND;
	}

	auto Content = json::parse(EffectFileHandler::Read_FromEffectFile(Path));
	if (Validate(Content) == false)
	{
		Logger::WriteLog("ERROR_FILE_CORRUPT: CardsThatMakeYouDraw Failed To Validate!", MODULE_NAME, 2);
		return ERROR_FILE_CORRUPT;
	}

	memcpy(CardsThatMakeYouDraw.data(), reinterpret_cast<void*>(0x140B15110), sizeof(CardsThatMakeYouDraw_Item) * 469);
	Memory::PatchZeros(reinterpret_cast<void*>(0x140B15110), 469, true);

	for (const auto& operationEntry : Content["Operations"])
	{
		std::string op = operationEntry["Operation"].get<std::string>();
		std::transform(op.begin(), op.end(), op.begin(), ::tolower);

		if (op == "add")
		{
			for (const auto& card : operationEntry["Cards"])
			{
				short CardID = card["CardID"].get<short>();
				short Count = card["Count"].get<short>();
				Logger::WriteLog(std::format("Operation: {}, CardID: {}, Count: {}", op, CardID, Count), MODULE_NAME, 0);

				CardsThatMakeYouDraw.emplace_back(CardsThatMakeYouDraw_Item{ CardID, Count });
			}
		}
		else if (op == "remove")
		{
			for (const auto& card : operationEntry["Cards"])
			{
				short CardID = card["CardID"].get<short>();
				Logger::WriteLog(std::format("Operation: {}, CardID: {}", op, card["CardID"].get<short>()), MODULE_NAME, 0);
				for (size_t i = 0; i < CardsThatMakeYouDraw.size(); ++i) {
					if (CardsThatMakeYouDraw[i].CardID == CardID) {
						CardsThatMakeYouDraw.erase(CardsThatMakeYouDraw.begin() + i);
						break;
					}
				}
			}
		}
		else if (op == "update")
		{
			for (const auto& card : operationEntry["Cards"])
			{
				short CardID = card["CardID"].get<short>();
				short Count = card["Count"].get<short>();
				Logger::WriteLog(std::format("Operation: {}, CardID: {}, Count: {}", op, CardID, Count), MODULE_NAME, 0);
				for (size_t i = 0; i < CardsThatMakeYouDraw.size(); ++i) {
					if (CardsThatMakeYouDraw[i].CardID == CardID) {
						CardsThatMakeYouDraw[i].Count = Count;
						break;
					}
				}
			}
		}
	}
	std::sort(CardsThatMakeYouDraw.begin(), CardsThatMakeYouDraw.end(), [](const CardsThatMakeYouDraw_Item& a, const CardsThatMakeYouDraw_Item& b) {return a.CardID < b.CardID; });

	Memory::EmplaceMOVSX(reinterpret_cast<void*>(0x14015EB16), Memory::X64Register::RDX, Memory::X64Register::RCX, reinterpret_cast<uint32_t>(CardsThatMakeYouDraw.data()), true, 9);
	auto Position = Memory::EmplaceCALL(reinterpret_cast<void*>(0x14015EB48), 0x140B15110, true);
	Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140B15110), reinterpret_cast<uintptr_t>(CardsThatMakeYouDraw.data()), Memory::X64Register::RDX, true);
	Memory::EmplaceRET(Position, true);
	Memory::InsertMOV(reinterpret_cast<void*>(0x14015EAFA), CardsThatMakeYouDraw.size(), Memory::X64Register::R12, true, 0x6);

	Logger::WriteLog("Done Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);
	return 0;
}