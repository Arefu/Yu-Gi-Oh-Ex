#pragma once
#include <string>
#include <vector>
#include <json.hpp>

static class _Manifest {
public:
	static std::vector<std::pair<short, short>> ProcessChanges(nlohmann::json Manifest, std::string Type);
};