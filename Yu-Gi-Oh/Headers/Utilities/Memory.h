#pragma once

typedef LONG NTSTATUS;
typedef DWORD KPRIORITY;
typedef WORD UWORD;

class Memory
{
public:
	static int GetSceneValue();
	static void SetSceneValue(DWORD);
private:
	static DWORD GetThreadStackAddr(int);
	static DWORD GetThreadStartAddress(HANDLE, HANDLE);
	static void* GetThreadStackTopAddress_x86(HANDLE, HANDLE);
	typedef struct _CLIENT_ID
	{
		PVOID UniqueProcess;
		PVOID UniqueThread;
	} CLIENT_ID, * PCLIENT_ID;
	enum THREADINFOCLASS
	{
		ThreadBasicInformation,
	};
	typedef struct _THREAD_BASIC_INFORMATION
	{
		NTSTATUS                ExitStatus;
		PVOID                   TebBaseAddress;
		CLIENT_ID               ClientId;
		KAFFINITY               AffinityMask;
		KPRIORITY               Priority;
		KPRIORITY               BasePriority;
	} THREAD_BASIC_INFORMATION, * PTHREAD_BASIC_INFORMATION;
};