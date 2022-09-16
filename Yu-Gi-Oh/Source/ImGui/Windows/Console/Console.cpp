#include "ImGui/Windows/Console/Console.h"
#include <stdio.h>

ConsoleState Console::State;

void Console::ShowConsole(bool* Open)
{
	ImGui::SetNextWindowSize(ImVec2(520, 600), ImGuiCond_FirstUseEver);
	if (!ImGui::Begin("Yu-Gi-Oh: Legacy Of The Duelist - WolfX", Open, ImGuiWindowFlags_NoCollapse))
	{
		ImGui::End();
		return;
	}
	if (ImGui::SmallButton("Clear")) {
		ClearLog();
	}

	ImGui::Separator();

	const float footer_height_to_reserve = ImGui::GetStyle().ItemSpacing.y + ImGui::GetFrameHeightWithSpacing();
	ImGui::BeginChild("ScrollingRegion", ImVec2(0, -footer_height_to_reserve), false, ImGuiWindowFlags_HorizontalScrollbar);

	if (ImGui::BeginPopupContextWindow())
	{
		if (ImGui::Selectable("Clear")) ClearLog();
		ImGui::EndPopup();
	}

	ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(4, 1));

	for (int i = 0; i < Console::State.Items.Size; i++)
	{
		const char* item = Console::State.Items[i];
		if (!Console::State.Filter.PassFilter(item))
			continue;

		ImVec4 color;
		bool has_color = false;
		if (strstr(item, "[error]")) { color = ImVec4(1.0f, 0.75f, 0.75f, 1.0f); has_color = true; }

		else if (strstr(item, "[info]")) { color = ImVec4(.75f, .75f, 1.0f, 1.0f); has_color = true; }

		else if (strstr(item, "[warn]")) { color = ImVec4(1.0f, 1.0f, .75f, 1.0f); has_color = true; }

		if (has_color)
			ImGui::PushStyleColor(ImGuiCol_Text, color);
		ImGui::TextUnformatted(item);
		if (has_color)
			ImGui::PopStyleColor();
	}

	ImGui::PopStyleVar();
	ImGui::EndChild();
	ImGui::End();
}

void Console::ClearLog()
{
	for (int i = 0; i < Console::State.Items.Size; i++)
		free(Console::State.Items[i]);

	Console::State.Items.clear();
}

void Console::WriteMessage(const char* fmt, ...)
{
	char buf[1024];
	va_list args;
	va_start(args, fmt);
	vsnprintf(buf, IM_ARRAYSIZE(buf), fmt, args);
	buf[IM_ARRAYSIZE(buf) - 1] = 0;
	va_end(args);
	Console::State.Items.push_back(Strdup(buf));
}