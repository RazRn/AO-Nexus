#pragma once

// One-way login progress for AO Nexus diagnostics. Each line names a login stage only:
// never the username, password, character name or any other request data. If the pipe is
// missing, busy or full, reports are dropped; the login itself never depends on them.
#include <cstdio>
#include <cstdarg>

static HANDLE statusPipe = INVALID_HANDLE_VALUE;
static CRITICAL_SECTION statusLock;
static bool statusReady = false;
static ULONGLONG statusStarted = 0;
static int statusLastScreen = INT_MIN;

static bool IsStatusPipeName(const std::wstring& name)
{
    const std::wstring prefix = L"AONexus.Status.";
    if (name.size() != prefix.size() + 32 || name.compare(0, prefix.size(), prefix) != 0)
        return false;
    for (size_t index = prefix.size(); index < name.size(); ++index)
    {
        wchar_t value = name[index];
        if (!((value >= L'0' && value <= L'9') || (value >= L'a' && value <= L'f')))
            return false;
    }
    return true;
}

static void StatusOpen()
{
    InitializeCriticalSection(&statusLock);
    statusReady = true;
    statusStarted = GetTickCount64();
    wchar_t nameBuffer[64] = {};
    DWORD length = GetEnvironmentVariableW(L"AONexusStatusPipe", nameBuffer, _countof(nameBuffer));
    if (length == 0 || length >= _countof(nameBuffer)) return;
    std::wstring name(nameBuffer, length);
    if (!IsStatusPipeName(name)) return;
    std::wstring path = L"\\\\.\\pipe\\" + name;
    // The server is created before this process starts; allow a short wait only.
    for (int attempt = 0; attempt < 20; ++attempt)
    {
        HANDLE pipe = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
        if (pipe != INVALID_HANDLE_VALUE)
        {
            DWORD mode = PIPE_READMODE_BYTE | PIPE_NOWAIT;
            SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);
            statusPipe = pipe;
            return;
        }
        DWORD error = GetLastError();
        if (error == ERROR_PIPE_BUSY) WaitNamedPipeW(path.c_str(), 100);
        else if (error == ERROR_FILE_NOT_FOUND) Sleep(100);
        else return;
    }
}

// Callers pass fixed stage names and numbers only.
static void StatusSend(const char* format, ...)
{
    if (!statusReady) return;
    char text[160] = {};
    va_list arguments;
    va_start(arguments, format);
    int written = _vsnprintf_s(text, sizeof(text), _TRUNCATE, format, arguments);
    va_end(arguments);
    if (written <= 0) return;
    char line[200] = {};
    int length = _snprintf_s(line, sizeof(line), _TRUNCATE, "AO_NEXUS_STATUS_V1 %llu %s\n",
        GetTickCount64() - statusStarted, text);
    if (length <= 0) return;
    EnterCriticalSection(&statusLock);
    if (statusPipe != INVALID_HANDLE_VALUE)
    {
        DWORD sent = 0;
        // Non-blocking: a full or closed pipe drops the report instead of stalling the game.
        if (!WriteFile(statusPipe, line, static_cast<DWORD>(length), &sent, nullptr) && GetLastError() != ERROR_NO_DATA)
        {
            CloseHandle(statusPipe);
            statusPipe = INVALID_HANDLE_VALUE;
        }
    }
    LeaveCriticalSection(&statusLock);
}

static void StatusScreen(int state)
{
    if (state == statusLastScreen) return;
    statusLastScreen = state;
    StatusSend("screen %d", state);
}

static void StatusClose()
{
    if (!statusReady) return;
    EnterCriticalSection(&statusLock);
    if (statusPipe != INVALID_HANDLE_VALUE)
    {
        CloseHandle(statusPipe);
        statusPipe = INVALID_HANDLE_VALUE;
    }
    LeaveCriticalSection(&statusLock);
}
