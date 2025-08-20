#include <Windows.h>
#include <shlwapi.h>
#include <json.hpp>
#include <iostream>
#include <vector>
#include <string>

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
static std::vector<CardsThatMakeYouDraw_Item> CardsThatMakeYouDraw(470);
uint8_t* EmitDrawCardPatch(uint8_t* patchStart) {
	uintptr_t vecAddress = reinterpret_cast<uintptr_t>(CardsThatMakeYouDraw.data());
	uint8_t* cursor = patchStart;

	// mov rdx, &CardsThatMakeYouDraw
	uint8_t movRdxImmediate[] = {
		0x48, 0xBA,                      // mov rdx, imm64
		0, 0, 0, 0, 0, 0, 0, 0           // placeholder
	};
	*reinterpret_cast<uintptr_t*>(&movRdxImmediate[2]) = vecAddress;
	cursor = Memory::PatchBytes(cursor, movRdxImmediate, sizeof(movRdxImmediate), true);

	// mov rdx, [rdx] (vector.data())
	uint8_t movRdxFromRdx[] = {
		0x48, 0x8B, 0x12                 // mov rdx, [rdx]
	};
	cursor = Memory::PatchBytes(cursor, movRdxFromRdx, sizeof(movRdxFromRdx), true);

	// lea rdx, [rdx + r12*2] (index into vector)
	uint8_t leaRdxIndexed[] = {
		0x4C, 0x8D, 0x14, 0x62           // lea rdx, [rdx + r12*2]
	};
	cursor = Memory::PatchBytes(cursor, leaRdxIndexed, sizeof(leaRdxIndexed), true);

	// movsx eax, word ptr [rdx] (load value at index)
	uint8_t movsxEaxFromRdx[] = {
		0x66, 0x0F, 0xBF, 0x02           // movsx eax, word ptr [rdx]
	};
	cursor = Memory::PatchBytes(cursor, movsxEaxFromRdx, sizeof(movsxEaxFromRdx), true);

	return cursor;
}
int Setup_CardsThatMakeYouDraw(std::string Path)
{
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

	memcpy(CardsThatMakeYouDraw.data(), reinterpret_cast<void*>(0x140B15110), sizeof(CardsThatMakeYouDraw_Item) * 470);
	Memory::PatchZeros(reinterpret_cast<void*>(0x140B15110), 470, true);

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
		else
		{
			Logger::WriteLog("ERROR_INVALID_OPERATION: CardsThatMakeYouDraw Has An Invalid Operation!", MODULE_NAME, 0);
			return ERROR_INVALID_OPERATION;
		}
	}

	auto res = EmitDrawCardPatch(reinterpret_cast<uint8_t*>(0x140B15110));
	Memory::EmplaceRET(reinterpret_cast<void*>(res), true);

	return 0;
}