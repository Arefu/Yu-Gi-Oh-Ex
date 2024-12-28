#pragma once

#include <string>
#include <json.hpp>

static class Utility {
public:
	static nlohmann::json Find_ManifestFilesInDirectory(std::string Path);
};
