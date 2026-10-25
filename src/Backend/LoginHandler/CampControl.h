#pragma once

// Fixed /camp operation on the game's window thread. This does not require AOSharp,
// synthesize keystrokes, execute arbitrary text, or force the game to exit.
typedef void* (__cdecl* CampGetEngine)();
typedef bool (__thiscall* CampStart)(void*);
typedef void (__cdecl* CampSetLogout)(int);
static CampGetEngine campEngine = nullptr;
static CampStart campStart = nullptr;
static CampSetLogout campLogout = nullptr;
static Client_GetCharID_t campCharacter = nullptr;
static bool* campTeleporting = nullptr;
static HANDLE campRequest = nullptr, campAccepted = nullptr, campRejected = nullptr;
static unsigned int campExpectedCharacter = 0;
static ULONGLONG campLastAttempt = 0;

static bool ExecuteNormalCamp()
{
    __try
    {
        if (campCharacter() != campExpectedCharacter || *campTeleporting) return false;
        void* engine = campEngine();
        if (!engine || !campStart(engine)) return false;
        // Normal timed logout, as used by AOSharp Game.Camp(false). Never ActivateGameClosing.
        campLogout(2);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static void CALLBACK CampTimer(HWND window, UINT, UINT_PTR timer, DWORD)
{
    if (WaitForSingleObject(campRequest, 0) != WAIT_OBJECT_0) return;
    ULONGLONG now = GetTickCount64();
    bool accepted = false;
    // Repeated shortcuts must not reset the logout countdown.
    if (now - campLastAttempt >= 30000)
    {
        campLastAttempt = now;
        accepted = ExecuteNormalCamp();
    }
    SetEvent(accepted ? campAccepted : campRejected);
}

static BOOL CALLBACK FindCampWindow(HWND window, LPARAM parameter)
{
    DWORD pid = 0;
    GetWindowThreadProcessId(window, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(window)) return TRUE;
    wchar_t title[256] = {};
    DWORD_PTR copied = 0;
    if (!SendMessageTimeoutW(window, WM_GETTEXT, _countof(title), reinterpret_cast<LPARAM>(title),
        SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &copied)) return TRUE;
    std::wstring name;
    if (!TryCreatedCharacterName(title, name)) return TRUE;
    *reinterpret_cast<HWND*>(parameter) = window;
    return FALSE;
}

static bool InstallCampControl()
{
    if (CreateCharacterMode) return false;
    wchar_t token[33] = {}, character[16] = {};
    if (GetEnvironmentVariableW(L"AONexusCampToken", token, _countof(token)) != 32) return false;
    SetEnvironmentVariableW(L"AONexusCampToken", nullptr);
    for (int i = 0; i < 32; ++i)
        if (!((token[i] >= L'0' && token[i] <= L'9') || (token[i] >= L'a' && token[i] <= L'f'))) return false;
    DWORD length = GetEnvironmentVariableW(L"AOCharId", character, _countof(character));
    if (!length || length >= _countof(character)) return false;
    wchar_t* end = nullptr;
    campExpectedCharacter = wcstoul(character, &end, 10);
    if (!end || *end || !campExpectedCharacter || campExpectedCharacter > INT_MAX) return false;
    campEngine = reinterpret_cast<CampGetEngine>(GetProcAddress(GetModuleHandleW(L"N3.dll"), "?GetInstance@n3Engine_t@@SAPAV1@XZ"));
    campStart = reinterpret_cast<CampStart>(GetProcAddress(GetModuleHandleW(L"Gamecode.dll"), "?N3Msg_StartCamping@n3EngineClientAnarchy_t@@QAE_NXZ"));
    campLogout = reinterpret_cast<CampSetLogout>(GetProcAddress(GetModuleHandleW(L"GUI.dll"), "?SetTimeLogoutStatus@FlowControlModule_t@@SAXW4QuitStatus_e@1@@Z"));
    campTeleporting = reinterpret_cast<bool*>(GetProcAddress(GetModuleHandleW(L"GUI.dll"), "?m_isTeleporting@FlowControlModule_t@@2_NA"));
    campCharacter = reinterpret_cast<Client_GetCharID_t>(GetProcAddress(GetModuleHandleW(L"Interfaces.dll"), "?GetCharID@Client_t@@SAIXZ"));
    if (!campEngine || !campStart || !campLogout || !campTeleporting || !campCharacter) return false;
    HWND window = nullptr;
    for (int attempt = 0; attempt < 240 && !window; ++attempt)
    {
        EnumWindows(FindCampWindow, reinterpret_cast<LPARAM>(&window));
        if (!window) Sleep(500);
    }
    if (!window) return false;
    std::wstring prefix = L"Local\\AONexus.Camp.v1." + std::to_wstring(GetCurrentProcessId()) + L"." + token + L".";
    // Default process-token DACL: local user/session; random per-process capability.
    campRequest = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"request").c_str());
    campAccepted = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"accepted").c_str());
    campRejected = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"rejected").c_str());
    UINT_PTR timerId = 0xA04E0000u | (wcstoul(token + 28, nullptr, 16) & 0xffffu);
    if (campRequest && campAccepted && campRejected && SetTimer(window, timerId, 250, CampTimer)) return true;
    if (campRequest) CloseHandle(campRequest);
    if (campAccepted) CloseHandle(campAccepted);
    if (campRejected) CloseHandle(campRejected);
    return false;
}
