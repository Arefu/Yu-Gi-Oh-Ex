#include "Table_140B15B30.h"

std::vector<Table_140B15B30::Table> Table_140B15B30::Items(239);
bool Table_140B15B30::Patched = false;

void Table_140B15B30::Setup()
{
	Logger::WriteLog(std::format("Copying Effect Damage Table: 0x140B15B30 -> 0x{:X} ", reinterpret_cast<uintptr_t>(Items.data())), MODULE_NAME, 0);
	memcpy(Items.data(), reinterpret_cast <void*>(0x140B15B30), sizeof(Table_140B15B30::Table) * 239);
	Memory::PatchZeros(reinterpret_cast<void*>(0x140B15B30), sizeof(Table_140B15B30::Table) * 239, true);
}

void Table_140B15B30::Patch()
{
	Logger::WriteLog("Patching Effect Damage Table", MODULE_NAME, 0);
	Memory::InsertMOV(reinterpret_cast<void*>(0x14015AD2A), Items.size(), Memory::X64Register::R11, true, 6);
	Memory::InsertCALL(reinterpret_cast<void*>(0x14015AD70), 0x140B15B30, 7, true);

	auto Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140B15B30), reinterpret_cast<uintptr_t>(Items.data()), Memory::X64Register::RDX, true);
	Position = Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);
	Memory::InsertCALL(reinterpret_cast<void*>(0x14015AD46), reinterpret_cast<uintptr_t>(Position), 9, true);
	Position = Memory::EmplaceMOVSX(reinterpret_cast<void*>(Position), Memory::X64Register::RDX, Memory::X64Register::RCX, reinterpret_cast<uint32_t>(Items.data()), true, 9);
	Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);

	Table_140B15B30::Patched = true;
}

void Table_140B15B30::List()
{
	Logger::WriteLog("Start - Effect Damage Table", MODULE_NAME, 0);
	for (int x = 0; x < Items.size(); x++)
	{
		Logger::WriteLog(std::format("Konami Card ID: {}, Amount ChangeD: {}", Items.at(x).KonamiID, Items.at(x).Amount), MODULE_NAME, 0);
	}
	Logger::WriteLog("End - Effect Damage Table", MODULE_NAME, 0);
}

bool Table_140B15B30::ChangeItemInList(short KonamiID, short Amount)
{
	return false;
}

bool Table_140B15B30::AddToList(short KonamiID, short Amount)
{
	if (Patched)
	{
		Logger::WriteLog("AddToList failed: Effect Damage Table has already been patched.", MODULE_NAME, 2);
		return false;
	}

	Items.emplace_back(Table{ KonamiID, Amount });

	std::stable_sort(Items.begin(), Items.end(), [](const Table& a, const Table& b) {
		return a.KonamiID < b.KonamiID;
		});

	return true;
}

bool Table_140B15B30::RemoveFromList(short KonamiID)
{
	if (Patched)
	{
		Logger::WriteLog("RemoveFromList failed: Effect Damage Table has already been patched.", MODULE_NAME, 2);
		return false;
	}

	auto it = std::remove_if(Items.begin(), Items.end(), [KonamiID](const Table& item) {
		return item.KonamiID == KonamiID;
		});

	if (it != Items.end())
	{
		Items.erase(it, Items.end());
		Logger::WriteLog(std::format("Removed Card ID {} from Effect Damage Table.", KonamiID), MODULE_NAME, 0);
		return true;
	}

	Logger::WriteLog(std::format("Card ID {} not found in Effect Damage Table.", KonamiID), MODULE_NAME, 1);
	return false;
}

short Table_140B15B30::GetItemFromList(short KonamiID)
{
	for (const auto& item : Items)
	{
		if (item.KonamiID == KonamiID)
			return item.Amount;
	}

	Logger::WriteLog(std::format("Card ID {} not found in Effect Damage Table.", KonamiID), MODULE_NAME, 1);
	return 0;
}