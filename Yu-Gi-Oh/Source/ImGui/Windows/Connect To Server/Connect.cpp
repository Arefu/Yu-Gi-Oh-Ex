#include "ImGui/Windows/Connect To Server/Connect.h"

#include "ImGui/YuGiOh_ImGui.h"

void Connect::Show_ConnectWindow(BOOL* g_show_connect_window)
{
	ImGui::SetNextWindowSize(ImVec2(250, 300), ImGuiCond_FirstUseEver);
	if (!ImGui::Begin("Connect To Server...", reinterpret_cast<bool*>(*g_show_connect_window),
		ImGuiWindowFlags_NoCollapse))
	{
		ImGui::End();
		return;
	}
	static char buf[MAX_PATH] = "";
	ImGui::InputText("<IP:PORT>", buf, IM_ARRAYSIZE(buf));

	if (ImGui::Button(("Connect")))
	{
		std::string Host = buf;
		Client::ConectToHost(Host);
		*g_show_connect_window = false;
		ImGui::End();
		return;
	}
	ImGui::End();
}