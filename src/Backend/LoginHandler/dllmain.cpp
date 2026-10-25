#include <windows.h>
#include <detours.h>
#include <string>
#include <cwctype>
#include <climits>

#define NOP 0x90

bool LoggedIn = false;
bool CreateCharacterMode = false;
bool CreationRequested = false;

typedef void* (__cdecl* Client_GetInstanceIfAny_t) ();
Client_GetInstanceIfAny_t Client_GetInstanceIfAny;

typedef std::string* (*Client_GetPlayerName_t) ();
Client_GetPlayerName_t Client_GetPlayerName;

typedef std::string* (*Client_GetPassword_t) ();
Client_GetPassword_t Client_GetPassword;

typedef void (*Client_SetCharID_t) (unsigned int charId);
Client_SetCharID_t Client_SetCharID;

typedef int(__thiscall* Client_ConnectToLH_t) (void* pClient);
Client_ConnectToLH_t Client_ConnectToLH;

typedef int(__thiscall* Client_LoginCharacter_t) (void* pClient);
Client_LoginCharacter_t Client_LoginCharacter;

typedef void(__thiscall* LoginModule_Initialize_t) (void* pLoginModule, bool loginAccount);
LoginModule_Initialize_t LoginModule_Initialize;

typedef void(__thiscall* LoginModule_Show_t) (void* pLoginModule, int state);
LoginModule_Show_t LoginModule_Show;

typedef void(__thiscall* LoginModule_CreateChar_t) (void* pLoginModule);
LoginModule_CreateChar_t LoginModule_CreateChar;

bool EnvironmentIs(const char* name, const char* expected)
{
    const char* value = std::getenv(name);
    return value != nullptr && strcmp(value, expected) == 0;
}

typedef void* (__thiscall* Utils_Assign_t) (std::string* string, const char* newString);
Utils_Assign_t Utils_Assign;

#include "StatusReport.h"

void NopCall(PVOID address) {
    DWORD d, ds;
    VirtualProtect(address, 5, PAGE_EXECUTE_READWRITE, &d);
    memset(address, NOP, 5); VirtualProtect(address, 5, d, &ds);
}

void __fastcall LoginModule_Initialize_Hook(void* pLoginModule, int edx, bool loginAccount)
{
    LoginModule_Initialize(pLoginModule, loginAccount);
    StatusSend("login-screen");

    Utils_Assign(Client_GetPlayerName(), std::getenv("AOUser"));
    Utils_Assign(Client_GetPassword(), std::getenv("AOPass"));
    int connected = Client_ConnectToLH(Client_GetInstanceIfAny());
    StatusSend("connect rc=%d", connected);
}

void __fastcall LoginModule_Show_Hook(void* pLoginModule, int edx, int state)
{
    StatusScreen(state);
    if (CreateCharacterMode)
    {
        // Keep the game's normal login/error/selection UI visible. ID 0 is never
        // passed to LoginCharacter: use the same handler as the New Char button.
        LoginModule_Show(pLoginModule, state);
        if (state == 3 && !CreationRequested)
        {
            CreationRequested = true;
            if (LoginModule_CreateChar != nullptr)
                LoginModule_CreateChar(pLoginModule);
            // Older client builds without this export remain at the visible
            // character list so the user can choose New Char themselves.
            LoggedIn = true;
        }
        return;
    }
    if (state == 3)
    {
        StatusSend("character-list");
        Client_SetCharID(atoi(std::getenv("AOCharId")));
        Client_LoginCharacter(Client_GetInstanceIfAny());
        StatusSend("character-requested");
        LoggedIn = true;
    }

    if (EnvironmentIs("QuietLaunch", "False"))
        LoginModule_Show(pLoginModule, state);
}

void PatchChat()
{
    DWORD BaseAddress = (DWORD)GetModuleHandle("GUI.dll");

    NopCall((PVOID)(BaseAddress + 0x8A3B0)); // Initial connect
    NopCall((PVOID)(BaseAddress + 0x8A596)); // Reconnect
}

