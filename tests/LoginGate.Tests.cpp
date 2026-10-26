#include <windows.h>
#include <string>
#include <cwchar>
#include <cstdlib>
#include <cstdio>
static void StatusSend(const char*, ...) {}
#include "../src/Backend/LoginHandler/LoginGate.h"
static int submitted = 0, cancelled = 0, checks = 0;
static void Submit() { ++submitted; }
static void Cancel() { ++cancelled; }
static void Check(bool value) { ++checks; if (!value) std::abort(); }
static void Poll() { NexusLoginGate::Poll(nullptr, 0, 0, 0); }
int main() {
    SetEnvironmentVariableW(L"AONexusLoginGate", nullptr);
    Check(!NexusLoginGate::Begin(Submit, Cancel));
    std::wstring root = L"Local\\AONexus.LoginGate.00000000000000000000000000000000";
    // Unique test name, with no account or credential data.
    wchar_t suffix[33]; swprintf_s(suffix, L"%032x", GetCurrentProcessId());
    root.replace(root.size() - 32, 32, suffix);
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, (root + L".ready").c_str());
    HANDLE cancel = CreateEventW(nullptr, TRUE, FALSE, (root + L".cancel").c_str());
    Check(ready && cancel);
    wchar_t pid[16]; swprintf_s(pid, L"%u", GetCurrentProcessId());
    SetEnvironmentVariableW(L"AONexusLoginGate", root.c_str());
    SetEnvironmentVariableW(L"AONexusLoginGateParent", pid);
    Check(NexusLoginGate::Begin(Submit, Cancel));
    for (int i=0; i<100; ++i) Poll();
    Check(submitted == 0 && cancelled == 0);
    SetEvent(ready); Poll(); Poll();
    Check(submitted == 1 && cancelled == 0);
    Check(!NexusLoginGate::timer && !NexusLoginGate::ready && !NexusLoginGate::cancel && !NexusLoginGate::parent);
    ResetEvent(ready); Check(NexusLoginGate::Begin(Submit, Cancel));
    SetEvent(ready); SetEvent(cancel); Poll(); Poll();
    Check(submitted == 1 && cancelled == 1);
    ResetEvent(ready); ResetEvent(cancel); Check(NexusLoginGate::Begin(Submit, Cancel));
    NexusLoginGate::deadline = 0; Poll();
    Check(submitted == 1 && cancelled == 2);
    Check(NexusLoginGate::Begin(Submit, Cancel));
    CloseHandle(NexusLoginGate::parent);
    NexusLoginGate::parent = CreateEventW(nullptr, TRUE, TRUE, nullptr);
    Poll(); Check(submitted == 1 && cancelled == 3);
    SetEnvironmentVariableW(L"AONexusLoginGate", L"bad-name");
    Check(NexusLoginGate::Begin(Submit, Cancel)); Check(submitted == 1 && cancelled == 4);
    SetEnvironmentVariableW(L"AONexusLoginGate", root.c_str());
    CloseHandle(ready); CloseHandle(cancel);
    Check(NexusLoginGate::Begin(Submit, Cancel)); Check(submitted == 1 && cancelled == 5);
    printf("PASS: %d login gate checks: withheld, once-only release, cancellation precedence, timeout, parent exit, missing events.\n", checks);
}
