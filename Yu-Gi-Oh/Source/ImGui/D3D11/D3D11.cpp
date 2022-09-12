#include "ImGui/D3D11/D3D11.h"

Address DirectX::_ImGuiD3D11SetupPatchAddres;
Address YuGiOh_ImGui::_ImGuiPresentAddress;

IDXGISwapChain* DirectX::pD3D11SwapChain;
ID3D11Device* DirectX::pD3D11Device;
ID3D11DeviceContext* DirectX::pD3D11DeviceContext;

void DirectX::Patch_ImGUi_D3D11Setup(BYTE* a1)
{
	typedef __int64 sub_1408CA830(BYTE*);
	sub_1408CA830* func_1408CA830 = (sub_1408CA830*)DirectX::_ImGuiD3D11SetupPatchAddres;

	D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL_11_0;
	DXGI_SWAP_CHAIN_DESC swapChainDesc;
	ZeroMemory(&swapChainDesc, sizeof(swapChainDesc));
	swapChainDesc.BufferCount = 1;
	swapChainDesc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
	swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
	swapChainDesc.OutputWindow = GetForegroundWindow();
	swapChainDesc.SampleDesc.Count = 1;
	swapChainDesc.Windowed = (GetWindowLong(GetForegroundWindow(), GWL_STYLE) & (WS_POPUP != 0)) ? false : true;
	swapChainDesc.BufferDesc.ScanlineOrdering = DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED;
	swapChainDesc.BufferDesc.Scaling = DXGI_MODE_SCALING_UNSPECIFIED;
	swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

	D3D11CreateDeviceAndSwapChain(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, NULL, &featureLevel, 1, D3D11_SDK_VERSION, &swapChainDesc, &DirectX::pD3D11SwapChain, &DirectX::pD3D11Device, NULL, &DirectX::pD3D11DeviceContext);

	void** vmt = *(void***)DirectX::pD3D11SwapChain;

	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());

	YuGiOh_ImGui::_ImGuiPresentAddress = reinterpret_cast<Address>(vmt[8]);
	DetourAttach((PVOID*)(&YuGiOh_ImGui::_ImGuiPresentAddress), YuGiOh_ImGui::Start_DearImGui);

	DetourTransactionCommit();

	func_1408CA830(a1);
}

void DirectX::Set_ImGuiD3D11SetupPatchAddress(Address Address)
{
	DirectX::_ImGuiD3D11SetupPatchAddres = Address;
}