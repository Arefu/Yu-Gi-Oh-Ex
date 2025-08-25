#pragma once

#include <vector>
#include <string>
#include <json.hpp>

namespace CardsThatMakeYouDraw {
	enum class Operation {
		Add,
		Remove,
		Update
	};

	struct Item {
		short CardID;
		short Count;
	};

	struct OperationEntry {
		Operation Operation;
		std::vector<Item> Cards;
	};

	struct OperationList {
		std::vector<OperationEntry> Operations;
	};

	Operation stringToOperation(const std::string& op);
	std::string operationToString(Operation op);

	bool Validate(const nlohmann::json& j);

	int Setup(const std::string& Path);

	void to_json(nlohmann::json& j, const Item& c);
	void from_json(const nlohmann::json& j, Item& c);
	void to_json(nlohmann::json& j, const OperationEntry& o);
	void from_json(const nlohmann::json& j, OperationEntry& o);
	void to_json(nlohmann::json& j, const OperationList& b);
	void from_json(const nlohmann::json& j, OperationList& b);
}