void LoadAddresses()
{
    HMODULE hInterfaces = GetModuleHandle("Interfaces.dll");
    HMODULE hUtils = GetModuleHandle("Utils.dll");
    HMODULE hGUI = GetModuleHandle("GUI.dll");

    Client_GetInstanceIfAny = (Client_GetInstanceIfAny_t)GetProcAddress(hInterfaces, "?GetInstanceIfAny@Client_t@@SAPAV1@XZ");
    Client_GetPlayerName = (Client_GetPlayerName_t)GetProcAddress(hInterfaces, "?GetPlayerName@Client_t@@SAABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    Client_GetPassword = (Client_GetPassword_t)GetProcAddress(hInterfaces, "?GetPassword@Client_t@@SAABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@XZ");
    Client_SetCharID = (Client_SetCharID_t)GetProcAddress(hInterfaces, "?SetCharID@Client_t@@SAXI@Z");
    Client_ConnectToLH = (Client_ConnectToLH_t)GetProcAddress(hInterfaces, "?ConnectToLH@Client_t@@QAEHXZ");
    Client_LoginCharacter = (Client_LoginCharacter_t)GetProcAddress(hInterfaces, "?LoginCharacter@Client_t@@QAEXXZ");
    Utils_Assign = (Utils_Assign_t)GetProcAddress(hUtils, "?assign@String@@QAEAAV1@PBD@Z");
    LoginModule_CreateChar = (LoginModule_CreateChar_t)GetProcAddress(hGUI, "?SlotCreateChar@LoginModule_c@@AAEXXZ");
}

int FindAndAttach(LPCSTR pszModule, LPCSTR pszFunction, PVOID* ppPointer, PVOID pDetour)
{
    auto function = DetourFindFunction(pszModule, pszFunction);

    if (function == NULL)
        return ERROR_INVALID_HANDLE;

    *ppPointer = function;
    return DetourAttach(ppPointer, pDetour);
}

// Creation detection stays independent of AOSharp and preference-folder layout.
// Its only output is one bounded, non-credential message to this launch's pipe.
bool IsCreationPipeName(const std::wstring& name)
{
    const std::wstring prefix = L"AOQuickLauncher.Created.";
    if (name.size() != prefix.size() + 32 || name.compare(0, prefix.size(), prefix) != 0)
        return false;
    for (size_t index = prefix.size(); index < name.size(); ++index)
    {
        wchar_t value = name[index];
        if (!((value >= L'0' && value <= L'9') || (value >= L'a' && value <= L'f')
            || (value >= L'A' && value <= L'F')))
            return false;
    }
    return true;
}

bool TryCreatedCharacterName(const std::wstring& title, std::wstring& name)
{
    const std::wstring prefix = L"Anarchy Online - ";
    name.clear();
    if (title.compare(0, prefix.size(), prefix) != 0) return false;
    size_t first = prefix.size();
    size_t last = title.size();
    while (first < last && iswspace(title[first])) ++first;
    while (last > first && iswspace(title[last - 1])) --last;
    if (last - first == 0 || last - first > 64) return false;
    for (size_t index = first; index < last; ++index)
        if (title[index] < 0x20 || (title[index] >= 0x7f && title[index] <= 0x9f) || iswcntrl(title[index])) return false;
    name.assign(title, first, last - first);
    return true;
}

struct CreationWindowName
{
    DWORD ProcessId;
    std::wstring Name;
    bool Ambiguous = false;
};

BOOL CALLBACK FindCreationWindow(HWND window, LPARAM parameter)
{
    auto result = reinterpret_cast<CreationWindowName*>(parameter);
    DWORD processId = 0;
    GetWindowThreadProcessId(window, &processId);
    if (processId != result->ProcessId || !IsWindowVisible(window)) return TRUE;

    // WM_GETTEXT can otherwise block this worker indefinitely on a busy UI thread.
    wchar_t title[256] = {};
    DWORD_PTR copied = 0;
    if (!SendMessageTimeoutW(window, WM_GETTEXT, _countof(title), reinterpret_cast<LPARAM>(title),
        SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &copied) || copied >= _countof(title) - 1)
        return TRUE;
    std::wstring name;
    if (!TryCreatedCharacterName(title, name)) return TRUE;
    if (!result->Name.empty() && result->Name != name)
    {
        result->Ambiguous = true;
        return FALSE;
    }
    result->Name = name;
    return TRUE;
}

typedef unsigned int (__cdecl* Client_GetCharID_t)();

bool TryReadCreatedCharacterId(Client_GetCharID_t getCharId, unsigned int& characterId)
{
    characterId = 0;
    // Unsupported clients must lose automatic detection, not their game process.
    __try
    {
        characterId = getCharId();
        return characterId > 0 && characterId <= INT_MAX;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

bool TryBuildCreationMessage(unsigned int characterId, const std::wstring& name, std::string& message)
{
    message.clear();
    std::wstring validated;
    if (characterId == 0 || characterId > INT_MAX
        || !TryCreatedCharacterName(L"Anarchy Online - " + name, validated) || validated != name)
        return false;
    int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, name.data(), static_cast<int>(name.size()),
        nullptr, 0, nullptr, nullptr);
    if (length <= 0 || length > 256) return false;
    std::string utf8(static_cast<size_t>(length), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, name.data(), static_cast<int>(name.size()),
        &utf8[0], length, nullptr, nullptr) != length) return false;
    message = "AO_NEXUS_CHAR_V1\n" + std::to_string(characterId) + "\n" + utf8 + "\n";
    return message.size() <= 512;
}

bool SendCreationMessage(const std::wstring& pipeName, const std::string& message)
{
    if (!IsCreationPipeName(pipeName) || message.empty() || message.size() > 512) return false;
    std::wstring pipePath = L"\\\\.\\pipe\\" + pipeName;
    ULONGLONG started = GetTickCount64();
    while (GetTickCount64() - started < 10000)
    {
        HANDLE pipe = CreateFileW(pipePath.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
        if (pipe != INVALID_HANDLE_VALUE)
        {
            // A receiver acknowledgement prevents CloseHandle from discarding a
            // buffered message before it is read. Both reads and writes stay bounded.
            DWORD mode = PIPE_NOWAIT;
            DWORD written = 0;
            bool success = SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr)
                && WriteFile(pipe, message.data(), static_cast<DWORD>(message.size()), &written, nullptr)
                && written == message.size();
            char acknowledgement[3] = {};
            DWORD acknowledged = 0;
            while (success && acknowledged < sizeof(acknowledgement) && GetTickCount64() - started < 10000)
            {
                DWORD count = 0;
                BOOL read = ReadFile(pipe, acknowledgement + acknowledged,
                    static_cast<DWORD>(sizeof(acknowledgement)) - acknowledged, &count, nullptr);
                if (read) acknowledged += count;
                else if (GetLastError() != ERROR_NO_DATA) break;
                if (acknowledged < sizeof(acknowledgement)) Sleep(20);
            }
            success = success && acknowledged == sizeof(acknowledgement)
                && acknowledgement[0] == 'O' && acknowledgement[1] == 'K' && acknowledgement[2] == '\n';
            CloseHandle(pipe);
            return success;
        }
        DWORD error = GetLastError();
        if (error != ERROR_PIPE_BUSY && error != ERROR_FILE_NOT_FOUND) return false;
        if (error == ERROR_PIPE_BUSY) WaitNamedPipeW(pipePath.c_str(), 100);
        else Sleep(100);
    }
    return false;
}

void ReportCreatedCharacter()
{
    wchar_t pipeNameBuffer[96] = {};
    DWORD pipeLength = GetEnvironmentVariableW(L"AOCharacterPipe", pipeNameBuffer, _countof(pipeNameBuffer));
    if (pipeLength == 0 || pipeLength >= _countof(pipeNameBuffer)) return;
    std::wstring pipeName(pipeNameBuffer, pipeLength);
    if (!IsCreationPipeName(pipeName)) return;
    HMODULE interfacesModule = GetModuleHandleW(L"Interfaces.dll");
    if (interfacesModule == nullptr) return;
    auto getCharId = reinterpret_cast<Client_GetCharID_t>(GetProcAddress(interfacesModule, "?GetCharID@Client_t@@SAIXZ"));
    if (getCharId == nullptr) return;

    unsigned int previousId = 0;
    std::wstring previousName;
    int stableSamples = 0;
    ULONGLONG started = GetTickCount64();
    while (GetTickCount64() - started < 60ULL * 60ULL * 1000ULL)
    {
        unsigned int characterId = 0;
        CreationWindowName windowName;
        windowName.ProcessId = GetCurrentProcessId();
        if (TryReadCreatedCharacterId(getCharId, characterId))
            EnumWindows(FindCreationWindow, reinterpret_cast<LPARAM>(&windowName));
        // Recheck the ID after reading the title so a selection change cannot
        // pair a new ID with the previous character's window name.
        unsigned int checkedId = 0;
        if (characterId > 0 && !windowName.Name.empty() && !windowName.Ambiguous
            && TryReadCreatedCharacterId(getCharId, checkedId) && checkedId == characterId)
        {
            stableSamples = characterId == previousId && windowName.Name == previousName ? stableSamples + 1 : 1;
            previousId = characterId;
            previousName = windowName.Name;
            if (stableSamples >= 3)
            {
                std::string message;
                if (TryBuildCreationMessage(characterId, windowName.Name, message))
                    SendCreationMessage(pipeName, message);
                return;
            }
        }
        else
        {
            stableSamples = 0;
            previousId = 0;
            previousName.clear();
        }
        Sleep(500);
    }
}

#include "CampControl.h"

void ThreadMain(HMODULE hModule)
{
    StatusOpen();
    LoadAddresses();
    CreateCharacterMode = EnvironmentIs("AOCreateCharacter", "True");
    // Which client exports were found: an unexpected client version shows up here
    // instead of as a silent wait at the login screen.
    StatusSend("exports instance=%d name=%d password=%d charid=%d connect=%d login=%d assign=%d",
        Client_GetInstanceIfAny != nullptr, Client_GetPlayerName != nullptr, Client_GetPassword != nullptr,
        Client_SetCharID != nullptr, Client_ConnectToLH != nullptr, Client_LoginCharacter != nullptr, Utils_Assign != nullptr);

    if (EnvironmentIs("NoChat", "True"))
        PatchChat();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    int initializeHook = FindAndAttach("GUI.dll", "?SlotInitialize@LoginModule_c@@AAEX_N@Z", (void**)&LoginModule_Initialize, LoginModule_Initialize_Hook);
    int showHook = FindAndAttach("GUI.dll", "?Show@LoginModule_c@@AAEXW4State_e@1@@Z", (void**)&LoginModule_Show, LoginModule_Show_Hook);
    LONG committed = DetourTransactionCommit();
    StatusSend("hooks initialize=%d show=%d commit=%ld", initializeHook, showHook, committed);

    while (!LoggedIn)
        Sleep(100);

    DetourTransactionBegin();
    DetourDetach((PVOID*)&LoginModule_Initialize, (PVOID)LoginModule_Initialize_Hook);
    DetourDetach((PVOID*)&LoginModule_Show, (PVOID)LoginModule_Show_Hook);
    DetourTransactionCommit();
    StatusSend("hooks-removed");
    StatusClose();

    // Detection failure must never terminate an otherwise healthy game client.
    if (CreateCharacterMode)
    {
        try { ReportCreatedCharacter(); }
        catch (...) { }
    }

    // Keep the module alive only when the UI-thread camp timer references its code.
    // Windows tears down the timer and event handles when the game process exits.
    bool campInstalled = false;
    try { campInstalled = InstallCampControl(); }
    catch (...) { }
    if (campInstalled) ExitThread(0);
    FreeLibraryAndExitThread(hModule, NULL);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        CreateThread(NULL, NULL, (LPTHREAD_START_ROUTINE)ThreadMain, hModule, NULL, NULL);
        break;
    }
    return TRUE;
}
