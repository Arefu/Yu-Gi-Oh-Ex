#include "ImGui/Windows/Connect To Server/Connect.h"

#include "ImGui/YuGiOh_ImGui.h"

void Connect::Show_ConnectWindow()
{
	ImGui::SetNextWindowSize(ImVec2(250, 300), ImGuiCond_FirstUseEver);
	if (!ImGui::Begin("Connect To Server...", &YuGiOh_ImGui::g_ShowConnectWindow, ImGuiWindowFlags_NoCollapse))
	{
		ImGui::End();
		return;
	}
	static char buf[MAX_PATH] = "";
	ImGui::InputText("<IP:PORT>", buf, IM_ARRAYSIZE(buf));

	if (ImGui::Button(("Connect")))
	{
		std::string Host = buf;
		ImGui::End();
		return;
	}
	ImGui::End();
}