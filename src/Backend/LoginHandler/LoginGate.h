#pragma once

// Preload the client without blocking its UI thread. A private local event
// releases authentication after the previous character's zone admission.
namespace NexusLoginGate {
    static HANDLE ready = nullptr, cancel = nullptr, parent = nullptr;
    static UINT_PTR timer = 0;
    static ULONGLONG deadline = 0;
    static void (*submit)() = nullptr;
    static void (*abortLogin)() = nullptr;

    static void Close() {
        if (timer) { KillTimer(nullptr, timer); timer = 0; }
        if (ready) { CloseHandle(ready); ready = nullptr; }
        if (cancel) { CloseHandle(cancel); cancel = nullptr; }
        if (parent) { CloseHandle(parent); parent = nullptr; }
    }
    static void CALLBACK Poll(HWND, UINT, UINT_PTR, DWORD) {
        if (!timer) return; // KillTimer can leave an already queued WM_TIMER.
        // Failure wins if cancellation and readiness arrive together.
        bool failed = WaitForSingleObject(cancel, 0) != WAIT_TIMEOUT
            || WaitForSingleObject(parent, 0) != WAIT_TIMEOUT || GetTickCount64() >= deadline;
        DWORD state = WaitForSingleObject(ready, 0);
        if (failed || state == WAIT_FAILED) { Close(); StatusSend("login-gate cancelled"); abortLogin(); }
        else if (state == WAIT_OBJECT_0) { Close(); StatusSend("login-gate released"); submit(); }
    }
    static bool Begin(void (*onReady)(), void (*onCancel)()) {
        wchar_t name[100] = {}, pid[16] = {};
        DWORD length = GetEnvironmentVariableW(L"AONexusLoginGate", name, _countof(name));
        if (!length) return false;
        submit = onReady; abortLogin = onCancel;
        const std::wstring prefix = L"Local\\AONexus.LoginGate.";
        bool valid = length < _countof(name) && length == prefix.size() + 32
            && std::wstring(name).compare(0, prefix.size(), prefix) == 0;
        for (size_t i = prefix.size(); valid && i < length; ++i)
            valid = (name[i] >= L'0' && name[i] <= L'9') || (name[i] >= L'a' && name[i] <= L'f');
        DWORD pidLength = GetEnvironmentVariableW(L"AONexusLoginGateParent", pid, _countof(pid));
        if (valid && pidLength > 0 && pidLength < _countof(pid)) {
            std::wstring root(name);
            ready = OpenEventW(SYNCHRONIZE, FALSE, (root + L".ready").c_str());
            cancel = OpenEventW(SYNCHRONIZE, FALSE, (root + L".cancel").c_str());
            parent = OpenProcess(SYNCHRONIZE, FALSE, wcstoul(pid, nullptr, 10));
        }
        if (!ready || !cancel || !parent) { Close(); StatusSend("login-gate unavailable"); abortLogin(); return true; }
        deadline = GetTickCount64() + 120000;
        timer = SetTimer(nullptr, 0, 15, Poll);
        if (!timer) { Close(); StatusSend("login-gate unavailable"); abortLogin(); return true; }
        StatusSend("login-gate waiting");
        Poll(nullptr, 0, 0, 0);
        return true;
    }
}
