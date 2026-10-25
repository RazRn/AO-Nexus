#include <windows.h>
#include <string>
#include <climits>
#include <iostream>
bool CreateCharacterMode = false;
typedef unsigned int (__cdecl* Client_GetCharID_t)();
bool TryCreatedCharacterName(const wchar_t*, std::wstring&) { return false; }
#include "../src/Backend/LoginHandler/CampControl.h"
static unsigned int characterId = 17;
static bool zoning = false, allowCamp = true, engineAvailable = true;
static int calls = 0, logoutCalls = 0, logoutStatus = 0;
static DWORD callingThread;
static unsigned int __cdecl Character() { return characterId; }
static void* __cdecl Engine() { return engineAvailable ? reinterpret_cast<void*>(1) : nullptr; }
struct EngineFixture {
    bool Start() { ++calls; callingThread = GetCurrentThreadId(); return allowCamp; }
};
static void __cdecl Logout(int status) { ++logoutCalls; logoutStatus = status; }
static void Check(bool condition, const char* message) { if (!condition) { std::cerr << message << std::endl; ExitProcess(1); } }
static DWORD WINAPI InstallTimer(void* window) { Check(SetTimer((HWND)window, 0xA04E1234, 10, CampTimer) != 0, "timer registration"); return 0; }
int main(int argc, char** argv)
{
    campCharacter = Character; campEngine = Engine; campTeleporting = &zoning; campExpectedCharacter = 17;
    auto member = &EngineFixture::Start;
    static_assert(sizeof(member) == sizeof(campStart));
    memcpy(&campStart, &member, sizeof(member));
    campLogout = Logout;
    if (argc == 2)
    {
        // Inert end-to-end fixture: named like a game only inside a temporary test folder.
        std::string token(argv[1]); Check(token.size() == 32, "fixture token");
        std::wstring wide(token.begin(), token.end());
        std::wstring prefix = L"Local\\AONexus.Camp.v1." + std::to_wstring(GetCurrentProcessId()) + L"." + wide + L".";
        campRequest = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"request").c_str());
        campAccepted = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"accepted").c_str());
        campRejected = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"rejected").c_str());
        WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW; wc.lpszClassName = L"NexusCampFixture";
        RegisterClassW(&wc);
        HWND window = CreateWindowW(wc.lpszClassName, L"Anarchy Online - Fixture", WS_OVERLAPPEDWINDOW, 0, 0, 260, 100, nullptr, nullptr, nullptr, nullptr);
        ShowWindow(window, SW_SHOWNOACTIVATE);
        HANDLE worker = CreateThread(nullptr, 0, InstallTimer, window, 0, nullptr);
        WaitForSingleObject(worker, 2000); CloseHandle(worker);
        campLastAttempt = GetTickCount64() - 30001;
        ULONGLONG until = GetTickCount64() + 15000;
        while (IsWindow(window) && GetTickCount64() < until)
        { MSG msg; while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&msg); Sleep(5); }
        return 0;
    }
    characterId = 18; Check(!ExecuteNormalCamp() && calls == 0, "wrong character must not camp");
    characterId = 17; zoning = true; Check(!ExecuteNormalCamp() && calls == 0, "zoning must not camp");
    zoning = false; engineAvailable = false; Check(!ExecuteNormalCamp() && calls == 0, "no engine must not camp");
    engineAvailable = true; allowCamp = false; Check(!ExecuteNormalCamp() && logoutCalls == 0, "rejected camp must not set logout");
    allowCamp = true; Check(ExecuteNormalCamp() && logoutCalls == 1 && logoutStatus == 2, "normal delayed camp");
    campRequest = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    campAccepted = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    campRejected = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW; wc.lpszClassName = L"NexusCampFixture";
    RegisterClassW(&wc);
    HWND window = CreateWindowW(wc.lpszClassName, L"inert test window", 0, 0, 0, 1, 1, HWND_MESSAGE, nullptr, nullptr, nullptr);
    HANDLE worker = CreateThread(nullptr, 0, InstallTimer, window, 0, nullptr);
    WaitForSingleObject(worker, 2000); CloseHandle(worker);
    campLastAttempt = GetTickCount64() - 30001;
    SetEvent(campRequest);
    DWORD until = GetTickCount() + 2000; bool accepted = false;
    while (GetTickCount() < until && !accepted)
    {
        MSG msg; while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&msg);
        accepted = WaitForSingleObject(campAccepted, 0) == WAIT_OBJECT_0; Sleep(5);
    }
    Check(accepted && callingThread == GetCurrentThreadId(), "camp must execute on owning window thread, not worker");
    int before = calls; SetEvent(campRequest); CampTimer(window, 0, 0, 0);
    Check(calls == before && WaitForSingleObject(campRejected, 0) == WAIT_OBJECT_0, "repeats must not reset countdown");
    KillTimer(window, 0xA04E1234); DestroyWindow(window);
    CloseHandle(campRequest); CloseHandle(campAccepted); CloseHandle(campRejected);
    std::cout << "PASS: native camp guards, normal logout status, UI-thread timer dispatch, duplicate suppression (8 checks)." << std::endl;
}
