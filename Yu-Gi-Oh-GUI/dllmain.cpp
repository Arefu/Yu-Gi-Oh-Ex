#include <windows.h>
#include <dxgi.h>
#include <thread>
#include <chrono>
#include <iostream>

#include <detours.h>



// Global pointer to hold the IDXGISwapChain instance
IDXGISwapChain* g_pSwapChain = nullptr;

// Known address of the IDXGISwapChain in the target application
uintptr_t pSwapChainAddress = 0x14332D380;

// Function to wait for IDXGISwapChain initialization
void WaitForSwapChainInitialization()
{
    while (!g_pSwapChain)
    {
        g_pSwapChain = reinterpret_cast<IDXGISwapChain*>(pSwapChainAddress);
       
        while (true)
        {
            std::cout << "IDXGISwapChain initialized at: " << g_pSwapChain << std::endl;
            break; // Exit the loop once initialized
        }


    }
}

BOOL Get_True()
{
	return TRUE;
}
typedef __int64 Address;

// DllMain entry point
BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    DetourRestoreAfterWith(); 
    AllocConsole();
	FILE* f;
	freopen_s(&f, "CONOUT$", "w", stdout);
	freopen_s(&f, "CONOUT$", "w", stderr);


    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:

          
        std::thread(WaitForSwapChainInitialization).detach();

        break;
    case DLL_THREAD_ATTACH:
     case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}