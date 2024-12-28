#include <Windows.h>

#include <iostream>
#include <fstream>
#include <string>
#include <json.hpp>

#include "Manifest.h"

std::vector<std::pair<short, short>> _Manifest::ProcessChanges(nlohmann::json Manifest, std::string Type)
{
	std::vector<std::pair<short, short>> Changes;
	
	if (Manifest["Requests"] == NULL || Manifest["Requests"].size() == 0)
	{
		std::cout << "[Yu-Gi-Oh-Effects]: No Requests Found" << std::endl;
		return Changes;
	}

	for (int x = 0; x < Manifest["Requests"].size(); x++) {
		if (Manifest["Requests"][x]["Type"] != Type)
			continue;

		if (Manifest["Requests"][x]["Changes"] != NULL)
		{
			std::cout << "[Yu-Gi-Oh-Effects]: Changes: " << Manifest["Requests"][x]["Changes"].size() << std::endl;
			for (int y = 0; y < Manifest["Requests"][x]["Changes"].size(); y++)
			{
				std::cout << "[Yu-Gi-Oh-Effects]: Manfest Updates Card: " << Manifest["Requests"][x]["Changes"][y]["Change"]["ID"] << std::endl;
				std::cout << "[Yu-Gi-Oh-Effects]: Manfest Update Value: " << Manifest["Requests"][x]["Changes"][y]["Change"]["Count"] << std::endl;
				std::pair <short, short> Change = std::make_pair(Manifest["Requests"][x]["Changes"][y]["Change"]["ID"], Manifest["Requests"][x]["Changes"][y]["Change"]["Count"]);
				Changes.push_back(Change);
			}
		}
		
		return Changes;
	}
}